using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.Services;

public sealed class AnalysisRunService
{
    private readonly IAngelMarketDataClient _angel;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketDataRepository _market;
    private readonly IPortfolioRepository _portfolio;
    private readonly MarketBarsSyncService _barsSync;
    private readonly TokenSyncService _tokenSync;
    private readonly UniverseSeedService _universeSeed;
    private readonly SignalOutcomeService _outcomes;
    private readonly AngelOptions _options;
    private readonly ILogger<AnalysisRunService> _logger;

    public AnalysisRunService(
        IAngelMarketDataClient angel,
        IInstrumentRepository instruments,
        IMarketDataRepository market,
        IPortfolioRepository portfolio,
        MarketBarsSyncService barsSync,
        TokenSyncService tokenSync,
        UniverseSeedService universeSeed,
        SignalOutcomeService outcomes,
        IOptions<AngelOptions> options,
        ILogger<AnalysisRunService> logger)
    {
        _angel = angel;
        _instruments = instruments;
        _market = market;
        _portfolio = portfolio;
        _barsSync = barsSync;
        _tokenSync = tokenSync;
        _universeSeed = universeSeed;
        _outcomes = outcomes;
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

            // Sector confirmation evidence (always computed; UI toggle filters client-side).
            var sectorBarsCache = new Dictionary<Guid, List<MarketBarRow>>();
            try
            {
                await _universeSeed.SeedAsync(ct);
                if (_options.Enabled)
                {
                    var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
                    if (sectorTokens.Count == 0)
                        await _tokenSync.SyncUniverseTokensAsync(ct);
                    await _barsSync.SyncMissingSectorBarsAsync(ct);
                    // Keep sector "latest" = today (same live OHLC idea as equities above).
                    await RefreshSectorDailyBarsFromQuotesAsync(asOf, sectorTokens, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Sector prep failed — sectorConfirmed may be false for many rows.");
            }

            var sectorIds = await _instruments.GetSectorInstrumentIdsAsync(ct);
            foreach (var sectorId in sectorIds)
            {
                var sBars = (await _market.GetBarsForInstrumentAsync(sectorId, 10, ct))
                    .OrderByDescending(b => b.TradeDate)
                    .ToList();
                if (sBars.Count >= 3)
                    sectorBarsCache[sectorId] = sBars;
            }

            _logger.LogInformation(
                "Sector evidence: {SectorIds} sectors, {WithBars} with bars (includeSectorCheck={IncludeSectorCheck} ignored for filtering).",
                sectorIds.Count, sectorBarsCache.Count, includeSectorCheck);

            var signalCount = 0;
            var skippedFewBars = 0;
            var noSetup = 0;
            var skippedFlip = 0;
            var sectorConfirmedCount = 0;
            var openOutcomes = await _outcomes.GetOpenAsync(userId, ct);

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
                var signal = BreakoutSignalEvaluator.Evaluate(
                    userId, runId, asOf, bars, livePrice > 0 ? livePrice : null,
                    actionableOnly: true,
                    projectPartialSessionVolume: true);
                if (signal is null)
                {
                    noSetup++;
                    continue;
                }

                if (OppositeSignalFlipGuard.IsFlipAgainstOpen(
                        instrumentId, signal.Side, asOf, openOutcomes, out var flipReason))
                {
                    skippedFlip++;
                    _logger.LogInformation(
                        "Signals skip {Symbol}: {Reason}", signal.AppSymbol, flipReason);
                    continue;
                }

                var sectorId = await _instruments.GetSectorIdForInstrumentAsync(instrumentId, ct);
                if (sectorId is not null && sectorBarsCache.TryGetValue(sectorId.Value, out var sectorBars))
                {
                    signal.SectorConfirmed = CheckSectorConfirmation(signal.Side, sectorBars);
                }
                else
                {
                    signal.SectorConfirmed = false;
                }

                if (signal.SectorConfirmed)
                    sectorConfirmedCount++;

                await _portfolio.InsertSignalAsync(signal, ct);
                await _outcomes.OpenFromSignalAsync(signal, ct);
                signalCount++;
            }

            _logger.LogInformation(
                "Analysis {RunId}: scanned={Scanned}, signals={Signals}, sectorConfirmed={SectorConfirmed}, fewBars={FewBars}, noSetup={NoSetup}, skippedFlip={SkippedFlip}, liveQuotes={LiveQuotes}",
                runId, instrumentIds.Count, signalCount, sectorConfirmedCount, skippedFewBars, noSetup, skippedFlip, livePrices.Count);

            await _portfolio.CompleteAnalysisRunAsync(
                runId,
                "succeeded",
                null,
                new
                {
                    scanned = instrumentIds.Count,
                    signals = signalCount,
                    sectorConfirmed = sectorConfirmedCount,
                    fewBars = skippedFewBars,
                    noSetup,
                    skippedFlip,
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

    private async Task RefreshSectorDailyBarsFromQuotesAsync(
        DateOnly asOf, IReadOnlyList<AngelTokenRow> sectorTokens, CancellationToken ct)
    {
        if (sectorTokens.Count == 0)
            return;

        await _angel.EnsureSessionAsync(ct);
        var exchangeTokens = sectorTokens
            .GroupBy(t => t.Exchange)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.SymbolToken).Distinct().ToList());

        var quotes = await _angel.GetQuotesAsync(QuoteModes.Full, exchangeTokens, ct);
        var byToken = quotes.ToDictionary(q => (q.Exchange, q.SymbolToken), q => q);
        var updated = 0;

        foreach (var token in sectorTokens)
        {
            if (!byToken.TryGetValue((token.Exchange, token.SymbolToken), out var q))
                continue;
            if (q.Ltp is null || q.Open is null || q.High is null || q.Low is null || q.Close is null)
                continue;

            var open = q.Open.Value;
            var high = q.High.Value;
            var low = q.Low.Value;
            var close = q.Ltp.Value;
            high = Math.Max(high, Math.Max(open, close));
            low = Math.Min(low, Math.Min(open, close));

            await _market.UpsertMarketBarAsync(
                token.InstrumentId, asOf, open, high, low, close, q.TradeVolume ?? 0, ct);
            updated++;
        }

        _logger.LogInformation("Refreshed today's OHLC for {Count} sector indexes.", updated);
    }
}
