using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Confluence;
using StockYouNeed.Application.OptionsIntraday;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Services;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.IndexOptions;

/// <summary>
/// Nifty index options: ORB, ORB+Liquidity V2, Liquidity V2+Breakout, Breakout+Volume,
/// Breakout+Chain, and Hero Zero.
/// Each directional section uses the same premium ticket (1 ITM CE/PE + Δ × Nifty levels).
/// </summary>
public sealed class NiftyOrbService
{
    public const string SourceOrb = "nifty_orb";
    public const string SourceOrbLiqV2 = "nifty_orb_liq_v2";
    public const string SourceLiqBreakout = "nifty_liq_breakout";
    public const string SourceBreakoutVolume = "nifty_breakout_volume";
    public const string SourceBreakoutChain = "nifty_breakout_chain";
    public const string SourceHeroZero = "nifty_hero_zero";
    private const decimal MaxBidAskSpreadPct = 5m;
    /// <summary>Wider than equity confluence — index entries can sit ~0.5% apart.</summary>
    private const decimal ComboPriceTolerancePct = 0.005m;
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private readonly IInstrumentRepository _instruments;
    private readonly IOptionsIntradayRepository _nfo;
    private readonly INiftyOrbRepository _repo;
    private readonly IMarketDataRepository _market;
    private readonly IAngelMarketDataClient _angel;
    private readonly NfoSyncService _nfoSync;
    private readonly IntradayBarsSyncService _intradaySync;
    private readonly SignalOutcomeService _outcomes;
    private readonly IndexOptionNotificationService _notifications;
    private readonly NiftyOptionChainService _chain;
    private readonly ILogger<NiftyOrbService> _logger;

    public NiftyOrbService(
        IInstrumentRepository instruments,
        IOptionsIntradayRepository nfo,
        INiftyOrbRepository repo,
        IMarketDataRepository market,
        IAngelMarketDataClient angel,
        NfoSyncService nfoSync,
        IntradayBarsSyncService intradaySync,
        SignalOutcomeService outcomes,
        IndexOptionNotificationService notifications,
        NiftyOptionChainService chain,
        ILogger<NiftyOrbService> logger)
    {
        _instruments = instruments;
        _nfo = nfo;
        _repo = repo;
        _market = market;
        _angel = angel;
        _nfoSync = nfoSync;
        _intradaySync = intradaySync;
        _outcomes = outcomes;
        _notifications = notifications;
        _chain = chain;
        _logger = logger;
    }

    public Task<IReadOnlyList<NiftyOrbRecommendationRow>> GetRecommendationsAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
        => _repo.GetRecommendationsAsync(userId, runId, ct);

    public async Task<NiftyOrbRunRow> RunAsync(Guid userId, CancellationToken ct = default)
    {
        var nowIst = DateTimeOffset.UtcNow.ToOffset(Ist);
        var asOf = DateOnly.FromDateTime(nowIst.DateTime);
        var runId = await _repo.CreateRunAsync(userId, asOf, ct);

        try
        {
            await _instruments.SeedSectorIndexIfMissingAsync("NIFTY", "Nifty 50", ct);

            var nifty = await _instruments.FindBySymbolAsync("NIFTY", ct)
                ?? throw new InvalidOperationException("NIFTY instrument not found after seed.");

            var niftyToken = await ResolveNiftyTokenAsync(nifty, ct);
            var existingNfo = await _nfo.GetNfoForUnderlyingAsync(nifty.Id, ct);
            if (!existingNfo.Any(c => c.Kind == "option"))
                await _nfoSync.SyncNiftyIndexNfoAsync(nifty.Id, ct);
            else
                _logger.LogDebug("Nifty OPTIDX already mapped — skip NFO re-sync");
            await EnsureNiftyDailyBarsAsync(niftyToken, ct);

            var from = asOf.ToDateTime(new TimeOnly(9, 0));
            var to = asOf.ToDateTime(new TimeOnly(15, 30));
            var candles = await _angel.GetFifteenMinuteCandlesAsync(
                niftyToken.Exchange, niftyToken.SymbolToken, from, to, ct);

            decimal? liveSpot = null;
            try
            {
                var quotes = await _angel.GetQuotesAsync(
                    QuoteModes.Ltp,
                    new Dictionary<string, IReadOnlyList<string>>
                    {
                        [niftyToken.Exchange] = new[] { niftyToken.SymbolToken }
                    },
                    ct);
                liveSpot = quotes.FirstOrDefault()?.Ltp;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nifty LTP quote failed");
            }

            var barTuples = candles
                .Where(c => c.BarTime is not null)
                .Select(c => (c.BarTime!.Value, c.High, c.Low, c.Close))
                .ToList();

            var orbSetups = NiftyOrbEvaluator.EvaluateAll(barTuples, asOf, liveSpot, nowIst);
            var session = orbSetups.FirstOrDefault();
            var spot = liveSpot
                ?? (barTuples.Count > 0 ? barTuples[^1].Close : session?.Entry ?? 0m);

            var recommended = orbSetups
                .Where(s => s.Status == "recommended" && s.Side is not null)
                .ToList();
            var waitingOrSkipped = orbSetups
                .Where(s => s.Status is "waiting" or "skipped" && s.Side is null)
                .ToList();

            if (recommended.Count == 0)
            {
                var display = waitingOrSkipped.FirstOrDefault() ?? session;
                if (display is not null)
                    await PersistStructural(runId, userId, nifty.Id, SourceOrb, display, spot, ct);

                if (display?.Status is "waiting")
                {
                    await PersistComboSkip(runId, userId, nifty.Id, display, spot,
                        "ORB waiting — combo evaluated when a side breaks", ct);
                }
                else if (display is not null)
                {
                    await PersistComboSkip(runId, userId, nifty.Id, display, spot,
                        $"ORB not actionable ({display.Status}: {display.SkipReason})", ct);
                }

                var liqInputsEarly = await LoadLiqInputsAsync(nifty, niftyToken, ct);
                await TryPersistLiqBreakoutAsync(
                    runId, userId, nifty, niftyToken, asOf, spot, liqInputsEarly, ct);
                await TryPersistBreakoutVolumeAsync(
                    runId, userId, nifty, asOf, spot, liqInputsEarly, ct);
                await TryPersistBreakoutChainAsync(
                    runId, userId, nifty, asOf, spot, liqInputsEarly, ct);
                await TryPersistHeroZeroAsync(
                    runId, userId, nifty, asOf, spot, orbSetups, liqInputsEarly, ct);
            }
            else
            {
                var liqInputs = await LoadLiqInputsAsync(nifty, niftyToken, ct);

                foreach (var orb in recommended)
                {
                    var orbTicket = await TryBuildAndPersistTicketAsync(
                        runId, userId, nifty.Id, SourceOrb, orb.Side!, spot,
                        orb.High, orb.Low, orb.Range,
                        orb.Entry, orb.StopLoss, orb.TargetT1, orb.TargetT2, orb.TargetT3,
                        orb.Reasons.ToList(), confidence: 80, ct);

                    if (orbTicket)
                        await TryPersistOrbLiqV2Async(
                            runId, userId, nifty, niftyToken, asOf, spot, orb, liqInputs, ct);
                    else
                    {
                        await PersistComboSkip(runId, userId, nifty.Id, orb, spot,
                            $"ORB {orb.Side} option ticket failed — combo skipped", ct);
                    }
                }

                // Skipped sides (e.g. first break spent while opposite still valid)
                foreach (var spent in orbSetups.Where(s =>
                    s.Side is not null && s.Status == "skipped"))
                {
                    await PersistSkipTicket(
                        runId, userId, nifty.Id, SourceOrb, spent.Side!, spot,
                        spent.High, spent.Low, spent.Range,
                        spent.Entry, spent.StopLoss, spent.TargetT1, spent.TargetT2, spent.TargetT3,
                        spent.Reasons.ToList(), spent.SkipReason ?? "skipped", ct);
                }

                await TryPersistLiqBreakoutAsync(
                    runId, userId, nifty, niftyToken, asOf, spot, liqInputs, ct);
                await TryPersistBreakoutVolumeAsync(
                    runId, userId, nifty, asOf, spot, liqInputs, ct);
                await TryPersistBreakoutChainAsync(
                    runId, userId, nifty, asOf, spot, liqInputs, ct);
                await TryPersistHeroZeroAsync(
                    runId, userId, nifty, asOf, spot, orbSetups, liqInputs, ct);
            }

            await _repo.CompleteRunAsync(runId, userId, "succeeded", null, ct);
            return Ok(runId, userId, asOf);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nifty ORB run {RunId} failed", runId);
            await _repo.CompleteRunAsync(runId, userId, "failed", ex.Message, ct);
            throw new InvalidOperationException($"Nifty ORB failed: {ex.Message}", ex);
        }
    }

