using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Services;
using StockYouNeed.Domain;
using HotChocolate;

namespace StockYouNeed.Api.GraphQL;

public sealed class Query
{
    public async Task<UserRow?> Me(
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.GetUserAsync(user.UserId, ct);

    public async Task<IReadOnlyList<MarketLtpRow>> Ltp(
        [Service] IMarketDataRepository market,
        CancellationToken ct)
        => await market.GetAllLtpAsync(ct);

    public async Task<IReadOnlyList<MarketBarRow>> MarketBars(
        Guid? instrumentId,
        int limitDays,
        [Service] IMarketDataRepository market,
        CancellationToken ct)
        => await market.GetBarsAsync(instrumentId, limitDays <= 0 ? 10 : limitDays, ct);

    public async Task<IReadOnlyList<Instrument>> Universes(
        [Service] IInstrumentRepository instruments,
        CancellationToken ct)
        => await instruments.GetUniverseEquitiesAsync(ct);

    public async Task<IReadOnlyList<AnalysisSignalRow>> Signals(
        Guid? runId,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.GetSignalsAsync(user.UserId, runId, ct);

    public async Task<IReadOnlyList<LiquiditySignalRow>> LiquiditySignals(
        Guid? runId,
        string? ruleset,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.GetLiquiditySignalsAsync(user.UserId, runId, ruleset ?? "classic", ct);

    public async Task<IReadOnlyList<ConfluenceSignalRow>> ConfluenceSignals(
        [Service] ICurrentUserAccessor user,
        [Service] Application.Confluence.ConfluenceService confluence,
        CancellationToken ct)
        => await confluence.GetSignalsAsync(user.UserId, ct);

    public async Task<IReadOnlyList<BreakoutConfirmationRow>> BreakoutConfirmations(
        Guid? runId,
        [Service] ICurrentUserAccessor user,
        [Service] Application.Breakout.BreakoutAnalysisService breakout,
        CancellationToken ct)
        => await breakout.GetConfirmationsAsync(user.UserId, runId, ct);

    public async Task<IReadOnlyList<TradeConfidenceScoreRow>> TradeConfidenceScores(
        Guid? runId,
        [Service] ICurrentUserAccessor user,
        [Service] TradeConfidenceService tradeScore,
        CancellationToken ct)
        => await tradeScore.GetScoresAsync(user.UserId, runId, ct);

    public async Task<AnalyzeStockResult> AnalyzeStock(
        Guid instrumentId,
        [Service] ICurrentUserAccessor user,
        [Service] Application.Analyze.AnalyzeStockService analyze,
        CancellationToken ct)
    {
        try
        {
            return await analyze.AnalyzeAsync(user.UserId, instrumentId, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<OpenPositionRow>> OpenPositions(
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
    {
        await portfolio.RefreshPositionMarksFromLtpAsync(user.UserId, ct);
        return await portfolio.GetOpenPositionsAsync(user.UserId, ct);
    }

    public async Task<IReadOnlyList<WatchlistItemRow>> Watchlist(
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.GetWatchlistAsync(user.UserId, ct);

    public async Task<IReadOnlyList<OptionsIntradayRecommendationRow>> OptionsIntradayRecommendations(
        Guid? runId,
        [Service] ICurrentUserAccessor user,
        [Service] Application.OptionsIntraday.OptionsIntradayService options,
        CancellationToken ct)
    {
        try
        {
            return await options.GetRecommendationsAsync(user.UserId, runId, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<BacktestNoteRow>> BacktestNotes(
        Guid? instrumentId,
        string? strategy,
        [Service] ICurrentUserAccessor user,
        [Service] IBacktestRepository backtest,
        CancellationToken ct)
    {
        try
        {
            return await backtest.GetNotesAsync(user.UserId, instrumentId, NormalizeStrategy(strategy), ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<BacktestSymbolSummary> BacktestSummary(
        Guid instrumentId,
        string? strategy,
        double? minRiskReward,
        bool? sectorConfirmedOnly,
        [Service] ICurrentUserAccessor user,
        [Service] IBacktestRepository backtest,
        CancellationToken ct)
    {
        try
        {
            decimal? minRr = minRiskReward is null ? null : (decimal)minRiskReward.Value;
            return await backtest.GetSymbolSummaryAsync(
                user.UserId, instrumentId, NormalizeStrategy(strategy), minRr,
                sectorConfirmedOnly == true, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<BacktestSymbolSummary>> BacktestSummaries(
        string? strategy,
        double? minRiskReward,
        bool? sectorConfirmedOnly,
        [Service] ICurrentUserAccessor user,
        [Service] IBacktestRepository backtest,
        CancellationToken ct)
    {
        try
        {
            decimal? minRr = minRiskReward is null ? null : (decimal)minRiskReward.Value;
            return await backtest.GetSummariesAsync(
                user.UserId, NormalizeStrategy(strategy), minRr,
                sectorConfirmedOnly == true, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<SignalOutcomeRow>> SignalOutcomes(
        string? strategy,
        string? result,
        bool? sectorConfirmedOnly,
        [Service] ICurrentUserAccessor user,
        [Service] Application.Outcomes.SignalOutcomeService outcomes,
        CancellationToken ct)
    {
        try
        {
            return await outcomes.GetOutcomesAsync(
                user.UserId, NormalizeStrategy(strategy), NormalizeOutcomeResult(result),
                sectorConfirmedOnly == true, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<SignalOutcomeSummary>> SignalOutcomeSummaries(
        string? strategy,
        bool? sectorConfirmedOnly,
        [Service] ICurrentUserAccessor user,
        [Service] Application.Outcomes.SignalOutcomeService outcomes,
        CancellationToken ct)
    {
        try
        {
            return await outcomes.GetSummariesAsync(
                user.UserId, NormalizeStrategy(strategy), sectorConfirmedOnly == true, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    private static string? NormalizeStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy)) return null;
        var s = strategy.Trim().ToLowerInvariant();
        return s is "signals" or "liquidity" or "liquidity_fresh" or "confluence" or "trade_score" or "breakout" or "options_intraday" ? s : null;
    }

    private static string? NormalizeOutcomeResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        var s = result.Trim().ToLowerInvariant();
        return s is "target" or "sl" or "open" or "time_stop" ? s : null;
    }
}

public sealed class Mutation
{
    public async Task<AnalysisRunRow> RunAnalysis(
        bool includeNifty50,
        bool includeNifty100,
        bool includeWatchlist,
        bool includeSectorCheck,
        [Service] ICurrentUserAccessor user,
        [Service] AnalysisRunService analysis,
        CancellationToken ct)
        => await analysis.RunAsync(
            user.UserId,
            includeNifty50,
            includeNifty100,
            includeWatchlist,
            AnalysisTriggers.ManualRun,
            includeSectorCheck,
            ct);

    public async Task<AnalysisRunRow> RunLiquidityAnalysis(
        bool includeNifty50,
        bool includeNifty100,
        bool includeWatchlist,
        string? ruleset,
        [Service] ICurrentUserAccessor user,
        [Service] LiquidityAnalysisService analysis,
        CancellationToken ct)
        => await analysis.RunAsync(
            user.UserId,
            includeNifty50,
            includeNifty100,
            includeWatchlist,
            "manual",
            ct,
            ruleset ?? "classic");

    public async Task<TradeConfidenceRunRow> RunTradeConfidenceAnalysis(
        bool refreshSignals,
        bool refreshLiquidity,
        [Service] ICurrentUserAccessor user,
        [Service] TradeConfidenceService tradeScore,
        CancellationToken ct)
    {
        try
        {
            return await tradeScore.RunAsync(user.UserId, refreshSignals, refreshLiquidity, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<BreakoutAnalysisRunRow> RunBreakoutAnalysis(
        [Service] ICurrentUserAccessor user,
        [Service] Application.Breakout.BreakoutAnalysisService breakout,
        CancellationToken ct)
    {
        try
        {
            return await breakout.RunAsync(user.UserId, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<bool> AddToWatchlist(
        Guid instrumentId,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
    {
        await portfolio.AddWatchlistAsync(user.UserId, instrumentId, ct);
        return true;
    }

    public async Task<bool> RemoveFromWatchlist(
        Guid instrumentId,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
    {
        await portfolio.RemoveWatchlistAsync(user.UserId, instrumentId, ct);
        return true;
    }

    public async Task<Guid> OpenPositionFromSignal(
        Guid signalId,
        int quantityLots,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.OpenPositionFromSignalAsync(
            user.UserId, signalId, quantityLots <= 0 ? 1 : quantityLots, ct);

    public async Task<Guid> OpenPositionFromLiquiditySignal(
        Guid signalId,
        int quantityLots,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.OpenPositionFromLiquiditySignalAsync(
            user.UserId, signalId, quantityLots <= 0 ? 1 : quantityLots, ct);

    public async Task<Guid> OpenPositionFromConfluence(
        Guid liquiditySignalId,
        Guid analysisSignalId,
        int quantityLots,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.OpenPositionFromConfluenceAsync(
            user.UserId, liquiditySignalId, analysisSignalId, quantityLots <= 0 ? 1 : quantityLots, ct);

    public async Task<Guid> OpenPositionFromTradeScore(
        Guid scoreId,
        int quantityLots,
        [Service] ICurrentUserAccessor user,
        [Service] ITradeScoreRepository tradeScore,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
        => await portfolio.OpenPositionFromTradeScoreAsync(
            user.UserId, scoreId, quantityLots <= 0 ? 1 : quantityLots, tradeScore, ct);

    public async Task<bool> UpdateStopLoss(
        Guid positionId,
        decimal newStop,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
    {
        await portfolio.UpdateStopLossAsync(user.UserId, positionId, newStop, ct);
        return true;
    }

    public async Task<bool> ClosePosition(
        Guid positionId,
        decimal exitPrice,
        string closeReason,
        [Service] ICurrentUserAccessor user,
        [Service] IPortfolioRepository portfolio,
        CancellationToken ct)
    {
        var reason = string.IsNullOrWhiteSpace(closeReason) ? "manual" : closeReason;
        await portfolio.ClosePositionAsync(user.UserId, positionId, exitPrice, reason, ct);
        return true;
    }

    /// <summary>Fetches fresh LTP from Angel into market_ltp (works off-hours / weekends).</summary>
    public async Task<int> RefreshLtp(
        [Service] LtpPollService poller,
        CancellationToken ct)
        => await poller.PollOnceAsync(ct);

    public async Task<BacktestNoteRow> UpsertBacktestNote(
        BacktestNoteInput input,
        [Service] ICurrentUserAccessor user,
        [Service] IBacktestRepository backtest,
        CancellationToken ct)
    {
        var note = new BacktestNoteRow
        {
            Id = input.Id ?? Guid.Empty,
            UserId = user.UserId,
            InstrumentId = input.InstrumentId,
            Strategy = input.Strategy,
            Side = input.Side,
            SignalDate = input.SignalDate,
            EntryPrice = input.EntryPrice,
            InitialStopLoss = input.InitialStopLoss,
            TargetT1 = input.TargetT1,
            TargetT2 = input.TargetT2,
            TargetT3 = input.TargetT3,
            Result = input.Result,
            TargetLevel = input.TargetLevel,
            TargetHitPct = input.TargetHitPct,
            ExitPrice = input.ExitPrice,
            ExitDate = input.ExitDate,
            PnlPct = input.PnlPct,
            RMultiple = input.RMultiple,
            Notes = input.Notes ?? "",
            WouldTakeLive = input.WouldTakeLive,
            Source = "manual",
        };
        return await backtest.UpsertNoteAsync(note, ct);
    }

    public async Task<bool> DeleteBacktestNote(
        Guid noteId,
        [Service] ICurrentUserAccessor user,
        [Service] IBacktestRepository backtest,
        CancellationToken ct)
        => await backtest.DeleteNoteAsync(user.UserId, noteId, ct);

    public async Task<BacktestSymbolSummary> RunHistoricalBacktest(
        Guid instrumentId,
        string strategy,
        [Service] ICurrentUserAccessor user,
        [Service] BacktestService backtest,
        CancellationToken ct)
    {
        try
        {
            return await backtest.RunHistoricalAsync(user.UserId, instrumentId, strategy, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Always surface a readable message (not HotChocolate's "Unexpected Execution Error").
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage(ex.Message)
                    .SetCode("BACKTEST_FAILED")
                    .SetExtension("strategy", strategy)
                    .Build());
        }
    }

    public async Task<int> ResolveSignalOutcomes(
        [Service] ICurrentUserAccessor user,
        [Service] Application.Outcomes.SignalOutcomeService outcomes,
        CancellationToken ct)
    {
        try
        {
            return await outcomes.ResolveOpenAsync(user.UserId, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<int> BackfillSignalOutcomes(
        [Service] ICurrentUserAccessor user,
        [Service] Application.Outcomes.SignalOutcomeService outcomes,
        CancellationToken ct)
    {
        try
        {
            return await outcomes.BackfillFromLiveAsync(user.UserId, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }

    public async Task<OptionsIntradayRunRow> RunOptionsIntradayAnalysis(
        [Service] ICurrentUserAccessor user,
        [Service] Application.OptionsIntraday.OptionsIntradayService options,
        CancellationToken ct)
    {
        try
        {
            return await options.RunAsync(user.UserId, ct);
        }
        catch (Exception ex)
        {
            throw new GraphQLException(ex.Message);
        }
    }
}

public sealed class BacktestNoteInput
{
    public Guid? Id { get; set; }
    public Guid InstrumentId { get; set; }
    public string Strategy { get; set; } = "signals";
    public string Side { get; set; } = "buy";
    public DateOnly SignalDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal? TargetT1 { get; set; }
    public decimal? TargetT2 { get; set; }
    public decimal? TargetT3 { get; set; }
    public string Result { get; set; } = "open";
    public string? TargetLevel { get; set; }
    public decimal? TargetHitPct { get; set; }
    public decimal? ExitPrice { get; set; }
    public DateOnly? ExitDate { get; set; }
    public decimal? PnlPct { get; set; }
    public decimal? RMultiple { get; set; }
    public string? Notes { get; set; }
    public bool? WouldTakeLive { get; set; }
}
