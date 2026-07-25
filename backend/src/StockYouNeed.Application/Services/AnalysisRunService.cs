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
                // Skip heavy historical sync when we already have enough bars (avoids Angel rate limits).
                var sampleId = instrumentIds.FirstOrDefault();
                var existingSample = sampleId != Guid.Empty
                    ? await _market.GetBarsForInstrumentAsync(sampleId, 5, ct)
                    : Array.Empty<MarketBarRow>();
                var shouldSyncBars = existingSample.Count < 5;

                if (shouldSyncBars)
                {
                    try
                    {
                        _logger.LogInformation("Syncing last 10 trading days of bars before analysis…");
                        await _barsSync.SyncLastNTradingDaysAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Bar sync failed — continuing with existing market_bars if any.");
                    }
                }
                else
                {
                    _logger.LogInformation("Skipping historical bar sync — sample instrument already has {Count} bars.", existingSample.Count);
                }

                try
                {
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

                            // Normalize OHLC so LTP-as-close never violates high/low check constraints.
                            var open = q.Open.Value;
                            var high = q.High.Value;
                            var low = q.Low.Value;
                            var close = q.Ltp.Value;
                            high = Math.Max(high, Math.Max(open, close));
                            low = Math.Min(low, Math.Min(open, close));

                            await _market.UpsertOhlcAsync(
                                token.InstrumentId,
                                token.Exchange,
                                string.IsNullOrWhiteSpace(q.TradingSymbol) ? token.TradingSymbol : q.TradingSymbol,
                                token.SymbolToken,
                                q.Ltp.Value,
                                open,
                                high,
                                low,
                                close,
                                q.TradeVolume ?? 0,
                                runId,
                                q.RawJson,
                                ct);

                            await _market.UpsertMarketBarAsync(
                                token.InstrumentId,
                                asOf,
                                open,
                                high,
                                low,
                                close,
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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Live quote refresh failed — screening from market_bars only.");
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
            var skippedFewBars = 0;
            var noSetup = 0;
            var sectorRejected = 0;
            foreach (var instrumentId in instrumentIds)
            {
                var bars = (await _market.GetBarsForInstrumentAsync(instrumentId, 10, ct))
                    .OrderByDescending(b => b.TradeDate)
                    .ToList();
                if (bars.Count < 5)
                {
                    skippedFewBars++;
                    continue;
                }

                livePrices.TryGetValue(instrumentId, out var livePrice);
                var signal = Evaluate(userId, runId, asOf, bars, livePrice > 0 ? livePrice : null);
                if (signal is null)
                {
                    noSetup++;
                    continue;
                }

                // Sector confirmation: if enabled, check sector also breaks 2-day range
                if (includeSectorCheck)
                {
                    var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
                    if (sectorId is not null && sectorBarsCache.TryGetValue(sectorId.Value, out var sectorBars))
                    {
                        var sectorConfirmed = CheckSectorConfirmation(signal.Side, sectorBars);
                        signal.SectorConfirmed = sectorConfirmed;
                        if (!sectorConfirmed)
                        {
                            sectorRejected++;
                            continue;
                        }
                    }
                }

                await _portfolio.InsertSignalAsync(signal, ct);
                signalCount++;
            }

            _logger.LogInformation(
                "Analysis {RunId}: scanned={Scanned}, signals={Signals}, fewBars={FewBars}, noSetup={NoSetup}, sectorRejected={SectorRejected}, liveQuotes={LiveQuotes}",
                runId, instrumentIds.Count, signalCount, skippedFewBars, noSetup, sectorRejected, livePrices.Count);

            await _portfolio.CompleteAnalysisRunAsync(
                runId,
                "succeeded",
                null,
                new
                {
                    scanned = instrumentIds.Count,
                    signals = signalCount,
                    fewBars = skippedFewBars,
                    noSetup,
                    sectorRejected,
                    liveQuotes = livePrices.Count
                },
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
            ? latest.High > last2High
            : latest.Low < last2Low;
    }

    /// <summary>
    /// Breakout: today's high/low vs prior 2 sessions.
    /// Volume: today's volume >= average of prior 3 sessions (momentum, not too low).
    /// Entry: LTP when available.
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

        // Prior 3 completed sessions only (exclude today).
        // Intraday volume is incomplete vs full prior days — "not too low" ≈ 25% of that average.
        var prior3 = barsDesc.Skip(1).Take(3).ToList();
        if (prior3.Count == 0)
            return null;
        var avgVolPrior3 = prior3.Average(b => (double)b.Volume);
        var volumeOk = latest.Volume >= (long)(avgVolPrior3 * 0.25);

        // Breakout vs last 2 sessions. LTP only used to resolve buy+sell same day.
        var ltp = livePrice ?? latest.Close;

        string? side = null;
        var buyBreak = latest.High > last2High;
        var sellBreak = latest.Low < last2Low;
        if (buyBreak && sellBreak && volumeOk)
        {
            var mid = (last2High + last2Low) / 2m;
            side = ltp >= mid ? SignalSides.Buy : SignalSides.Sell;
        }
        else if (buyBreak && volumeOk)
            side = SignalSides.Buy;
        else if (sellBreak && volumeOk)
            side = SignalSides.Sell;

        if (side is null)
            return null;

        // Entry = breakout level: last-2-day high (buy) / last-2-day low (sell).
        var entry = side == SignalSides.Buy ? last2High : last2Low;

        var closes = barsDesc.Take(5).Select(b => b.Close).Reverse().ToList();
        decimal Ma(int n) => closes.TakeLast(n).Average();

        var ma2 = closes.Count >= 2 ? Ma(2) : (decimal?)null;
        var ma3 = closes.Count >= 3 ? Ma(3) : (decimal?)null;
        var ma5 = closes.Count >= 5 ? Ma(5) : (decimal?)null;

        // Targets from average % up/down excursion vs prior close (not MAs).
        // Buy:  T1=5d avg up%, T2=3d, T3=2d  → entry * (1 + pct)
        // Sell: T1=5d avg down%, T2=3d, T3=2d → entry * (1 - pct)
        var avgUp5 = AvgDirectionalMovePct(barsDesc, 5, up: true);
        var avgUp3 = AvgDirectionalMovePct(barsDesc, 3, up: true);
        var avgUp2 = AvgDirectionalMovePct(barsDesc, 2, up: true);
        var avgDn5 = AvgDirectionalMovePct(barsDesc, 5, up: false);
        var avgDn3 = AvgDirectionalMovePct(barsDesc, 3, up: false);
        var avgDn2 = AvgDirectionalMovePct(barsDesc, 2, up: false);

        decimal? t1;
        decimal? t2;
        decimal? t3;
        decimal sl;

        if (side == SignalSides.Buy)
        {
            sl = last2Low;
            if (sl >= entry)
                sl = entry * 0.98m;
            var buyTargets = new[]
                {
                    avgUp5 > 0 ? RoundPrice(entry * (1 + avgUp5)) : (decimal?)null,
                    avgUp3 > 0 ? RoundPrice(entry * (1 + avgUp3)) : null,
                    avgUp2 > 0 ? RoundPrice(entry * (1 + avgUp2)) : null
                }
                .Where(t => t is decimal v && v > entry)
                .Select(t => t!.Value)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            t1 = buyTargets.Count > 0 ? buyTargets[0] : null;
            t2 = buyTargets.Count > 1 ? buyTargets[1] : null;
            t3 = buyTargets.Count > 2 ? buyTargets[2] : null;
        }
        else
        {
            sl = last2High;
            if (sl <= entry)
                sl = entry * 1.02m;
            // Sell: descending so T1 is nearest (highest), T3 furthest (lowest).
            var sellTargets = new[]
                {
                    avgDn5 > 0 ? RoundPrice(entry * (1 - avgDn5)) : (decimal?)null,
                    avgDn3 > 0 ? RoundPrice(entry * (1 - avgDn3)) : null,
                    avgDn2 > 0 ? RoundPrice(entry * (1 - avgDn2)) : null
                }
                .Where(t => t is decimal v && v < entry)
                .Select(t => t!.Value)
                .Distinct()
                .OrderByDescending(t => t)
                .ToList();
            t1 = sellTargets.Count > 0 ? sellTargets[0] : null;
            t2 = sellTargets.Count > 1 ? sellTargets[1] : null;
            t3 = sellTargets.Count > 2 ? sellTargets[2] : null;
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
            SectorConfirmed = true,
            Ma2d = ma2,
            Ma3d = ma3,
            Ma5d = ma5,
            Last2dHigh = last2High,
            Last2dLow = last2Low
        };
    }

    /// <summary>
    /// Average daily % move over the last <paramref name="days"/> sessions.
    /// Up: max(0, (high - prevClose) / prevClose). Down: max(0, (prevClose - low) / prevClose).
    /// </summary>
    private static decimal AvgDirectionalMovePct(List<MarketBarRow> barsNewestFirst, int days, bool up)
    {
        if (days < 1 || barsNewestFirst.Count < days + 1)
            return 0m;

        decimal sum = 0m;
        var count = 0;
        for (var i = 0; i < days; i++)
        {
            var day = barsNewestFirst[i];
            var prevClose = barsNewestFirst[i + 1].Close;
            if (prevClose <= 0)
                continue;

            var pct = up
                ? (day.High - prevClose) / prevClose
                : (prevClose - day.Low) / prevClose;
            if (pct < 0)
                pct = 0;
            sum += pct;
            count++;
        }

        return count == 0 ? 0m : sum / count;
    }

    private static decimal RoundPrice(decimal price) =>
        Math.Round(price, 2, MidpointRounding.AwayFromZero);
}