    private async Task TryPersistOrbLiqV2Async(
        Guid runId, Guid userId, Instrument nifty, AngelTokenRow niftyToken,
        DateOnly asOf, decimal spot, NiftyOrbEvaluator.OrbLevels orb,
        (List<MarketIntradayBarRow> Bars1h, List<LiquidityAnalysisService.Ohlcv> Bars4h, List<MarketBarRow> Daily) inputs,
        CancellationToken ct)
    {
        var (bars1h, bars4h, daily) = inputs;

        var diag = new LiquidityV2Evaluator.Diagnostics();
        var liq = LiquidityV2Evaluator.TryEvaluate(
            userId, runId, asOf, niftyToken,
            bars1h, bars4h, daily,
            livePrice: spot > 0 ? spot : null,
            sectorConfirmed: false,
            niftyDailyNewestFirst: null,
            options: new LiquidityV2Evaluator.Options(ActionableOnly: true),
            diag: diag);

        if (liq is null)
        {
            await PersistComboSkip(runId, userId, nifty.Id, orb, spot,
                $"Liquidity V2 no setup ({diag.DescribeRejects()})", ct);
            return;
        }

        if (!string.Equals(liq.Side, orb.Side, StringComparison.OrdinalIgnoreCase))
        {
            await PersistComboSkip(runId, userId, nifty.Id, orb, spot,
                $"Side conflict: ORB {orb.Side} vs Liq V2 {liq.Side}", ct);
            return;
        }

        if (!ConfluenceLevelComposer.DatesAlign(asOf, liq.AsOfDate))
        {
            await PersistComboSkip(runId, userId, nifty.Id, orb, spot,
                $"Date misalignment ORB {asOf} vs Liq V2 {liq.AsOfDate}", ct);
            return;
        }

        if (!PricesAlignLoose(orb.Entry, liq.EntryPrice, liq.EntryPrice))
        {
            await PersistComboSkip(runId, userId, nifty.Id, orb, spot,
                $"Entry misalignment ORB {orb.Entry:0.00} vs Liq V2 {liq.EntryPrice:0.00} (>0.5%)", ct);
            return;
        }

        // ORB is the intraday trigger; SL = nearer (tighter) of ORB and Liq V2.
        var entry = orb.Entry;
        var sl = ConfluenceLevelComposer.NearerStopLoss(orb.Side!, orb.StopLoss, liq.InitialStopLoss);
        if (orb.Side == SignalSides.Buy && sl >= entry)
            sl = entry * (1m - ComboPriceTolerancePct);
        else if (orb.Side == SignalSides.Sell && sl <= entry)
            sl = entry * (1m + ComboPriceTolerancePct);

        var risk = Math.Abs(entry - sl);
        if (risk <= 0)
        {
            await PersistComboSkip(runId, userId, nifty.Id, orb, spot, "Combo zero risk after compose", ct);
            return;
        }

        decimal Target(decimal m) =>
            orb.Side == SignalSides.Buy
                ? Math.Round(entry + risk * m, 2, MidpointRounding.AwayFromZero)
                : Math.Round(entry - risk * m, 2, MidpointRounding.AwayFromZero);

        var t1 = Target(2m);
        var t2 = Target(3m);
        var t3 = Target(4m);

        var reasons = new List<string>
        {
            "ORB + Liquidity V2 confluence",
            $"ORB entry {orb.Entry:0.00} · Liq V2 entry {liq.EntryPrice:0.00}",
            $"Composed SL {sl:0.00} (nearer of ORB/Liq)",
            $"Liq V2 {liq.EventType} · score {liq.QualityScore} ({liq.ConfidenceRating})",
            $"Risk {risk:0.00} pts · T1 at 2R",
        };

        var ok = await TryBuildAndPersistTicketAsync(
            runId, userId, nifty.Id, SourceOrbLiqV2, orb.Side!, spot,
            orb.High, orb.Low, orb.Range,
            entry, sl, t1, t2, t3,
            reasons, confidence: Math.Max(85, liq.QualityScore), ct);

        if (!ok)
        {
            await PersistComboSkip(runId, userId, nifty.Id, orb, spot,
                "ORB+Liq V2 aligned but option ticket failed", ct);
        }
        else
        {
            _logger.LogInformation(
                "Nifty ORB+LiqV2: {Side} entry={Entry} SL={Sl} T1={T1} event={Event}",
                orb.Side, entry, sl, t1, liq.EventType);
        }
    }

