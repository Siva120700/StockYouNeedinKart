using System.Data;
using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class SignalOutcomeRepository : ISignalOutcomeRepository
{
    private readonly IDbConnectionFactory _db;

    public SignalOutcomeRepository(IDbConnectionFactory db) => _db = db;

    public async Task OpenAsync(SignalOutcomeRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO signal_outcomes (
              id, user_id, instrument_id, strategy, side, signal_date,
              entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
              result, analysis_signal_id, liquidity_signal_id,
              trade_confidence_score_id, breakout_confirmation_id, sector_confirmed)
            VALUES (
              @Id, @UserId, @InstrumentId, @Strategy, @Side::signal_side, @SignalDate,
              @EntryPrice, @InitialStopLoss, @TargetT1, @TargetT2, @TargetT3,
              'open', @AnalysisSignalId, @LiquiditySignalId,
              @TradeConfidenceScoreId, @BreakoutConfirmationId, @SectorConfirmed)
            ON CONFLICT (user_id, strategy, instrument_id, side, signal_date) DO NOTHING
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    private const string OutcomeSelect = """
              o.id AS Id,
              o.user_id AS UserId,
              o.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              o.strategy AS Strategy,
              o.side::text AS Side,
              o.signal_date AS SignalDate,
              o.entry_price AS EntryPrice,
              o.initial_stop_loss AS InitialStopLoss,
              o.target_t1 AS TargetT1,
              o.target_t2 AS TargetT2,
              o.target_t3 AS TargetT3,
              o.result AS Result,
              o.target_level AS TargetLevel,
              o.target_hit_pct AS TargetHitPct,
              o.exit_price AS ExitPrice,
              o.exit_date AS ExitDate,
              o.pnl_pct AS PnlPct,
              o.r_multiple AS RMultiple,
              o.analysis_signal_id AS AnalysisSignalId,
              o.liquidity_signal_id AS LiquiditySignalId,
              o.trade_confidence_score_id AS TradeConfidenceScoreId,
              o.breakout_confirmation_id AS BreakoutConfirmationId,
              o.sector_confirmed AS SectorConfirmed,
              o.created_at AS CreatedAt,
              o.updated_at AS UpdatedAt
            """;

    public async Task<IReadOnlyList<SignalOutcomeRow>> GetOpenAsync(Guid userId, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT
            {OutcomeSelect}
            FROM signal_outcomes o
            JOIN instruments i ON i.id = o.instrument_id
            WHERE o.user_id = @userId AND o.result = 'open'
            ORDER BY o.signal_date, i.symbol
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<SignalOutcomeRow>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SignalOutcomeRow>> GetOutcomesAsync(
        Guid userId, string? strategy, string? result, bool sectorConfirmedOnly = false,
        DateOnly? fromDate = null, DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        var filters = new List<string>
        {
            "o.user_id = @userId",
        };
        var p = new DynamicParameters();
        p.Add("userId", userId);

        if (!string.IsNullOrWhiteSpace(strategy))
        {
            filters.Add("o.strategy = @strategy");
            p.Add("strategy", strategy);
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            filters.Add("o.result = @result");
            p.Add("result", result);
        }

        if (sectorConfirmedOnly)
            filters.Add("o.sector_confirmed");

        if (fromDate is not null)
        {
            filters.Add("o.signal_date >= @fromDate");
            p.Add("fromDate", fromDate.Value.ToDateTime(TimeOnly.MinValue), DbType.Date);
        }

        if (toDate is not null)
        {
            filters.Add("o.signal_date <= @toDate");
            p.Add("toDate", toDate.Value.ToDateTime(TimeOnly.MinValue), DbType.Date);
        }

        var sql = $"""
            SELECT
            {OutcomeSelect}
            FROM signal_outcomes o
            JOIN instruments i ON i.id = o.instrument_id
            WHERE {string.Join("\n              AND ", filters)}
            ORDER BY o.signal_date DESC, i.symbol
            LIMIT 2000
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<SignalOutcomeRow>(
            new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task ResolveAsync(SignalOutcomeRow row, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE signal_outcomes SET
              result = @Result,
              target_level = @TargetLevel,
              target_hit_pct = @TargetHitPct,
              exit_price = @ExitPrice,
              exit_date = @ExitDate,
              pnl_pct = @PnlPct,
              r_multiple = @RMultiple,
              updated_at = now()
            WHERE id = @Id AND user_id = @UserId AND result = 'open'
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SignalOutcomeSummary>> GetSummariesAsync(
        Guid userId, string? strategy, bool sectorConfirmedOnly = false,
        DateOnly? fromDate = null, DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        var filters = new List<string>
        {
            "o.user_id = @userId",
        };
        var p = new DynamicParameters();
        p.Add("userId", userId);

        if (!string.IsNullOrWhiteSpace(strategy))
        {
            filters.Add("o.strategy = @strategy");
            p.Add("strategy", strategy);
        }

        if (sectorConfirmedOnly)
            filters.Add("o.sector_confirmed");

        if (fromDate is not null)
        {
            filters.Add("o.signal_date >= @fromDate");
            p.Add("fromDate", fromDate.Value.ToDateTime(TimeOnly.MinValue), DbType.Date);
        }

        if (toDate is not null)
        {
            filters.Add("o.signal_date <= @toDate");
            p.Add("toDate", toDate.Value.ToDateTime(TimeOnly.MinValue), DbType.Date);
        }

        var sql = $"""
            SELECT
              o.strategy AS StrategyFilter,
              COUNT(*)::int AS Setups,
              COUNT(*) FILTER (WHERE o.result = 'target')::int AS TargetHits,
              COUNT(*) FILTER (WHERE o.result = 'sl')::int AS SlHits,
              COUNT(*) FILTER (WHERE o.result = 'time_stop')::int AS TimeStops,
              COUNT(*) FILTER (WHERE o.result = 'open')::int AS OpenCount,
              CASE
                WHEN COUNT(*) FILTER (WHERE o.result IN ('target', 'sl')) = 0 THEN NULL
                ELSE ROUND(
                  100.0 * COUNT(*) FILTER (WHERE o.result = 'target')
                    / COUNT(*) FILTER (WHERE o.result IN ('target', 'sl')), 2)
              END AS TargetHitRatePct,
              AVG(o.target_hit_pct) FILTER (WHERE o.target_hit_pct IS NOT NULL) AS AvgTargetHitPct,
              AVG(
                ABS(o.target_t1 - o.entry_price)
                  / NULLIF(ABS(o.entry_price - o.initial_stop_loss), 0)
              ) FILTER (WHERE o.target_t1 IS NOT NULL) AS AvgRiskReward,
              AVG(o.r_multiple) FILTER (WHERE o.r_multiple IS NOT NULL) AS AvgRMultiple
            FROM signal_outcomes o
            WHERE {string.Join("\n              AND ", filters)}
            GROUP BY o.strategy
            ORDER BY o.strategy
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<SignalOutcomeSummary>(
            new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.ToList();
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync(
            "SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
