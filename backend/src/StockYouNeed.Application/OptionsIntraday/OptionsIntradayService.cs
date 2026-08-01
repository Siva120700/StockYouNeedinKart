using Microsoft.Extensions.Logging;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Outcomes;
using StockYouNeed.Application.Signals;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.OptionsIntraday;

/// <summary>
/// Options Intraday recommendations: underlying Liquidity Fresh / Confluence decides
/// side + SL/T1; Angel optionGreek picks ATM/1ITM contract; premium is display-only.
/// </summary>
public sealed class OptionsIntradayService
{
    private const int MinConfidence = 75;
    private const decimal MaxBidAskSpreadPct = 5m;
    private static readonly TimeSpan FlatCutoffIst = new(15, 20, 0);

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
        var nowIst = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
        var asOf = DateOnly.FromDateTime(nowIst.DateTime);
        var afterCutoff = nowIst.TimeOfDay >= FlatCutoffIst;
        var runId = await _repo.CreateRunAsync(userId, asOf, ct);

        try
        {
            if (!afterCutoff)
                await _nfoSync.SyncUniverseNfoAsync(ct);

            var liquidity = await _portfolio.GetLiquiditySignalsAsync(userId, null, "fresh", ct);
            var signals = await _portfolio.GetSignalsAsync(userId, null, ct);
            var ltpMap = (await _market.GetAllLtpAsync(ct))
                .ToDictionary(x => x.InstrumentId, x => x.Ltp);

            var written = 0;
            var skippedFlip = 0;
            var seen = new HashSet<(Guid Inst, string Side)>();
            var openOutcomes = await _outcomes.GetOpenAsync(userId, ct);

            foreach (var liq in liquidity)
            {
                ct.ThrowIfCancellationRequested();
                var key = (liq.InstrumentId, liq.Side.ToLowerInvariant());
                if (!seen.Add(key)) continue;

                if (OppositeSignalFlipGuard.IsFlipAgainstOpen(
                        liq.InstrumentId, liq.Side, asOf, openOutcomes, out var flipReason))
                {
                    skippedFlip++;
                    _logger.LogInformation(
                        "Options Intraday skip {Symbol}: {Reason}", liq.AppSymbol, flipReason);
                    continue;
                }

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

                if (afterCutoff)
                {
                    await PersistSkipped(
                        runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        "not_checked", null, confidence, reasons,
                        "15:20 IST cutoff reached — no new option entry", analysisId, ct);
                    written++;
                    continue;
                }

                string? buildUp = null;
                decimal? premPct = null;
                if (futures.Count > 0)
                {
                    var fut = futures[0];
                    var fQuote = await QuoteNfoAsync(fut.SymbolToken, ct);
                    var fLtp = fQuote.Ltp;
                    var fOi = fQuote.Oi;
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
                        buildUp, premPct, confidence, reasons,
                        $"No liquid ATM/1ITM contract with Δ {OptionStrikeSelector.MinLongDelta:0.00}–{OptionStrikeSelector.MaxLongDelta:0.00} and volume ≥ {OptionStrikeSelector.MinTradeVolume:0}",
                        analysisId, ct);
                    written++;
                    continue;
                }

                decimal? prem = null;
                string? tradingSym = primary.Contract?.TradingSymbol;
                string? token = primary.Contract?.SymbolToken;
                int? lot = primary.Contract?.LotSize;
                if (token is null)
                {
                    await PersistSkipped(
                        runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons,
                        "Selected strike has no mapped NFO token; liquidity/spread cannot be verified",
                        analysisId, ct);
                    written++;
                    continue;
                }

                var pQuote = await QuoteNfoAsync(token, ct);
                prem = pQuote.Ltp;
                if (prem is null or <= 0)
                {
                    await PersistSkipped(
                        runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons,
                        "Option premium quote unavailable", analysisId, ct);
                    written++;
                    continue;
                }
                await _repo.UpdateNfoQuoteAsync(token, prem, pQuote.Oi, ct);

                var spreadPct = SpreadPct(pQuote.Bid, pQuote.Ask);
                if (spreadPct is null)
                {
                    await PersistSkipped(
                        runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons,
                        "Bid/ask depth unavailable; spread cannot be verified", analysisId, ct);
                    written++;
                    continue;
                }
                if (spreadPct > MaxBidAskSpreadPct)
                {
                    await PersistSkipped(
                        runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons,
                        $"Bid/ask spread {spreadPct:0.00}% exceeds {MaxBidAskSpreadPct:0.00}%",
                        analysisId, ct);
                    written++;
                    continue;
                }

                decimal? altPrem = null;
                if (alt?.Contract?.SymbolToken is string altTok)
                {
                    altPrem = (await QuoteNfoAsync(altTok, ct)).Ltp;
                }

                if (primary.Delta is not null) reasons.Add($"Δ {Math.Abs(primary.Delta.Value):0.00} (long)");
                if (primary.Iv is not null) reasons.Add($"IV {primary.Iv:0.0}%");
                reasons.Add($"Volume {primary.Volume:0}");
                reasons.Add($"Bid/ask spread {spreadPct:0.00}%");
                confidence = Math.Clamp(confidence + 10, 0, 99);

                if (confidence < MinConfidence)
                {
                    await PersistSkipped(
                        runId, userId, liq, source, entry, sl, t1, t2, t3, spot,
                        buildUp, premPct, confidence, reasons,
                        $"Confidence {confidence} below required {MinConfidence}; require Confluence or supportive futures OI",
                        analysisId, ct);
                    written++;
                    continue;
                }
                reasons.Add($"Confidence gate passed ({confidence} ≥ {MinConfidence})");

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
                    SectorConfirmed = liq.SectorConfirmed,
                }, ct);
                written++;
            }

            await _repo.CompleteRunAsync(runId, userId, "succeeded", null, ct);
            _logger.LogInformation(
                "Options Intraday run {RunId}: {Count} rows, skippedFlip={Flip}",
                runId, written, skippedFlip);

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