    /// <summary>
    /// Nifty Liq V2 / Breakout charts → structural entry / SL / T1.
    /// Strike 15-min chart → premium entry / SL / T1 (+15/+20).
    /// Recommend only the strike with the highest Nifty↔premium match (≥70).
    /// Executable ticket is always the strike chart.
    /// </summary>
    private async Task TryPersistLiqBreakoutAsync(
        Guid runId, Guid userId, Instrument nifty, AngelTokenRow niftyToken,
        DateOnly asOf, decimal spot,
        (List<MarketIntradayBarRow> Bars1h, List<LiquidityAnalysisService.Ohlcv> Bars4h, List<MarketBarRow> Daily) inputs,
        CancellationToken ct)
    {
        var (bars1h, bars4h, daily) = inputs;
        var niftyReasons = new List<string>();

        LiquiditySignalRow? liq = null;
        var diag = new LiquidityV2Evaluator.Diagnostics();
        if (bars1h.Count >= 45 && daily.Count >= 35)
        {
            liq = LiquidityV2Evaluator.TryEvaluate(
                userId, runId, asOf, niftyToken,
                bars1h, bars4h, daily,
                livePrice: spot > 0 ? spot : null,
                sectorConfirmed: false,
                niftyDailyNewestFirst: null,
                options: new LiquidityV2Evaluator.Options(ActionableOnly: false),
                diag: diag);
        }

        AnalysisSignalRow? brk = daily.Count >= 5
            ? BreakoutSignalEvaluator.Evaluate(
                userId, runId, asOf, daily,
                livePrice: spot > 0 ? spot : null,
                actionableOnly: false,
                projectPartialSessionVolume: true)
            : null;

        if (liq is not null)
            niftyReasons.Add($"Nifty Liq V2 {liq.EventType} {liq.Side} entry {liq.EntryPrice:0.00} SL {liq.InitialStopLoss:0.00} T1 {liq.TargetT1:0.00}");
        else
            niftyReasons.Add($"Nifty Liq V2 none ({diag.DescribeRejects()})");

        if (brk is not null)
            niftyReasons.Add($"Nifty Breakout {brk.Side} entry {brk.EntryPrice:0.00} SL {brk.InitialStopLoss:0.00} T1 {brk.TargetT1:0.00}");
        else
            niftyReasons.Add("Nifty Breakout none");

        if (liq is null && brk is null)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceLiqBreakout, SignalSides.Buy, spot,
                0, 0, 0, spot, spot, null, null, null,
                niftyReasons, "No Nifty chart levels (need Liq V2 or Breakout)", ct);
            return;
        }

        if (liq is not null && brk is not null
            && !string.Equals(liq.Side, brk.Side, StringComparison.OrdinalIgnoreCase))
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceLiqBreakout, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss,
                brk.TargetT1, brk.TargetT2, brk.TargetT3,
                niftyReasons, $"Nifty side conflict: Breakout {brk.Side} vs Liq V2 {liq.Side}", ct);
            return;
        }

        var side = liq?.Side ?? brk!.Side;
        decimal niftyEntry;
        decimal niftySl;
        var niftyEntriesAlign = false;
        var bothEngines = liq is not null && brk is not null;

        if (bothEngines)
        {
            niftyEntriesAlign = PricesAlignLoose(brk!.EntryPrice, liq!.EntryPrice, liq.EntryPrice);
            niftyEntry = liq.EntryPrice;
            niftySl = ConfluenceLevelComposer.NearerStopLoss(side, brk.InitialStopLoss, liq.InitialStopLoss);
            if (side == SignalSides.Buy && niftySl >= niftyEntry)
                niftySl = niftyEntry * (1m - ComboPriceTolerancePct);
            else if (side == SignalSides.Sell && niftySl <= niftyEntry)
                niftySl = niftyEntry * (1m + ComboPriceTolerancePct);
            niftyReasons.Add(
                niftyEntriesAlign
                    ? $"Nifty composed entry {niftyEntry:0.00} (Liq) · SL {niftySl:0.00} (nearer) · entries aligned"
                    : $"Nifty composed entry {niftyEntry:0.00} (Liq) · SL {niftySl:0.00} · entries >0.5% apart");
        }
        else if (liq is not null)
        {
            niftyEntry = liq.EntryPrice;
            niftySl = liq.InitialStopLoss;
        }
        else
        {
            niftyEntry = brk!.EntryPrice;
            niftySl = brk.InitialStopLoss;
        }

        var niftyT1 = liq?.TargetT1 ?? brk?.TargetT1 ?? 0;
        var niftyT2 = liq?.TargetT2 ?? brk?.TargetT2 ?? 0;
        var niftyT3 = liq?.TargetT3 ?? brk?.TargetT3 ?? 0;

        niftyReasons.Insert(0,
            "Nifty chart levels + strike chart — ticket uses strike premium when match ≥55");

        var bothAgree = liq is not null && brk is not null
            && string.Equals(liq.Side, brk.Side, StringComparison.OrdinalIgnoreCase);

        if (bothAgree)
        {
            niftyReasons.Insert(0,
                "Liq V2 + Breakout same side — Δ × Nifty option ticket (1 ITM + ATM alt)");
            await TryBuildAndPersistTicketAsync(
                runId, userId, nifty.Id, SourceLiqBreakout, side, spot,
                0, 0, 0, niftyEntry, niftySl, niftyT1, niftyT2, niftyT3,
                niftyReasons,
                confidence: niftyEntriesAlign ? 78 : 72,
                ct);
            return;
        }

        await TryPersistPremiumStrikeTicketAsync(
            runId, userId, nifty.Id, SourceLiqBreakout, side, spot, asOf,
            niftyEntry, niftySl, niftyT1, niftyT2, niftyT3,
            niftyReasons, bothEngines, niftyEntriesAlign,
            minMatchScore: 55, ct);
    }

    /// <summary>
    /// Nifty daily breakout (2d high/low) with volume confirmation → Δ × Nifty option ticket.
    /// No Liquidity V2 requirement.
    /// </summary>
    private async Task TryPersistBreakoutVolumeAsync(
        Guid runId, Guid userId, Instrument nifty,
        DateOnly asOf, decimal spot,
        (List<MarketIntradayBarRow> Bars1h, List<LiquidityAnalysisService.Ohlcv> Bars4h, List<MarketBarRow> Daily) inputs,
        CancellationToken ct)
    {
        var daily = inputs.Daily;
        if (daily.Count < 5)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutVolume, SignalSides.Buy, spot,
                0, 0, 0, spot, spot, null, null, null,
                new List<string> { "Need Nifty daily bars for breakout" },
                "Insufficient Nifty daily history", ct);
            return;
        }

        var brk = BreakoutSignalEvaluator.Evaluate(
            userId, runId, asOf, daily,
            livePrice: spot > 0 ? spot : null,
            actionableOnly: false,
            projectPartialSessionVolume: true);

        if (brk is null)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutVolume, SignalSides.Buy, spot,
                0, 0, 0, spot, spot, null, null, null,
                new List<string> { "Nifty Breakout none (no 2d high/low break)" },
                "No Nifty breakout setup", ct);
            return;
        }

        if (!brk.VolumeOk)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutVolume, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss,
                brk.TargetT1, brk.TargetT2, brk.TargetT3,
                new List<string> { $"Nifty Breakout {brk.Side} but volume below 25% of prior 3-day avg" },
                "Breakout without volume confirmation", ct);
            return;
        }

        if (brk.TargetT1 is not decimal t1)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutVolume, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss, null, null, null,
                new List<string> { "Breakout targets already spent on live mark" },
                "Breakout setup spent", ct);
            return;
        }

        var reasons = new List<string>
        {
            "Nifty 2d high/low breakout + volume OK (no Liquidity V2)",
            $"Breakout {brk.Side} entry {brk.EntryPrice:0.00} · SL {brk.InitialStopLoss:0.00} · T1 {t1:0.00}",
            "Option ticket via Δ × Nifty levels (1 ITM primary + ATM alt)",
            "Option buying only — flat by 14:30 IST",
        };

        await TryBuildAndPersistTicketAsync(
            runId, userId, nifty.Id, SourceBreakoutVolume, brk.Side, spot,
            0, 0, 0,
            brk.EntryPrice, brk.InitialStopLoss, t1, brk.TargetT2 ?? 0, brk.TargetT3 ?? 0,
            reasons, confidence: 75, ct);
    }

    /// <summary>
    /// Pattern breakout + volume, then option-chain OI gate (PCR / put-call walls).
    /// Only recommends when chain agrees with breakout side → strike + premium ticket.
    /// </summary>
    private async Task TryPersistBreakoutChainAsync(
        Guid runId, Guid userId, Instrument nifty,
        DateOnly asOf, decimal spot,
        (List<MarketIntradayBarRow> Bars1h, List<LiquidityAnalysisService.Ohlcv> Bars4h, List<MarketBarRow> Daily) inputs,
        CancellationToken ct)
    {
        var daily = inputs.Daily;
        if (daily.Count < 5)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutChain, SignalSides.Buy, spot,
                0, 0, 0, spot, spot, null, null, null,
                new List<string> { "Need Nifty daily bars for breakout" },
                "Insufficient Nifty daily history", ct);
            return;
        }

        var brk = BreakoutSignalEvaluator.Evaluate(
            userId, runId, asOf, daily,
            livePrice: spot > 0 ? spot : null,
            actionableOnly: false,
            projectPartialSessionVolume: true);

        if (brk is null)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutChain, SignalSides.Buy, spot,
                0, 0, 0, spot, spot, null, null, null,
                new List<string> { "Nifty Breakout none (no 2d high/low break)" },
                "No Nifty breakout setup", ct);
            return;
        }

        if (!brk.VolumeOk)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutChain, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss,
                brk.TargetT1, brk.TargetT2, brk.TargetT3,
                new List<string> { $"Nifty Breakout {brk.Side} but volume below 25% of prior 3-day avg" },
                "Breakout without volume confirmation", ct);
            return;
        }

        if (brk.TargetT1 is not decimal t1)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutChain, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss, null, null, null,
                new List<string> { "Breakout targets already spent on live mark" },
                "Breakout setup spent", ct);
            return;
        }

        NiftyOptionChainSnapshot chainSnap;
        try
        {
            chainSnap = await _chain.GetSnapshotAsync(nifty.Id, spot > 0 ? spot : brk.EntryPrice, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nifty option chain snapshot failed");
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutChain, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss, t1, brk.TargetT2, brk.TargetT3,
                new List<string>
                {
                    $"Breakout {brk.Side} entry {brk.EntryPrice:0.00} · SL {brk.InitialStopLoss:0.00} · T1 {t1:0.00}",
                    $"Option chain fetch failed: {ex.Message}",
                },
                "Option chain unavailable", ct);
            return;
        }

        var gate = NiftyOptionChainAnalyzer.EvaluateBreakout(brk.Side, chainSnap.Metrics);
        var reasons = new List<string>
        {
            "Breakout + Volume + option chain OI confirmation",
            $"Breakout {brk.Side} entry {brk.EntryPrice:0.00} · SL {brk.InitialStopLoss:0.00} · T1 {t1:0.00}",
        };
        reasons.AddRange(gate.Reasons);
        reasons.Add("Option ticket via Δ × Nifty levels (1 ITM primary + ATM alt)");
        reasons.Add("Option buying only — flat by 14:30 IST");

        if (!gate.Confirmed)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceBreakoutChain, brk.Side, spot,
                0, 0, 0, brk.EntryPrice, brk.InitialStopLoss, t1, brk.TargetT2, brk.TargetT3,
                reasons, gate.Summary, ct);
            return;
        }

        await TryBuildAndPersistTicketAsync(
            runId, userId, nifty.Id, SourceBreakoutChain, brk.Side, spot,
            0, 0, 0,
            brk.EntryPrice, brk.InitialStopLoss, t1, brk.TargetT2 ?? 0, brk.TargetT3 ?? 0,
            reasons, confidence: 82, ct);
    }

    /// <summary>
    /// Hero Zero: far OTM lottery when ORB and/or Breakout+Volume gives a directional catalyst.
    /// Risk = full premium; targets = 2× / 3× / 5× premium. No bell alerts (speculative).
    /// </summary>
    private async Task TryPersistHeroZeroAsync(
        Guid runId, Guid userId, Instrument nifty,
        DateOnly asOf, decimal spot,
        IReadOnlyList<NiftyOrbEvaluator.OrbLevels> orbSetups,
        (List<MarketIntradayBarRow> Bars1h, List<LiquidityAnalysisService.Ohlcv> Bars4h, List<MarketBarRow> Daily) inputs,
        CancellationToken ct)
    {
        var daily = inputs.Daily;
        AnalysisSignalRow? brk = daily.Count >= 5
            ? BreakoutSignalEvaluator.Evaluate(
                userId, runId, asOf, daily,
                livePrice: spot > 0 ? spot : null,
                actionableOnly: false,
                projectPartialSessionVolume: true)
            : null;

        var catalysts = NiftyHeroZeroEvaluator.CollectCatalysts(orbSetups, brk);
        var setup = NiftyHeroZeroEvaluator.ResolveSetup(catalysts, orbSetups, brk);

        if (setup is null)
        {
            var reason = catalysts.Count == 0
                ? "Need ORB break or Breakout+Volume catalyst"
                : "Conflicting buy/sell catalysts — Hero Zero needs one clear side";
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, SignalSides.Buy, spot,
                0, 0, 0, spot, spot, null, null, null,
                new List<string> { reason }, reason, ct);
            return;
        }

        var nfo = await _nfo.GetNfoForUnderlyingAsync(nifty.Id, ct);
        var options = nfo.Where(c => c.Kind == "option").ToList();
        if (options.Count == 0)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, setup.Side, spot,
                0, 0, 0, setup.NiftyEntry, setup.NiftySl, setup.NiftyT1, null, null,
                setup.CatalystLabels, "No Nifty OPTIDX contracts mapped", ct);
            return;
        }

        var nearestExpiry = options.Min(o => o.Expiry);
        var expiryContracts = options.Where(o => o.Expiry == nearestExpiry).ToList();
        var expiryLabel = expiryContracts[0].ExpiryLabel;
        var angelName = expiryContracts[0].AngelName;

        var greeks = await _angel.GetOptionGreeksAsync(angelName, expiryLabel, ct);
        if (greeks.Count == 0)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, setup.Side, spot,
                0, 0, 0, setup.NiftyEntry, setup.NiftySl, setup.NiftyT1, null, null,
                setup.CatalystLabels, $"optionGreek unavailable ({angelName} {expiryLabel})", ct);
            return;
        }

        var candidate = HeroZeroStrikeSelector.SelectFarOtm(
            setup.Side, spot, greeks, expiryContracts, expiryLabel);
        if (candidate?.Contract?.SymbolToken is null)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, setup.Side, spot,
                0, 0, 0, setup.NiftyEntry, setup.NiftySl, setup.NiftyT1, null, null,
                setup.CatalystLabels,
                $"No far OTM strike (Δ {HeroZeroStrikeSelector.MinDelta:0.00}–{HeroZeroStrikeSelector.MaxDelta:0.00}, vol ≥{HeroZeroStrikeSelector.MinTradeVolume:0})",
                ct);
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(800), ct);
        var quote = await QuoteNfoAsync(candidate.Contract.SymbolToken, ct);
        if (quote.Ltp is null or <= 0)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, setup.Side, spot,
                0, 0, 0, setup.NiftyEntry, setup.NiftySl, setup.NiftyT1, null, null,
                setup.CatalystLabels, "Far OTM premium quote unavailable", ct);
            return;
        }

        if (!HeroZeroStrikeSelector.PremiumInBand(quote.Ltp.Value))
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, setup.Side, spot,
                0, 0, 0, setup.NiftyEntry, setup.NiftySl, setup.NiftyT1, null, null,
                setup.CatalystLabels,
                $"Premium ₹{quote.Ltp:0.00} outside Hero Zero band ₹{HeroZeroStrikeSelector.MinPremium:0}–₹{HeroZeroStrikeSelector.MaxPremium:0}",
                ct);
            return;
        }

        var spreadPct = SpreadPct(quote.Bid, quote.Ask);
        if (spreadPct is null || spreadPct > 8m)
        {
            await PersistSourceSkip(
                runId, userId, nifty.Id, SourceHeroZero, setup.Side, spot,
                0, 0, 0, setup.NiftyEntry, setup.NiftySl, setup.NiftyT1, null, null,
                setup.CatalystLabels,
                spreadPct is null
                    ? "Far OTM bid/ask depth unavailable"
                    : $"Bid/ask spread {spreadPct:0.00}% exceeds 8% (OTM)",
                ct);
            return;
        }

        var ticket = NiftyHeroZeroEvaluator.BuildPremiumTicket(quote.Ltp.Value);
        var longDelta = OptionStrikeSelector.ToLongOptionDelta(candidate.Delta) ?? candidate.Delta ?? 0.1m;
        var reasons = setup.CatalystLabels
            .Select(l => $"Catalyst: {l}")
            .Concat(ticket.Reasons)
            .Concat(new[]
            {
                $"Far OTM {candidate.Strike:0.##} {candidate.OptionType} · Δ {longDelta:0.00}",
                $"Bid/ask {spreadPct:0.00}% · vol {candidate.Volume:0}",
                "Not for bell alerts — speculative sizing only",
            })
            .ToArray();

        var row = new NiftyOrbRecommendationRow
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            UserId = userId,
            InstrumentId = nifty.Id,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Side = setup.Side,
            SignalSource = SourceHeroZero,
            Status = "recommended",
            SpotLtp = spot,
            UnderlyingEntry = setup.NiftyEntry,
            UnderlyingStopLoss = setup.NiftySl,
            UnderlyingTargetT1 = setup.NiftyT1,
            ConfidenceScore = setup.Confidence,
            Reasons = reasons,
            ContractTradingSymbol = candidate.Contract.TradingSymbol,
            ContractExpiryLabel = expiryLabel,
            ContractStrike = candidate.Strike,
            ContractOptionType = candidate.OptionType,
            ContractToken = candidate.Contract.SymbolToken,
            ContractLotSize = candidate.Contract.LotSize,
            PremiumLtp = ticket.Entry,
            PremiumStopLoss = ticket.StopLoss,
            PremiumTargetT1 = ticket.TargetT1,
            PremiumTargetT2 = ticket.TargetT2,
            PremiumTargetT3 = ticket.TargetT3,
            Delta = longDelta,
            Gamma = candidate.Gamma,
            Theta = candidate.Theta,
            Vega = candidate.Vega,
            ImpliedVolatility = candidate.Iv,
            TradeVolume = candidate.Volume,
            FlatByIst = "14:30",
        };

        await _repo.InsertRecommendationAsync(row, ct);
        await _outcomes.OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = nifty.Id,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Strategy = SourceHeroZero,
            Side = setup.Side,
            SignalDate = asOf,
            EntryPrice = ticket.Entry,
            InitialStopLoss = ticket.StopLoss,
            TargetT1 = ticket.TargetT1,
            TargetT2 = ticket.TargetT2,
            TargetT3 = ticket.TargetT3,
            SectorConfirmed = false,
        }, ct);

        _logger.LogInformation(
            "Hero Zero: {Side} {Strike}{Type} @ ₹{Prem} catalyst={Catalysts}",
            setup.Side, candidate.Strike, candidate.OptionType, ticket.Entry,
            string.Join(", ", setup.CatalystLabels));
    }

    private async Task TryPersistPremiumStrikeTicketAsync(
        Guid runId, Guid userId, Guid instrumentId, string source, string side, decimal spot, DateOnly asOf,
        decimal niftyEntry, decimal niftySl, decimal niftyT1, decimal niftyT2, decimal niftyT3,
        List<string> biasReasons, bool bothNiftyEngines, bool niftyEntriesAlign,
        int minMatchScore = NiftyPremiumStrikeEvaluator.MinMatchScore,
        CancellationToken ct = default)
    {
        var nfo = await _nfo.GetNfoForUnderlyingAsync(instrumentId, ct);
        var options = nfo.Where(c => c.Kind == "option").ToList();
        if (options.Count == 0)
        {
            await PersistSourceSkip(
                runId, userId, instrumentId, source, side, spot,
                0, 0, 0, niftyEntry, niftySl, niftyT1, niftyT2, niftyT3,
                biasReasons, "No Nifty OPTIDX contracts mapped", ct);
            return;
        }

        var nearestExpiry = options.Min(o => o.Expiry);
        var expiryContracts = options.Where(o => o.Expiry == nearestExpiry).ToList();
        var expiryLabel = expiryContracts[0].ExpiryLabel;
        var angelName = expiryContracts[0].AngelName;

        var greeks = await _angel.GetOptionGreeksAsync(angelName, expiryLabel, ct);
        if (greeks.Count == 0)
        {
            await PersistSourceSkip(
                runId, userId, instrumentId, source, side, spot,
                0, 0, 0, niftyEntry, niftySl, niftyT1, niftyT2, niftyT3,
                biasReasons, $"optionGreek unavailable ({angelName} {expiryLabel})", ct);
            return;
        }

        // ATM first (tighter spread for 15–20 pt scalps); 1 ITM as alternate.
        var (atm, itm) = OptionStrikeSelector.Select(
            side, spot, greeks, expiryContracts, expiryLabel);
        var candidates = new[] { atm, itm }
            .Where(c => c?.Contract?.SymbolToken is not null)
            .DistinctBy(c => c!.Strike)
            .ToList();
        if (candidates.Count == 0)
        {
            await PersistSourceSkip(
                runId, userId, instrumentId, source, side, spot,
                0, 0, 0, niftyEntry, niftySl, niftyT1, niftyT2, niftyT3,
                biasReasons,
                $"No liquid ATM/1ITM with Δ {OptionStrikeSelector.MinLongDelta:0.00}–{OptionStrikeSelector.MaxLongDelta:0.00}",
                ct);
            return;
        }

        var scored = new List<(
            OptionStrikeSelector.Candidate Cand,
            NfoQuoteSnapshot Quote,
            NiftyPremiumStrikeEvaluator.Result Chart,
            int Match,
            decimal Delta)>();

        foreach (var cand in candidates)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(800), ct);
            var quote = await QuoteNfoAsync(cand!.Contract!.SymbolToken!, ct);
            if (quote.Ltp is null or <= 0)
                continue;

            var spreadPct = SpreadPct(quote.Bid, quote.Ask);
            if (spreadPct is null || spreadPct > MaxBidAskSpreadPct)
                continue;

            var from = asOf.ToDateTime(new TimeOnly(9, 0));
            var to = asOf.ToDateTime(new TimeOnly(15, 30));
            var candles = await _angel.GetFifteenMinuteCandlesAsync(
                "NFO", cand.Contract.SymbolToken!, from, to, ct);
            var tuples = candles
                .Where(c => c.BarTime is not null)
                .Select(c => (c.BarTime!.Value, c.High, c.Low, c.Close))
                .ToList();

            var chart = NiftyPremiumStrikeEvaluator.Evaluate(tuples, asOf, quote.Ltp.Value);
            var delta = OptionStrikeSelector.ToLongOptionDelta(cand.Delta) ?? 0.5m;
            var match = chart.Status == "recommended"
                ? NiftyPremiumStrikeEvaluator.ScoreAgainstNifty(
                    niftyEntry, niftySl, niftyT1,
                    chart.Entry, chart.StopLoss, chart.TargetT1,
                    delta, bothNiftyEngines, niftyEntriesAlign)
                : 0;
            scored.Add((cand, quote, chart, match, delta));
        }

        if (scored.Count == 0)
        {
            await PersistSourceSkip(
                runId, userId, instrumentId, source, side, spot,
                0, 0, 0, niftyEntry, niftySl, niftyT1, niftyT2, niftyT3,
                biasReasons, "Strike premium quote/chart unavailable", ct);
            return;
        }

        var best = scored
            .OrderByDescending(x => x.Match)
            .ThenByDescending(x => x.Chart.Status == "recommended")
            .First();
        var chosen = best.Cand;
        var chosenChart = best.Chart;
        var chosenQuote = best.Quote;
        var longDelta = best.Delta;
        var matchScore = best.Match;
        var alt = scored
            .Where(x => x.Cand.Strike != chosen.Strike)
            .OrderByDescending(x => x.Match)
            .Select(x => x.Cand)
            .FirstOrDefault();
        decimal? altPrem = scored
            .Where(x => x.Cand.Strike != chosen.Strike)
            .Select(x => x.Quote.Ltp)
            .FirstOrDefault();

        var reasons = biasReasons
            .Concat(chosenChart.Reasons)
            .Concat(new[]
            {
                $"Nifty↔strike match {matchScore}/100 (need ≥{minMatchScore})",
                $"{chosen.Strike:0.##} {chosen.OptionType} · Δ {longDelta:0.00} · IV {chosen.Iv:0.0}%",
                $"Bid/ask {SpreadPct(chosenQuote.Bid, chosenQuote.Ask):0.00}%",
                "Ticket = strike chart entry/SL/T1 · Nifty levels for structure",
                "Option buying only — flat by 14:30 IST",
            })
            .ToArray();

        if (chosenChart.Status != "recommended"
            || matchScore < minMatchScore)
        {
            var skip = chosenChart.Status != "recommended"
                ? (chosenChart.SkipReason ?? chosenChart.Status)
                : $"Nifty vs strike match {matchScore} below {minMatchScore}";
            var waitOrSkip = chosenChart.Status == "waiting" ? "waiting" : "skipped";

            await _repo.InsertRecommendationAsync(new NiftyOrbRecommendationRow
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                UserId = userId,
                InstrumentId = instrumentId,
                AppSymbol = "NIFTY",
                InstrumentName = "Nifty 50",
                Side = side,
                SignalSource = source,
                Status = waitOrSkip,
                SkipReason = skip,
                SpotLtp = spot > 0 ? spot : null,
                UnderlyingEntry = niftyEntry,
                UnderlyingStopLoss = niftySl,
                UnderlyingTargetT1 = niftyT1 > 0 ? niftyT1 : null,
                UnderlyingTargetT2 = niftyT2 > 0 ? niftyT2 : null,
                UnderlyingTargetT3 = niftyT3 > 0 ? niftyT3 : null,
                ConfidenceScore = 0,
                Reasons = reasons,
                ContractTradingSymbol = chosen.Contract!.TradingSymbol,
                ContractExpiryLabel = expiryLabel,
                ContractStrike = chosen.Strike,
                ContractOptionType = chosen.OptionType,
                ContractToken = chosen.Contract.SymbolToken,
                ContractLotSize = chosen.Contract.LotSize,
                PremiumLtp = chosenQuote.Ltp,
                PremiumStopLoss = chosenChart.StopLoss > 0 ? chosenChart.StopLoss : null,
                PremiumTargetT1 = chosenChart.TargetT1 > 0 ? chosenChart.TargetT1 : null,
                PremiumTargetT2 = chosenChart.TargetT2 > 0 ? chosenChart.TargetT2 : null,
                PremiumTargetT3 = chosenChart.TargetT3 > 0 ? chosenChart.TargetT3 : null,
                Delta = longDelta,
                Gamma = chosen.Gamma,
                Theta = chosen.Theta,
                Vega = chosen.Vega,
                ImpliedVolatility = chosen.Iv,
                TradeVolume = chosen.Volume,
                AltTradingSymbol = alt?.Contract?.TradingSymbol,
                AltStrike = alt?.Strike,
                AltDelta = OptionStrikeSelector.ToLongOptionDelta(alt?.Delta),
                AltImpliedVolatility = alt?.Iv,
                AltPremiumLtp = altPrem,
                FlatByIst = "14:30",
            }, ct);
            return;
        }

        var row = new NiftyOrbRecommendationRow
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Side = side,
            SignalSource = source,
            Status = "recommended",
            SpotLtp = spot,
            UnderlyingEntry = niftyEntry,
            UnderlyingStopLoss = niftySl,
            UnderlyingTargetT1 = niftyT1 > 0 ? niftyT1 : null,
            UnderlyingTargetT2 = niftyT2 > 0 ? niftyT2 : null,
            UnderlyingTargetT3 = niftyT3 > 0 ? niftyT3 : null,
            ConfidenceScore = matchScore,
            Reasons = reasons,
            ContractTradingSymbol = chosen.Contract!.TradingSymbol,
            ContractExpiryLabel = expiryLabel,
            ContractStrike = chosen.Strike,
            ContractOptionType = chosen.OptionType,
            ContractToken = chosen.Contract.SymbolToken,
            ContractLotSize = chosen.Contract.LotSize,
            PremiumLtp = chosenChart.Entry,
            PremiumStopLoss = chosenChart.StopLoss,
            PremiumTargetT1 = chosenChart.TargetT1,
            PremiumTargetT2 = chosenChart.TargetT2,
            PremiumTargetT3 = chosenChart.TargetT3,
            Delta = longDelta,
            Gamma = chosen.Gamma,
            Theta = chosen.Theta,
            Vega = chosen.Vega,
            ImpliedVolatility = chosen.Iv,
            TradeVolume = chosen.Volume,
            AltTradingSymbol = alt?.Contract?.TradingSymbol,
            AltStrike = alt?.Strike,
            AltDelta = OptionStrikeSelector.ToLongOptionDelta(alt?.Delta),
            AltImpliedVolatility = alt?.Iv,
            AltPremiumLtp = altPrem,
            FlatByIst = "14:30",
        };

        await _repo.InsertRecommendationAsync(row, ct);
        await _outcomes.OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Strategy = source,
            Side = side,
            SignalDate = asOf,
            EntryPrice = chosenChart.Entry,
            InitialStopLoss = chosenChart.StopLoss,
            TargetT1 = chosenChart.TargetT1,
            TargetT2 = chosenChart.TargetT2,
            TargetT3 = chosenChart.TargetT3,
            SectorConfirmed = false,
        }, ct);
        await _notifications.TryNotifyAsync(row, ct);

        _logger.LogInformation(
            "Nifty Liq+Breakout strike chart: {Side} {Strike}{Type} prem entry={Entry} SL={Sl} T1={T1}",
            side, chosen.Strike, chosen.OptionType, chosenChart.Entry, chosenChart.StopLoss, chosenChart.TargetT1);
    }

    private async Task<(List<MarketIntradayBarRow> Bars1h, List<LiquidityAnalysisService.Ohlcv> Bars4h, List<MarketBarRow> Daily)>
        LoadLiqInputsAsync(Instrument nifty, AngelTokenRow niftyToken, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.2), ct);

        var existing1h = await _market.CountIntradayBarsAsync(
            nifty.Id, IntradayBarsSyncService.Interval1h, ct);
        var needForce = existing1h < 45;
        try
        {
            await _intradaySync.SyncInstrumentHourlyAsync(niftyToken, ct, force: needForce);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nifty 1h sync failed");
        }

        var bars1h = (await _market.GetIntradayBarsForInstrumentAsync(
            nifty.Id, IntradayBarsSyncService.Interval1h, 120, ct)).ToList();
        if (bars1h.Count < 45)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            try
            {
                await _intradaySync.SyncInstrumentHourlyAsync(niftyToken, ct, force: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nifty 1h sync retry failed");
            }
            bars1h = (await _market.GetIntradayBarsForInstrumentAsync(
                nifty.Id, IntradayBarsSyncService.Interval1h, 120, ct)).ToList();
        }

        var bars4h = LiquidityAnalysisService.Aggregate4h(bars1h);
        var daily = (await _market.GetBarsForInstrumentAsync(nifty.Id, 80, ct)).ToList();
        return (bars1h, bars4h, daily);
    }

    private async Task<bool> TryBuildAndPersistTicketAsync(
        Guid runId, Guid userId, Guid instrumentId, string source, string side, decimal spot,
        decimal orbHigh, decimal orbLow, decimal orbRange,
        decimal entry, decimal sl, decimal t1, decimal t2, decimal t3,
        List<string> baseReasons, int confidence, CancellationToken ct)
    {
        var nfo = await _nfo.GetNfoForUnderlyingAsync(instrumentId, ct);
        var options = nfo.Where(c => c.Kind == "option").ToList();
        if (options.Count == 0)
        {
            await PersistSkipTicket(runId, userId, instrumentId, source, side, spot,
                orbHigh, orbLow, orbRange, entry, sl, t1, t2, t3, baseReasons,
                "No Nifty OPTIDX contracts mapped", ct);
            return false;
        }

        var nearestExpiry = options.Min(o => o.Expiry);
        var expiryContracts = options.Where(o => o.Expiry == nearestExpiry).ToList();
        var expiryLabel = expiryContracts[0].ExpiryLabel;
        var angelName = expiryContracts[0].AngelName;

        var greeks = await _angel.GetOptionGreeksAsync(angelName, expiryLabel, ct);
        if (greeks.Count == 0)
        {
            await PersistSkipTicket(runId, userId, instrumentId, source, side, spot,
                orbHigh, orbLow, orbRange, entry, sl, t1, t2, t3, baseReasons,
                $"optionGreek unavailable ({angelName} {expiryLabel})", ct);
            return false;
        }

        var (primary, alt) = OptionStrikeSelector.SelectPreferItm(
            side, spot, greeks, expiryContracts, expiryLabel);
        if (primary is null || primary.Contract?.SymbolToken is null)
        {
            await PersistSkipTicket(runId, userId, instrumentId, source, side, spot,
                orbHigh, orbLow, orbRange, entry, sl, t1, t2, t3, baseReasons,
                $"No liquid 1ITM/ATM with Δ {OptionStrikeSelector.MinLongDelta:0.00}–{OptionStrikeSelector.MaxLongDelta:0.00}",
                ct);
            return false;
        }

        var pQuote = await QuoteNfoAsync(primary.Contract.SymbolToken, ct);
        if (pQuote.Ltp is null or <= 0)
        {
            await PersistSkipTicket(runId, userId, instrumentId, source, side, spot,
                orbHigh, orbLow, orbRange, entry, sl, t1, t2, t3, baseReasons,
                "Option premium quote unavailable", ct);
            return false;
        }

        var spreadPct = SpreadPct(pQuote.Bid, pQuote.Ask);
        if (spreadPct is null || spreadPct > MaxBidAskSpreadPct)
        {
            await PersistSkipTicket(runId, userId, instrumentId, source, side, spot,
                orbHigh, orbLow, orbRange, entry, sl, t1, t2, t3, baseReasons,
                spreadPct is null
                    ? "Bid/ask depth unavailable"
                    : $"Bid/ask spread {spreadPct:0.00}% exceeds {MaxBidAskSpreadPct:0.00}%",
                ct);
            return false;
        }

        decimal? altPrem = null;
        if (alt?.Contract?.SymbolToken is string altTok)
            altPrem = (await QuoteNfoAsync(altTok, ct)).Ltp;

        var longDelta = OptionStrikeSelector.ToLongOptionDelta(primary.Delta) ?? 0.5m;
        var premLevels = EstimatePremiumLevels(
            pQuote.Ltp.Value, longDelta, entry, sl, t1, t2, t3);

        var reasons = baseReasons
            .Concat(new[]
            {
                $"Primary 1 ITM {primary.Strike:0.##} {primary.OptionType}",
                alt is not null ? $"Alt ATM {alt.Strike:0.##} {alt.OptionType}" : "No ATM alternate",
                $"Δ {longDelta:0.00} · IV {primary.Iv:0.0}%",
                $"Option entry ₹{pQuote.Ltp:0.00} · SL ₹{premLevels.Sl:0.00} · T1 ₹{premLevels.T1:0.00}",
                $"Bid/ask {spreadPct:0.00}%",
                "Option buying only — flat by 14:30 IST",
            })
            .ToArray();

        var row = new NiftyOrbRecommendationRow
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Side = side,
            SignalSource = source,
            Status = "recommended",
            SpotLtp = spot,
            OrbHigh = orbHigh > 0 ? orbHigh : null,
            OrbLow = orbLow > 0 ? orbLow : null,
            OrbRange = orbRange > 0 ? orbRange : null,
            UnderlyingEntry = entry,
            UnderlyingStopLoss = sl,
            UnderlyingTargetT1 = t1,
            UnderlyingTargetT2 = t2,
            UnderlyingTargetT3 = t3,
            ConfidenceScore = confidence,
            Reasons = reasons,
            ContractTradingSymbol = primary.Contract.TradingSymbol,
            ContractExpiryLabel = expiryLabel,
            ContractStrike = primary.Strike,
            ContractOptionType = primary.OptionType,
            ContractToken = primary.Contract.SymbolToken,
            ContractLotSize = primary.Contract.LotSize,
            PremiumLtp = pQuote.Ltp,
            PremiumStopLoss = premLevels.Sl,
            PremiumTargetT1 = premLevels.T1,
            PremiumTargetT2 = premLevels.T2,
            PremiumTargetT3 = premLevels.T3,
            Delta = longDelta,
            Gamma = primary.Gamma,
            Theta = primary.Theta,
            Vega = primary.Vega,
            ImpliedVolatility = primary.Iv,
            TradeVolume = primary.Volume,
            AltTradingSymbol = alt?.Contract?.TradingSymbol
                ?? (alt is null ? null : $"NIFTY {alt.Strike:0.##} {alt.OptionType}"),
            AltStrike = alt?.Strike,
            AltDelta = OptionStrikeSelector.ToLongOptionDelta(alt?.Delta),
            AltImpliedVolatility = alt?.Iv,
            AltPremiumLtp = altPrem,
            FlatByIst = "14:30",
        };

        await _repo.InsertRecommendationAsync(row, ct);
        await _outcomes.OpenAsync(new SignalOutcomeRow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Strategy = source,
            Side = side,
            SignalDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Ist).DateTime),
            EntryPrice = entry,
            InitialStopLoss = sl,
            TargetT1 = t1,
            TargetT2 = t2,
            TargetT3 = t3,
            SectorConfirmed = false,
        }, ct);

        await _notifications.TryNotifyAsync(row, ct);

        _logger.LogInformation(
            "Nifty ticket {Source}: {Side} entry={Entry} SL={Sl} T1={T1} contract={Contract}",
            source, side, entry, sl, t1, row.ContractTradingSymbol);
        return true;
    }

    private async Task EnsureNiftyDailyBarsAsync(AngelTokenRow token, CancellationToken ct)
    {
        var existing = await _market.GetBarsForInstrumentAsync(token.InstrumentId, 40, ct);
        if (existing.Count >= 35)
            return;

        var to = DateTime.Now;
        var from = to.Date.AddDays(-120);
        var candles = await _angel.GetDailyCandlesAsync(token.Exchange, token.SymbolToken, from, to, ct);
        foreach (var c in candles)
        {
            await _market.UpsertMarketBarAsync(
                token.InstrumentId, c.TradeDate, c.Open, c.High, c.Low, c.Close, c.Volume, ct);
        }
    }

    private async Task<AngelTokenRow> ResolveNiftyTokenAsync(Instrument nifty, CancellationToken ct)
    {
        var sectorTokens = await _instruments.GetActiveTokensForSectorsAsync(ct);
        var niftyToken = sectorTokens.FirstOrDefault(t => t.InstrumentId == nifty.Id);
        if (niftyToken is not null)
            return niftyToken;

        var scrips = await _angel.DownloadScripMasterAsync(ct);
        var idx = scrips.FirstOrDefault(s =>
            s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)
            && (s.InstrumentType.Equals("AMXIDX", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Nifty", StringComparison.OrdinalIgnoreCase))
            && (s.Name.Equals("Nifty 50", StringComparison.OrdinalIgnoreCase)
                || s.Symbol.Equals("Nifty 50", StringComparison.OrdinalIgnoreCase))
            && !s.Name.Contains("500", StringComparison.OrdinalIgnoreCase));
        idx ??= scrips.FirstOrDefault(s =>
            s.ExchSeg.Equals("NSE", StringComparison.OrdinalIgnoreCase)
            && s.InstrumentType.Equals("AMXIDX", StringComparison.OrdinalIgnoreCase)
            && s.Name.Equals("NIFTY", StringComparison.OrdinalIgnoreCase)
            && !s.Name.Contains("500", StringComparison.OrdinalIgnoreCase));
        if (idx is null)
            throw new InvalidOperationException("No Angel NSE token for Nifty 50 index.");

        niftyToken = new AngelTokenRow
        {
            InstrumentId = nifty.Id,
            Exchange = "NSE",
            SymbolToken = idx.Token,
            TradingSymbol = idx.Symbol,
            Name = idx.Name,
            AppSymbol = "NIFTY",
        };
        await _instruments.UpsertAngelTokenAsync(niftyToken, ct);
        return niftyToken;
    }

    private async Task PersistStructural(
        Guid runId, Guid userId, Guid instrumentId, string source,
        NiftyOrbEvaluator.OrbLevels orb, decimal spot, CancellationToken ct)
    {
        await _repo.InsertRecommendationAsync(new NiftyOrbRecommendationRow
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Side = SignalSides.Buy,
            SignalSource = source,
            Status = orb.Status,
            SkipReason = orb.SkipReason,
            SpotLtp = spot > 0 ? spot : null,
            OrbHigh = orb.High > 0 ? orb.High : null,
            OrbLow = orb.Low > 0 ? orb.Low : null,
            OrbRange = orb.Range > 0 ? orb.Range : null,
            UnderlyingEntry = orb.Entry > 0 ? orb.Entry : spot,
            UnderlyingStopLoss = orb.StopLoss > 0 ? orb.StopLoss : spot,
            UnderlyingTargetT1 = orb.TargetT1 > 0 ? orb.TargetT1 : null,
            UnderlyingTargetT2 = orb.TargetT2 > 0 ? orb.TargetT2 : null,
            UnderlyingTargetT3 = orb.TargetT3 > 0 ? orb.TargetT3 : null,
            ConfidenceScore = 0,
            Reasons = orb.Reasons,
            FlatByIst = "14:30",
        }, ct);
    }

    private async Task PersistComboSkip(
        Guid runId, Guid userId, Guid instrumentId,
        NiftyOrbEvaluator.OrbLevels orb, decimal spot, string reason, CancellationToken ct)
    {
        await PersistSkipTicket(
            runId, userId, instrumentId, SourceOrbLiqV2,
            orb.Side ?? SignalSides.Buy, spot,
            orb.High, orb.Low, orb.Range,
            orb.Entry > 0 ? orb.Entry : spot,
            orb.StopLoss > 0 ? orb.StopLoss : spot,
            orb.TargetT1, orb.TargetT2, orb.TargetT3,
            orb.Reasons.ToList(), reason, ct);
    }

    private Task PersistSourceSkip(
        Guid runId, Guid userId, Guid instrumentId, string source, string side, decimal spot,
        decimal orbHigh, decimal orbLow, decimal orbRange,
        decimal entry, decimal sl, decimal? t1, decimal? t2, decimal? t3,
        List<string> baseReasons, string reason, CancellationToken ct)
        => PersistSkipTicket(
            runId, userId, instrumentId, source, side, spot,
            orbHigh, orbLow, orbRange, entry, sl, t1, t2, t3, baseReasons, reason, ct);

    private async Task PersistSkipTicket(
        Guid runId, Guid userId, Guid instrumentId, string source, string side, decimal spot,
        decimal orbHigh, decimal orbLow, decimal orbRange,
        decimal entry, decimal sl, decimal? t1, decimal? t2, decimal? t3,
        List<string> baseReasons, string reason, CancellationToken ct)
    {
        await _repo.InsertRecommendationAsync(new NiftyOrbRecommendationRow
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            UserId = userId,
            InstrumentId = instrumentId,
            AppSymbol = "NIFTY",
            InstrumentName = "Nifty 50",
            Side = side,
            SignalSource = source,
            Status = "skipped",
            SkipReason = reason,
            SpotLtp = spot > 0 ? spot : null,
            OrbHigh = orbHigh > 0 ? orbHigh : null,
            OrbLow = orbLow > 0 ? orbLow : null,
            OrbRange = orbRange > 0 ? orbRange : null,
            UnderlyingEntry = entry,
            UnderlyingStopLoss = sl,
            UnderlyingTargetT1 = t1 is > 0 ? t1 : null,
            UnderlyingTargetT2 = t2 is > 0 ? t2 : null,
            UnderlyingTargetT3 = t3 is > 0 ? t3 : null,
            ConfidenceScore = 0,
            Reasons = baseReasons.Append(reason).ToArray(),
            FlatByIst = "14:30",
        }, ct);
    }

    private static NiftyOrbRunRow Ok(Guid runId, Guid userId, DateOnly asOf) => new()
    {
        Id = runId, UserId = userId, AsOfDate = asOf, Status = "succeeded"
    };

    private sealed record NfoQuoteSnapshot(
        decimal? Ltp, long? Oi, long? Volume, decimal? Bid, decimal? Ask);

    private async Task<NfoQuoteSnapshot> QuoteNfoAsync(string token, CancellationToken ct)
    {
        try
        {
            var quotes = await _angel.GetQuotesAsync(
                QuoteModes.Full,
                new Dictionary<string, IReadOnlyList<string>> { ["NFO"] = new[] { token } },
                ct);
            var q = quotes.FirstOrDefault();
            return new NfoQuoteSnapshot(
                q?.Ltp, q?.OpenInterest, q?.TradeVolume, q?.BestBid, q?.BestAsk);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NFO quote failed for token {Token}", token);
            return new NfoQuoteSnapshot(null, null, null, null, null);
        }
    }

    private static decimal? SpreadPct(decimal? bid, decimal? ask)
    {
        if (bid is null or <= 0 || ask is null or <= 0 || ask < bid)
            return null;
        var mid = (bid.Value + ask.Value) / 2m;
        return mid <= 0 ? null : Math.Round((ask.Value - bid.Value) / mid * 100m, 4);
    }

    private static bool PricesAlignLoose(decimal a, decimal b, decimal reference)
    {
        if (reference <= 0) return false;
        return Math.Abs(a - b) / reference <= ComboPriceTolerancePct;
    }

    /// <summary>
    /// Map Nifty points to option premium levels using long delta
    /// (Δ premium ≈ Δ × Nifty move). Approximate — gamma/IV can change the path.
    /// </summary>
    internal static (decimal Sl, decimal T1, decimal? T2, decimal? T3) EstimatePremiumLevels(
        decimal premiumEntry,
        decimal longDelta,
        decimal niftyEntry,
        decimal niftySl,
        decimal niftyT1,
        decimal niftyT2,
        decimal niftyT3)
    {
        var d = longDelta <= 0 ? 0.5m : longDelta;
        var riskPts = Math.Abs(niftyEntry - niftySl);
        var t1Pts = Math.Abs(niftyT1 - niftyEntry);
        var t2Pts = Math.Abs(niftyT2 - niftyEntry);
        var t3Pts = Math.Abs(niftyT3 - niftyEntry);

        decimal RoundP(decimal v) => Math.Round(Math.Max(0.05m, v), 2, MidpointRounding.AwayFromZero);

        var sl = RoundP(premiumEntry - riskPts * d);
        if (sl >= premiumEntry)
            sl = RoundP(premiumEntry * 0.7m);

        return (
            sl,
            RoundP(premiumEntry + t1Pts * d),
            RoundP(premiumEntry + t2Pts * d),
            RoundP(premiumEntry + t3Pts * d));
    }
}
