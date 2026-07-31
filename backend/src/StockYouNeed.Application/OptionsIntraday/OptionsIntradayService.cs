using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.OptionsIntraday;

/// <summary>
/// Options Intraday recommendations: underlying Liquidity Fresh / Confluence decides
/// side + SL/T1; Angel optionGreek picks ATM/1ITM contract; premium is display-only.
/// </summary>
public sealed class OptionsIntradayService
{
    private readonly IPortfolioRepository _portfolio;
    private readonly IOptionsIntradayRepository _repo;
    private readonly IMarketDataRepository _market;
    private readonly IAngelMarketDataClient _angel;
    private readonly NfoSyncService _nfoSync;
    private readonly SignalOutcomeService _outcomes;
    private readonly ILogger<OptionsIntradayService> _logger;

    public OptionsIntradayService(
        IPortfolioRepository portfolio,
        IOptionsIntradayRepository repo,
        IMarketDataRepository market,
        IAngelMarketDataClient angel,
        NfoSyncService nfoSync,
        SignalOutcomeService outcomes,
        ILogger<OptionsIntradayService> logger)
    {
        _portfolio = portfolio;
        _repo = repo;
        _market = market;
        _angel = angel;
        _nfoSync = nfoSync;
        _outcomes = outcomes;
        _logger = logger;
    }

    public Task<IReadOnlyList<OptionsIntradayRecommendationRow>> GetRecommendationsAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
        => _repo.GetRecommendationsAsync(userId, runId, ct);

