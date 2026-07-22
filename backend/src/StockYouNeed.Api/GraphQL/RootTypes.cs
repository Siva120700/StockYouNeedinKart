using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Services;
using StockYouNeed.Domain;

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
}
