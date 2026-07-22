using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class AnalysisRunService
{
    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly IPortfolioRepository _portfolio;
    private readonly MarketBarsSyncService _barsSync;
    private readonly AngelOptions _options;
    private readonly ILogger<AnalysisRunService> _logger;

    public AnalysisRunService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IPortfolioRepository portfolio,
        MarketBarsSyncService barsSync,
        IOptions<AngelOptions> options,
        ILogger<AnalysisRunService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _portfolio = portfolio;
        _barsSync = barsSync;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AnalysisRunRow> RunAsync(
        Guid userId,
        bool includeNifty50,
        bool includeNifty100,
        bool includeWatchlist,
        string triggeredBy,
        bool includeSectorCheck = false,
        CancellationToken ct = default)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)); // IST approx
        var runId = await _portfolio.CreateAnalysisRunAsync(
            userId, triggeredBy, includeNifty50, includeNifty100, includeWatchlist, asOf, ct);

        try
        {
            var tokens = await _instruments.GetActiveTokensForUniversesAsync(ct);
            var watchlistIds = includeWatchlist
                ? await _portfolio.GetWatchlistInstrumentIdsAsync(userId, ct)
                : Array.Empty<Guid>();

            // Filter by requested universes is already embedded in token query (both nifty_50/100).
            // Optionally narrow to watchlist-only extras: tokens already cover index; watchlist still scanned from bars.
            var instrumentIds = tokens.Select(t => t.InstrumentId).ToHashSet();
            foreach (var id in watchlistIds)
                instrumentIds.Add(id);

            var livePrices = new Dictionary<Guid, decimal>();
            if (_options.Enabled && tokens.Count > 0)
            {
                // Ensure historical daily bars exist before screening (Worker may not be running).
                _logger.LogInformation("Syncing last 10 trading days of bars before analysis…");
                await _barsSync.SyncLastNTradingDaysAsync(ct);

                await _angel.EnsureSessionAsync(ct);
                foreach (var chunk in tokens.Chunk(50))
                {
                    var started = DateTimeOffset.UtcNow;
                    var exchangeTokens = chunk
                        .GroupBy(t => t.Exchange)
                        .ToDictionary(
                            g => g.Key,
                            g => (IReadOnlyList<string>)g.Select(x => x.SymbolToken).Distinct().ToList());
                    var requestJson = JsonSerializer.Serialize(exchangeTokens);

                    // FULL → volume + OHLC; depth ignored
                    var quotes = await _angel.GetQuotesAsync(QuoteModes.Full, exchangeTokens, ct);
                    var byToken = quotes.ToDictionary(q => (q.Exchange, q.SymbolToken), q => q);

                    foreach (var token in chunk)
                    {
                        if (!byToken.TryGetValue((token.Exchange, token.SymbolToken), out var q))
                            continue;
                        if (q.Ltp is null || q.Open is null || q.High is null || q.Low is null || q.Close is null)
                            continue;

                        await _market.UpsertOhlcAsync(
                            token.InstrumentId,
                            token.Exchange,
                            string.IsNullOrWhiteSpace(q.TradingSymbol) ? token.TradingSymbol : q.TradingSymbol,
                            token.SymbolToken,
                            q.Ltp.Value,
                            q.Open.Value,
                            q.High.Value,
                            q.Low.Value,
                            q.Close.Value,
                            q.TradeVolume ?? 0,
                            runId,
                            q.RawJson,
                            ct);

                        // Keep market_bars in sync with today's live session for screening.
                        await _market.UpsertMarketBarAsync(
                            token.InstrumentId,
                            asOf,
                            q.Open.Value,
                            q.High.Value,
                            q.Low.Value,
                            q.Close.Value,
                            q.TradeVolume ?? 0,
                            ct);

                        livePrices[token.InstrumentId] = q.Ltp.Value;
                    }

                    await _market.LogQuoteFetchBatchAsync(
                        QuoteModes.Full,
                        chunk.Length,
                        quotes.Count,
                        Math.Max(0, chunk.Length - quotes.Count),
                        true,
                        "SUCCESS",
                        "",
                        requestJson,
                        "[]",
                        runId,
                        (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                        ct);

                    await Task.Delay(1100, ct);
                }
            }
            else if (_options.Enabled)
            {
                _logger.LogWarning("Angel enabled but no mapped tokens; cannot sync bars or quotes.");
            }
            else
            {
                _logger.LogWarning("Angel disabled — screening uses existing market_bars only.");
            }

            // Build sector bars cache for sector confirmation
            var sectorBarsCache = new Dictionary<Guid, List<MarketBarRow>>();
            if (includeSectorCheck)
            {
                var sectorIds = await _instruments.GetSectorInstrumentIdsAsync(ct);
                foreach (var sectorId in sectorIds)
                {
                    var sBars = (await _market.GetBarsForInstrumentAsync(sectorId, 10, ct))
                        .OrderByDescending(b => b.TradeDate)
                        .ToList();
                    if (sBars.Count >= 3)
                        sectorBarsCache[sectorId] = sBars;
                }
            }

            var signalCount = 0;
            foreach (var instrumentId in instrumentIds)
            {
                var bars = (await _market.GetBarsForInstrumentAsync(instrumentId, 10, ct))
                    .OrderByDescending(b => b.TradeDate)
                    .ToList();
                if (bars.Count < 5)
                {
                    _logger.LogDebug("Skipping {InstrumentId}: only {BarCount} bars", instrumentId, bars.Count);
                    continue;
                }

                livePrices.TryGetValue(instrumentId, out var livePrice);
                var signal = Evaluate(userId, runId, asOf, bars, livePrice > 0 ? livePrice : null);
                if (signal is null)
                    continue;

                // Sector confirmation: if enabled, check sector also breaks 2-day range
                if (includeSectorCheck)
                {
                    var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
                    if (sectorId is not null && sectorBarsCache.TryGetValue(sectorId.Value, out var sectorBars))
                    {
                        var sectorConfirmed = CheckSectorConfirmation(signal.Side, sectorBars);
                        signal.SectorConfirmed = sectorConfirmed;
                        if (!sectorConfirmed)
                            continue;
                    }
                }

                await _portfolio.InsertSignalAsync(signal, ct);
                signalCount++;
            }

            await _portfolio.CompleteAnalysisRunAsync(
                runId,
                "succeeded",
                null,
                new { scanned = instrumentIds.Count, signals = signalCount },
                ct);

            return new AnalysisRunRow
            {
                Id = runId,
                UserId = userId,
                TriggeredBy = triggeredBy,
                IncludeNifty50 = includeNifty50,
                IncludeNifty100 = includeNifty100,
                IncludeWatchlist = includeWatchlist,
                AsOfDate = asOf,
                StartedAt = DateTimeOffset.UtcNow,
                FinishedAt = DateTimeOffset.UtcNow,
                Status = "succeeded"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis run {RunId} failed", runId);
            await _portfolio.CompleteAnalysisRunAsync(runId, "failed", ex.Message, new { }, ct);
            throw;
        }
    }

    /// Sector confirmation: sector index must also break last 2 sessions' high/low (no volume required).
    private static bool CheckSectorConfirmation(string side, List<MarketBarRow> sectorBarsDesc)
    {
        if (sectorBarsDesc.Count < 3)
            return true; // not enough data — pass through

        var latest = sectorBarsDesc[0];
        var prev = sectorBarsDesc.Skip(1).Take(2).ToList();
        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);

        return side == SignalSides.Buy
            ? latest.Close > last2High
            : latest.Close < last2Low;
    }

    /// <summary>
    /// Lightweight v1 strategy: breakout vs last 2 sessions + volume vs 5d avg + MA targets.
    /// </summary>
    private static AnalysisSignalRow? Evaluate(
        Guid userId, Guid runId, DateOnly asOf, List<MarketBarRow> barsDesc, decimal? livePrice = null)
    {
        var latest = barsDesc[0];
        var prev = barsDesc.Skip(1).Take(2).ToList();
        if (prev.Count < 2)
            return null;

        var last2High = prev.Max(b => b.High);
        var last2Low = prev.Min(b => b.Low);
        var avgVol5 = barsDesc.Take(5).Average(b => (double)b.Volume);
        var volumeOk = latest.Volume >= (long)(avgVol5 * 1.0);

        // Strategy uses current price (LTP) when available; otherwise today's close.
        var price = livePrice ?? latest.Close;

        string? side = null;
        if (price > last2High && volumeOk)
            side = SignalSides.Buy;
        else if (price < last2Low && volumeOk)
            side = SignalSides.Sell;

        if (side is null)
            return null;

        var closes = barsDesc.Take(5).Select(b => b.Close).Reverse().ToList();
        decimal Ma(int n) => closes.TakeLast(n).Average();

        var ma2 = closes.Count >= 2 ? Ma(2) : (decimal?)null;
        var ma3 = closes.Count >= 3 ? Ma(3) : (decimal?)null;
        var ma5 = closes.Count >= 5 ? Ma(5) : (decimal?)null;

        var entry = price;
        decimal sl;
        decimal? t1 = null, t2 = null, t3 = null;

        if (side == SignalSides.Buy)
        {
            sl = last2Low;
            t1 = ma2 is decimal m2 && m2 > entry ? m2 : null;
            t2 = ma3 is decimal m3 && m3 >= (t1 ?? entry) ? m3 : null;
            t3 = ma5 is decimal m5 && m5 >= (t2 ?? t1 ?? entry) ? m5 : null;
            if (sl >= entry)
                sl = entry * 0.98m;
        }
        else
        {
            sl = last2High;
            t1 = ma2 is decimal s2 && s2 < entry ? s2 : null;
            t2 = ma3 is decimal s3 && s3 <= (t1 ?? entry) ? s3 : null;
            t3 = ma5 is decimal s5 && s5 <= (t2 ?? t1 ?? entry) ? s5 : null;
            if (sl <= entry)
                sl = entry * 1.02m;
        }

        return new AnalysisSignalRow
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = runId,
            UserId = userId,
            InstrumentId = latest.InstrumentId,
            AppSymbol = latest.AppSymbol,
            Side = side,
            AsOfDate = asOf,
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = t1,
            TargetT2 = t2,
            TargetT3 = t3,
            VolumeOk = volumeOk,
            SectorConfirmed = true, // sector rule wired later
            Ma2d = ma2,
            Ma3d = ma3,
            Ma5d = ma5,
            Last2dHigh = last2High,
            Last2dLow = last2Low
        };
    }
}