    public async Task<OptionsIntradayRunRow> RunAsync(Guid userId, CancellationToken ct = default)
    {
        var asOf = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var runId = await _repo.CreateRunAsync(userId, asOf, ct);

        try
        {
            await _nfoSync.SyncUniverseNfoAsync(ct);

            var liquidity = await _portfolio.GetLiquiditySignalsAsync(userId, null, "fresh", ct);
            var signals = await _portfolio.GetSignalsAsync(userId, null, ct);
            var ltpMap = (await _market.GetAllLtpAsync(ct))
                .ToDictionary(x => x.InstrumentId, x => x.Ltp);

            var written = 0;
            var seen = new HashSet<(Guid Inst, string Side)>();

            foreach (var liq in liquidity)
            {
                ct.ThrowIfCancellationRequested();
                var key = (liq.InstrumentId, liq.Side.ToLowerInvariant());
                if (!seen.Add(key)) continue;

                var sig = signals.FirstOrDefault(s =>
                    s.InstrumentId == liq.InstrumentId
                    && string.Equals(s.Side, liq.Side, StringComparison.OrdinalIgnoreCase));

                var source = "liquidity_fresh";
                var entry = liq.EntryPrice;
                var sl = liq.InitialStopLoss;
                var t1 = liq.TargetT1;
                var t2 = liq.TargetT2;
                var t3 = liq.TargetT3;
                Guid? analysisId = null;

                if (sig is not null
                    && Confluence.ConfluenceLevelComposer.DatesAlign(sig.AsOfDate, liq.AsOfDate)
                    && Confluence.ConfluenceLevelComposer.PricesAlign(liq.EntryPrice, sig.EntryPrice, liq.EntryPrice)
                    && Confluence.ConfluenceLevelComposer.TryCompose(
                        liq.Side, sig.EntryPrice, sig.InitialStopLoss,
                        liq.EntryPrice, liq.InitialStopLoss,
                        out var cEntry, out var cSl))
                {
                    source = "confluence";
                    entry = cEntry;
                    sl = cSl;
                    analysisId = sig.Id;
                }

                ltpMap.TryGetValue(liq.InstrumentId, out var spot);
                if (spot <= 0) spot = entry;

                var nfo = await _repo.GetNfoForUnderlyingAsync(liq.InstrumentId, ct);
                var options = nfo.Where(c => c.Kind == "option").ToList();
                var futures = nfo.Where(c => c.Kind == "future").OrderBy(c => c.Expiry).ToList();

                var reasons = new List<string> { source == "confluence" ? "Confluence aligned" : "Liquidity Fresh" };
                var confidence = source == "confluence" ? 70 : 55;

                string? buildUp = null;
                decimal? premPct = null;
                if (futures.Count > 0)
                {
                    var fut = futures[0];
                    var (fLtp, fOi) = await QuoteNfoAsync(fut.SymbolToken, ct);
                    if (fLtp is decimal fl && spot > 0)
                    {
                        premPct = Math.Round((fl - spot) / spot * 100m, 4);
                        reasons.Add($"Futures premium {premPct:+0.00;-0.00}%");
                    }

                    buildUp = ClassifyBuildUp(liq.Side, fOi, fut.LastOi, premPct);
                    if (fut.LastOi is not null && fOi is not null)
                        reasons.Add(buildUp ?? "Futures OI checked");

                    await _repo.UpdateNfoQuoteAsync(fut.SymbolToken, fLtp, fOi, ct);

                    if (IsConflict(liq.Side, buildUp))
                    {
                        await PersistSkipped(runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                            buildUp, premPct, confidence, reasons,
                            $"Futures conflict ({buildUp})", analysisId, ct);
                        written++;
                        continue;
                    }

                    if (buildUp is "long_buildup" or "short_buildup")
                        confidence += 15;
                }
                else
                {
                    buildUp = "no_future";
                    reasons.Add("No near future mapped");
                }

                if (options.Count == 0)
                {
                    await PersistSkipped(runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons, "No NFO options mapped", analysisId, ct);
                    written++;
                    continue;
                }

                var nearestExpiry = options.Min(o => o.Expiry);
                var expiryContracts = options.Where(o => o.Expiry == nearestExpiry).ToList();
                var expiryLabel = expiryContracts[0].ExpiryLabel;
                var angelName = expiryContracts[0].AngelName;

                var greeks = await _angel.GetOptionGreeksAsync(angelName, expiryLabel, ct);
                if (greeks.Count == 0)
                {
                    await PersistSkipped(runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons,
                        $"optionGreek unavailable ({angelName} {expiryLabel})", analysisId, ct);
                    written++;
                    continue;
                }

                var (primary, alt) = OptionStrikeSelector.Select(
                    liq.Side, spot, greeks, expiryContracts, expiryLabel);
                if (primary is null)
                {
                    await PersistSkipped(runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons, "No ATM/1ITM candidate", analysisId, ct);
                    written++;
                    continue;
                }

                decimal? prem = null;
                string? tradingSym = primary.Contract?.TradingSymbol;
                string? token = primary.Contract?.SymbolToken;
                int? lot = primary.Contract?.LotSize;
                if (token is not null)
                {
                    var (pLtp, _) = await QuoteNfoAsync(token, ct);
                    prem = pLtp;
                    if (pLtp is not null)
                        await _repo.UpdateNfoQuoteAsync(token, pLtp, null, ct);
                }
                else
                {
                    // Contract map miss — still recommend strike from Greeks.
                    tradingSym = $"{liq.AppSymbol} {primary.Strike:0.##} {primary.OptionType}";
                }

                decimal? altPrem = null;
                if (alt?.Contract?.SymbolToken is string altTok)
                {
                    var (aLtp, _) = await QuoteNfoAsync(altTok, ct);
                    altPrem = aLtp;
                }

                if (primary.Delta is not null) reasons.Add($"Δ {Math.Abs(primary.Delta.Value):0.00} (long)");
                if (primary.Iv is not null) reasons.Add($"IV {primary.Iv:0.0}%");
                confidence = Math.Clamp(confidence + 10, 0, 99);

                var row = new OptionsIntradayRecommendationRow
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    UserId = userId,
                    InstrumentId = liq.InstrumentId,
                    AppSymbol = liq.AppSymbol,
                    InstrumentName = liq.InstrumentName,
                    Side = liq.Side,
                    SignalSource = source,
                    Status = "recommended",
                    SpotLtp = spot,
                    UnderlyingEntry = entry,
                    UnderlyingStopLoss = sl,
                    UnderlyingTargetT1 = t1,
                    UnderlyingTargetT2 = t2,
                    UnderlyingTargetT3 = t3,
                    FuturesBuildUp = buildUp,
                    FuturesPremiumPct = premPct,
                    ConfidenceScore = confidence,
                    Reasons = reasons.ToArray(),
                    ContractTradingSymbol = tradingSym,
                    ContractExpiryLabel = expiryLabel,
                    ContractStrike = primary.Strike,
                    ContractOptionType = primary.OptionType,
                    ContractToken = token,
                    ContractLotSize = lot,
                    PremiumLtp = prem,
                    Delta = OptionStrikeSelector.ToLongOptionDelta(primary.Delta),
                    Gamma = primary.Gamma,
                    Theta = primary.Theta,
                    Vega = primary.Vega,
                    ImpliedVolatility = primary.Iv,
                    TradeVolume = primary.Volume,
                    AltTradingSymbol = alt?.Contract?.TradingSymbol
                        ?? (alt is null ? null : $"{liq.AppSymbol} {alt.Strike:0.##} {alt.OptionType}"),
                    AltStrike = alt?.Strike,
                    AltDelta = OptionStrikeSelector.ToLongOptionDelta(alt?.Delta),
                    AltImpliedVolatility = alt?.Iv,
                    AltPremiumLtp = altPrem,
                    FlatByIst = "15:20",
                    LiquiditySignalId = liq.Id,
                    AnalysisSignalId = analysisId,
                };

                await _repo.InsertRecommendationAsync(row, ct);
                await _outcomes.OpenAsync(new SignalOutcomeRow
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstrumentId = liq.InstrumentId,
                    Strategy = "options_intraday",
                    Side = liq.Side,
                    SignalDate = asOf,
                    EntryPrice = entry,
                    InitialStopLoss = sl,
                    TargetT1 = t1,
                    TargetT2 = t2,
                    TargetT3 = t3,
                    LiquiditySignalId = liq.Id,
                    AnalysisSignalId = analysisId,
                }, ct);
                written++;
            }

