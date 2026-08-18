using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.OptionsIntraday;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

/// <summary>Nearest FUTSTK contract + mapped entry/exit/targets for momentum signals.</summary>
public sealed class MomentumFuturesService
{
    private readonly IOptionsIntradayRepository _nfo;
    private readonly IMarketDataRepository _market;
    private readonly IAngelMarketDataClient _angel;
    private readonly NfoSyncService _nfoSync;
    private readonly ILogger<MomentumFuturesService> _logger;

    public MomentumFuturesService(
        IOptionsIntradayRepository nfo,
        IMarketDataRepository market,
        IAngelMarketDataClient angel,
        NfoSyncService nfoSync,
        ILogger<MomentumFuturesService> logger)
    {
        _nfo = nfo;
        _market = market;
        _angel = angel;
        _nfoSync = nfoSync;
        _logger = logger;
    }

    public async Task<MomentumFuturesSuggestionRow> GetSuggestionAsync(
        Guid instrumentId,
        string side,
        decimal entryPrice,
        decimal initialStopLoss,
        decimal? targetT1,
        decimal? targetT2,
        decimal? targetT3,
        CancellationToken ct = default)
    {
        var result = new MomentumFuturesSuggestionRow
        {
            InstrumentId = instrumentId,
            Side = side,
            UnderlyingEntry = entryPrice,
            UnderlyingStopLoss = initialStopLoss,
            UnderlyingTargetT1 = targetT1,
            UnderlyingTargetT2 = targetT2,
            UnderlyingTargetT3 = targetT3,
        };

        var fut = await ResolveNearestFutureAsync(instrumentId, ct);
        if (fut is null)
        {
            result.SkipReason = "No FUTSTK on Angel for this symbol (not in F&O, or name mismatch).";
            return result;
        }

        result.TradingSymbol = fut.TradingSymbol;
        result.ExpiryLabel = fut.ExpiryLabel;
        result.LotSize = fut.LotSize;
        result.SymbolToken = fut.SymbolToken;

        var spot = await ResolveSpotAsync(instrumentId, entryPrice, ct);
        result.SpotLtp = spot;

        var quote = await QuoteNfoAsync(fut.SymbolToken, ct);
        var futLtp = quote.Ltp ?? fut.LastLtp;
        if (futLtp is not decimal fl || fl <= 0)
        {
            result.SkipReason = "Futures quote unavailable — retry during market hours.";
            return result;
        }

        result.FuturesEntry = Math.Round(fl, 2);
        if (spot > 0)
            result.PremiumPct = Math.Round((fl - spot) / spot * 100m, 4);

        await _nfo.UpdateNfoQuoteAsync(fut.SymbolToken, quote.Ltp, quote.Oi, ct);

        var buildUp = ClassifyBuildUp(side, quote.Oi, fut.LastOi, result.PremiumPct);
        result.BuildUp = buildUp;
        result.FuturesConflict = IsConflict(side, buildUp);

        // Map underlying levels → futures via constant basis (fut − spot).
        var basis = fl - (spot > 0 ? spot : entryPrice);
        result.FuturesExit = MapLevel(initialStopLoss, basis);
        result.FuturesTargetT1 = MapLevel(targetT1, basis);
        result.FuturesTargetT2 = MapLevel(targetT2, basis);
        result.FuturesTargetT3 = MapLevel(targetT3, basis);

        FillLotEconomics(result, side, fl, fut.LotSize);
        return result;
    }

    /// <summary>Typical NSE stock-futures SPAN + exposure as a fraction of notional.</summary>
    public const decimal StockFuturesMarginPct = 0.18m;

    private static void FillLotEconomics(
        MomentumFuturesSuggestionRow result, string side, decimal entry, int lotSize)
    {
        if (lotSize <= 0) lotSize = 1;
        result.ContractValue = Math.Round(entry * lotSize, 2);
        result.MarginRequired = Math.Round(result.ContractValue.Value * StockFuturesMarginPct, 0);

        result.ExpectedProfitT1 = LotPnl(side, entry, result.FuturesTargetT1, lotSize);
        result.ExpectedProfitT2 = LotPnl(side, entry, result.FuturesTargetT2, lotSize);
        result.ExpectedProfitT3 = LotPnl(side, entry, result.FuturesTargetT3, lotSize);
        var slPnl = LotPnl(side, entry, result.FuturesExit, lotSize);
        result.ExpectedStopLoss = slPnl is decimal loss ? Math.Round(Math.Abs(loss), 0) : null;
    }