            await _repo.CompleteRunAsync(runId, userId, "succeeded", null, ct);
            _logger.LogInformation("Options Intraday run {RunId}: {Count} rows", runId, written);

            return new OptionsIntradayRunRow
            {
                Id = runId,
                UserId = userId,
                AsOfDate = asOf,
                Status = "succeeded",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Options Intraday run {RunId} failed", runId);
            await _repo.CompleteRunAsync(runId, userId, "failed", ex.Message, ct);
            throw new InvalidOperationException($"Options Intraday failed: {ex.Message}", ex);
        }
    }

    private async Task PersistSkipped(
        Guid runId, Guid userId, LiquiditySignalRow liq, string source,
        decimal entry, decimal sl, decimal? t1, decimal? t2, decimal? t3, decimal spot,
        string? buildUp, decimal? premPct, int confidence, List<string> reasons,
        string skipReason, Guid? analysisId, CancellationToken ct)
    {
        await _repo.InsertRecommendationAsync(new OptionsIntradayRecommendationRow
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            UserId = userId,
            InstrumentId = liq.InstrumentId,
            AppSymbol = liq.AppSymbol,
            InstrumentName = liq.InstrumentName,
            Side = liq.Side,
            SignalSource = source,
            Status = "skipped",
            SkipReason = skipReason,
            SpotLtp = spot,
            UnderlyingEntry = entry,
            UnderlyingStopLoss = sl,
            UnderlyingTargetT1 = t1,
            UnderlyingTargetT2 = t2,
            UnderlyingTargetT3 = t3,
            FuturesBuildUp = buildUp,
            FuturesPremiumPct = premPct,
            ConfidenceScore = confidence,
            Reasons = reasons.Append(skipReason).ToArray(),
            FlatByIst = "15:20",
            LiquiditySignalId = liq.Id,
            AnalysisSignalId = analysisId,
        }, ct);
    }

    private async Task<(decimal? Ltp, long? Oi)> QuoteNfoAsync(string token, CancellationToken ct)
    {
        try
        {
            var quotes = await _angel.GetQuotesAsync(
                QuoteModes.Full,
                new Dictionary<string, IReadOnlyList<string>> { ["NFO"] = new[] { token } },
                ct);
            var q = quotes.FirstOrDefault();
            return (q?.Ltp, q?.OpenInterest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NFO quote failed for token {Token}", token);
            return (null, null);
        }
    }

    private static string ClassifyBuildUp(string side, long? newOi, long? oldOi, decimal? premiumPct)
    {
        if (newOi is null || oldOi is null || oldOi == 0)
            return premiumPct is > 0 ? "premium_positive" : premiumPct is < 0 ? "premium_negative" : "oi_unknown";

        var oiUp = newOi > oldOi;
        // Without future price direction series, use signal side + OI as soft label.
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