    /// <summary>Signed P&amp;L for 1 lot. Positive = profit.</summary>
    private static decimal? LotPnl(string side, decimal entry, decimal? level, int lotSize)
    {
        if (level is not decimal px || px <= 0 || entry <= 0)
            return null;
        var points = side == SignalSides.Sell ? entry - px : px - entry;
        return Math.Round(points * lotSize, 0);
    }

    private async Task<NfoContractRow?> ResolveNearestFutureAsync(Guid instrumentId, CancellationToken ct)
    {
        var fut = await NearestFutureAsync(instrumentId, ct);
        if (fut is not null)
            return fut;

        try
        {
            _logger.LogInformation("No FUTSTK in DB for {InstrumentId} — syncing NFO from Angel…", instrumentId);
            await _nfoSync.SyncUnderlyingNfoAsync(instrumentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "NFO sync failed for {InstrumentId}", instrumentId);
            return null;
        }

        return await NearestFutureAsync(instrumentId, ct);
    }

    private async Task<NfoContractRow?> NearestFutureAsync(Guid instrumentId, CancellationToken ct)
    {
        var nfo = await _nfo.GetNfoForUnderlyingAsync(instrumentId, ct);
        return nfo.Where(c => c.Kind == "future").OrderBy(c => c.Expiry).FirstOrDefault();
    }

    private async Task<decimal> ResolveSpotAsync(Guid instrumentId, decimal fallback, CancellationToken ct)
    {
        try
        {
            var ltpRows = await _market.GetUniverseLtpAsync(ct);
            var row = ltpRows.FirstOrDefault(r => r.InstrumentId == instrumentId);
            if (row?.Ltp is decimal l && l > 0)
                return l;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Spot LTP lookup failed for {InstrumentId}", instrumentId);
        }

        return fallback > 0 ? fallback : 0m;
    }

    private static decimal? MapLevel(decimal? spotLevel, decimal basis)
    {
        if (spotLevel is not decimal level || level <= 0)
            return null;
        return Math.Round(level + basis, 2);
    }

    private sealed record NfoQuoteSnapshot(decimal? Ltp, long? Oi);

    private async Task<NfoQuoteSnapshot> QuoteNfoAsync(string token, CancellationToken ct)
    {
        try
        {
            await _angel.EnsureSessionAsync(ct);
            var quotes = await _angel.GetQuotesAsync(
                QuoteModes.Full,
                new Dictionary<string, IReadOnlyList<string>> { ["NFO"] = new[] { token } },
                ct);
            var q = quotes.FirstOrDefault();
            return new NfoQuoteSnapshot(q?.Ltp, q?.OpenInterest);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "NFO quote failed for token {Token}", token);
            return new NfoQuoteSnapshot(null, null);
        }
    }

    private static string ClassifyBuildUp(string side, long? newOi, long? oldOi, decimal? premiumPct)
    {
        if (newOi is null || oldOi is null || oldOi == 0)
            return premiumPct is > 0 ? "premium_positive" : premiumPct is < 0 ? "premium_negative" : "oi_unknown";

        var oiUp = newOi > oldOi;
        if (side == SignalSides.Buy && oiUp) return "long_buildup";
        if (side == SignalSides.Sell && oiUp) return "short_buildup";
        if (side == SignalSides.Buy && !oiUp) return "long_unwinding";
        if (side == SignalSides.Sell && !oiUp) return "short_covering";
        return "oi_flat";
    }

    private static bool IsConflict(string side, string? buildUp) =>
        (side == SignalSides.Buy && buildUp is "short_buildup")
        || (side == SignalSides.Sell && buildUp is "long_buildup");
}
