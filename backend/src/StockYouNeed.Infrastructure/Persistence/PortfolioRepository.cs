using System.Text.Json;
using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly IDbConnectionFactory _db;

    public PortfolioRepository(IDbConnectionFactory db) => _db = db;

    public async Task<UserRow?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, email::text AS Email, display_name AS DisplayName
            FROM users WHERE id = @userId
            """;
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AnalysisSignalRow>> GetSignalsAsync(Guid userId, Guid? runId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.id AS Id,
              s.analysis_run_id AS AnalysisRunId,
              s.user_id AS UserId,
              s.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              s.side::text AS Side,
              s.as_of_date AS AsOfDate,
              s.entry_price AS EntryPrice,
              s.initial_stop_loss AS InitialStopLoss,
              s.target_t1 AS TargetT1,
              s.target_t2 AS TargetT2,
              s.target_t3 AS TargetT3,
              s.volume_ok AS VolumeOk,
              s.sector_confirmed AS SectorConfirmed,
              s.fresh_cross AS FreshCross,
              s.ma_2d AS Ma2d,
              s.ma_3d AS Ma3d,
              s.ma_5d AS Ma5d,
              s.last_2d_high AS Last2dHigh,
              s.last_2d_low AS Last2dLow
            FROM analysis_signals s
            JOIN instruments i ON i.id = s.instrument_id
            WHERE s.user_id = @userId
              AND (
                (@runId IS NOT NULL AND s.analysis_run_id = @runId)
                OR (
                  @runId IS NULL
                  AND s.analysis_run_id = (
                    SELECT r.id FROM analysis_runs r
                    WHERE r.user_id = @userId AND r.status = 'succeeded'
                    ORDER BY r.started_at DESC
                    LIMIT 1
                  )
                )
              )
            ORDER BY s.created_at DESC
            LIMIT 500
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<AnalysisSignalRow>(new CommandDefinition(sql, new { userId, runId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<OpenPositionRow>> GetOpenPositionsAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              p.id AS Id,
              p.user_id AS UserId,
              p.instrument_id AS InstrumentId,
              i.symbol AS Symbol,
              i.name AS InstrumentName,
              p.side::text AS Side,
              p.quantity_lots AS QuantityLots,
              p.lot_size AS LotSize,
              p.quantity_units AS QuantityUnits,
              p.entry_price AS EntryPrice,
              p.current_stop_loss AS CurrentStopLoss,
              p.last_price AS LastPrice,
              p.unrealized_pnl_inr AS UnrealizedPnlInr,
              CASE
                WHEN p.side = 'buy'  THEN (COALESCE(p.last_price, p.entry_price) - p.entry_price) * p.quantity_units
                WHEN p.side = 'sell' THEN (p.entry_price - COALESCE(p.last_price, p.entry_price)) * p.quantity_units
              END AS ComputedUnrealizedPnl
            FROM positions p
            JOIN instruments i ON i.id = p.instrument_id
            WHERE p.user_id = @userId AND p.status = 'open'
            ORDER BY p.entry_at DESC
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<OpenPositionRow>(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<WatchlistItemRow>> GetWatchlistAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT w.user_id AS UserId, w.instrument_id AS InstrumentId,
                   i.symbol AS Symbol, i.name AS Name, w.sort_order AS SortOrder
            FROM user_watchlist_items w
            JOIN instruments i ON i.id = w.instrument_id
            WHERE w.user_id = @userId
            ORDER BY w.sort_order, i.symbol
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<WatchlistItemRow>(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task AddWatchlistAsync(Guid userId, Guid instrumentId, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO user_watchlist_items (user_id, instrument_id)
            VALUES (@userId, @instrumentId)
            ON CONFLICT DO NOTHING
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { userId, instrumentId }, cancellationToken: ct));
    }

    public async Task RemoveWatchlistAsync(Guid userId, Guid instrumentId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM user_watchlist_items WHERE user_id = @userId AND instrument_id = @instrumentId",
            new { userId, instrumentId }, cancellationToken: ct));
    }

    public async Task<Guid> CreateAnalysisRunAsync(
        Guid userId, string triggeredBy, bool nifty50, bool nifty100, bool watchlist, DateOnly asOfDate,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO analysis_runs (
              user_id, triggered_by, include_nifty_50, include_nifty_100, include_watchlist, as_of_date, status)
            VALUES (
              @userId, @triggeredBy::analysis_trigger, @nifty50, @nifty100, @watchlist, @asOfDate, 'running')
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            userId, triggeredBy, nifty50, nifty100, watchlist, asOfDate
        }, cancellationToken: ct));
    }

    public async Task CompleteAnalysisRunAsync(Guid runId, string status, string? error, object stats, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE analysis_runs
            SET finished_at = now(), status = @status, error_message = @error, stats = @stats::jsonb
            WHERE id = @runId
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            runId, status, error, stats = JsonSerializer.Serialize(stats)
        }, cancellationToken: ct));
    }

    public async Task InsertSignalAsync(AnalysisSignalRow signal, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO analysis_signals (
              id, analysis_run_id, user_id, instrument_id, side, as_of_date,
              entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
              last_2d_high, last_2d_low, volume_ok, sector_confirmed, fresh_cross,
              ma_2d, ma_3d, ma_5d, universe_tags)
            VALUES (
              @Id, @AnalysisRunId, @UserId, @InstrumentId, @Side::signal_side, @AsOfDate,
              @EntryPrice, @InitialStopLoss, @TargetT1, @TargetT2, @TargetT3,
              @Last2dHigh, @Last2dLow, @VolumeOk, @SectorConfirmed, @FreshCross,
              @Ma2d, @Ma3d, @Ma5d, ARRAY['nifty_50']::text[])
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, signal.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, signal, cancellationToken: ct));
    }

    public async Task<AnalysisSignalRow?> GetSignalAsync(Guid signalId, Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.id AS Id,
              s.analysis_run_id AS AnalysisRunId,
              s.user_id AS UserId,
              s.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              s.side::text AS Side,
              s.as_of_date AS AsOfDate,
              s.entry_price AS EntryPrice,
              s.initial_stop_loss AS InitialStopLoss,
              s.target_t1 AS TargetT1,
              s.target_t2 AS TargetT2,
              s.target_t3 AS TargetT3,
              s.volume_ok AS VolumeOk,
              s.sector_confirmed AS SectorConfirmed,
              s.fresh_cross AS FreshCross,
              s.ma_2d AS Ma2d,
              s.ma_3d AS Ma3d,
              s.ma_5d AS Ma5d,
              s.last_2d_high AS Last2dHigh,
              s.last_2d_low AS Last2dLow
            FROM analysis_signals s
            JOIN instruments i ON i.id = s.instrument_id
            WHERE s.id = @signalId AND s.user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.QuerySingleOrDefaultAsync<AnalysisSignalRow>(
            new CommandDefinition(sql, new { signalId, userId }, cancellationToken: ct));
    }

    public async Task<Guid> OpenPositionFromSignalAsync(Guid userId, Guid signalId, int quantityLots, CancellationToken ct = default)
    {
        var signal = await GetSignalAsync(signalId, userId, ct)
                     ?? throw new InvalidOperationException("Signal not found.");

        const string sql = """
            INSERT INTO positions (
              user_id, instrument_id, signal_id, side, status,
              quantity_lots, lot_size, quantity_units,
              entry_price, entry_as_of_date, current_stop_loss,
              target_t1, target_t2, target_t3, last_price)
            VALUES (
              @userId, @instrumentId, @signalId, @side::signal_side, 'open',
              @quantityLots, 1, @quantityLots,
              @entry, @asOf, @sl,
              @t1, @t2, @t3, @entry)
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            userId,
            instrumentId = signal.InstrumentId,
            signalId,
            side = signal.Side,
            quantityLots,
            entry = signal.EntryPrice,
            asOf = signal.AsOfDate,
            sl = signal.InitialStopLoss,
            t1 = signal.TargetT1,
            t2 = signal.TargetT2,
            t3 = signal.TargetT3
        }, cancellationToken: ct));
    }

    public async Task UpdateStopLossAsync(Guid userId, Guid positionId, decimal newStop, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);

        var previous = await conn.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            """
            SELECT current_stop_loss FROM positions
            WHERE id = @positionId AND user_id = @userId AND status = 'open'
            """,
            new { userId, positionId }, cancellationToken: ct));

        if (previous is null)
            throw new InvalidOperationException("Open position not found.");

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE positions
            SET current_stop_loss = @newStop, updated_at = now()
            WHERE id = @positionId AND user_id = @userId AND status = 'open'
            """,
            new { userId, positionId, newStop }, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO position_stop_loss_events
              (position_id, user_id, as_of_date, old_stop_loss, new_stop_loss, reason)
            VALUES (
              @positionId, @userId, (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Kolkata')::date,
              @previous, @newStop, 'manual_trail')
            """,
            new { positionId, userId, previous, newStop }, cancellationToken: ct));
    }

    public async Task ClosePositionAsync(Guid userId, Guid positionId, decimal exitPrice, string closeReason, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE positions
            SET status = 'closed',
                exit_price = @exitPrice,
                exit_at = now(),
                close_reason = @closeReason::close_reason,
                realized_pnl_inr = CASE
                  WHEN side = 'buy' THEN (@exitPrice - entry_price) * quantity_units
                  ELSE (entry_price - @exitPrice) * quantity_units
                END,
                updated_at = now()
            WHERE id = @positionId AND user_id = @userId AND status = 'open'
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            userId, positionId, exitPrice, closeReason
        }, cancellationToken: ct));
        if (affected == 0)
            throw new InvalidOperationException("Open position not found.");
    }

    public async Task RefreshPositionMarksFromLtpAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE positions p
            SET last_price = l.ltp,
                unrealized_pnl_inr = CASE
                  WHEN p.side = 'buy' THEN (l.ltp - p.entry_price) * p.quantity_units
                  ELSE (p.entry_price - l.ltp) * p.quantity_units
                END,
                updated_at = now()
            FROM market_ltp l
            WHERE p.instrument_id = l.instrument_id
              AND p.user_id = @userId
              AND p.status = 'open'
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> GetWatchlistInstrumentIdsAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(
            "SELECT instrument_id FROM user_watchlist_items WHERE user_id = @userId",
            new { userId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid> CreateLiquidityAnalysisRunAsync(
        Guid userId, string triggeredBy, bool nifty50, bool nifty100, bool watchlist, DateOnly asOfDate,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO liquidity_analysis_runs (
              user_id, triggered_by, include_nifty50, include_nifty100, include_watchlist, as_of_date, status)
            VALUES (
              @userId, @triggeredBy, @nifty50, @nifty100, @watchlist, @asOfDate, 'running')
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            userId, triggeredBy, nifty50, nifty100, watchlist, asOfDate
        }, cancellationToken: ct));
    }

    public async Task CompleteLiquidityAnalysisRunAsync(Guid runId, string status, string? error, object stats, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE liquidity_analysis_runs
            SET finished_at = now(), status = @status, error_message = @error, stats = @stats::jsonb
            WHERE id = @runId
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            runId, status, error, stats = JsonSerializer.Serialize(stats)
        }, cancellationToken: ct));
    }

    public async Task InsertLiquiditySignalAsync(LiquiditySignalRow signal, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO liquidity_signals (
              id, liquidity_run_id, user_id, instrument_id, side, as_of_date,
              entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
              relative_volume, rvol_percentile, rvol_ok, strong_close,
              sweep_side, swept_zone_type, swept_zone_price,
              nearest_zone_type, nearest_zone_price, distance_pct,
              zone_tags, timeframe_context)
            VALUES (
              @Id, @LiquidityRunId, @UserId, @InstrumentId, @Side::signal_side, @AsOfDate,
              @EntryPrice, @InitialStopLoss, @TargetT1, @TargetT2, @TargetT3,
              @RelativeVolume, @RvolPercentile, @RvolOk, @StrongClose,
              @SweepSide, @SweptZoneType, @SweptZonePrice,
              @NearestZoneType, @NearestZonePrice, @DistancePct,
              @ZoneTags, @TimeframeContext)
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, signal.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, signal, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LiquiditySignalRow>> GetLiquiditySignalsAsync(Guid userId, Guid? runId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.id AS Id,
              s.liquidity_run_id AS LiquidityRunId,
              s.user_id AS UserId,
              s.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              s.side::text AS Side,
              s.as_of_date AS AsOfDate,
              s.entry_price AS EntryPrice,
              s.initial_stop_loss AS InitialStopLoss,
              s.target_t1 AS TargetT1,
              s.target_t2 AS TargetT2,
              s.target_t3 AS TargetT3,
              s.relative_volume AS RelativeVolume,
              s.rvol_percentile AS RvolPercentile,
              s.rvol_ok AS RvolOk,
              s.strong_close AS StrongClose,
              s.sweep_side AS SweepSide,
              s.swept_zone_type AS SweptZoneType,
              s.swept_zone_price AS SweptZonePrice,
              s.nearest_zone_type AS NearestZoneType,
              s.nearest_zone_price AS NearestZonePrice,
              s.distance_pct AS DistancePct,
              s.zone_tags AS ZoneTags,
              s.timeframe_context AS TimeframeContext
            FROM liquidity_signals s
            JOIN instruments i ON i.id = s.instrument_id
            WHERE s.user_id = @userId
              AND (
                (@runId IS NOT NULL AND s.liquidity_run_id = @runId)
                OR (
                  @runId IS NULL
                  AND s.liquidity_run_id = (
                    SELECT r.id FROM liquidity_analysis_runs r
                    WHERE r.user_id = @userId AND r.status = 'succeeded'
                    ORDER BY r.started_at DESC
                    LIMIT 1
                  )
                )
              )
            ORDER BY s.created_at DESC
            LIMIT 500
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<LiquiditySignalRow>(
            new CommandDefinition(sql, new { userId, runId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<LiquiditySignalRow?> GetLiquiditySignalAsync(Guid signalId, Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.id AS Id,
              s.liquidity_run_id AS LiquidityRunId,
              s.user_id AS UserId,
              s.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              s.side::text AS Side,
              s.as_of_date AS AsOfDate,
              s.entry_price AS EntryPrice,
              s.initial_stop_loss AS InitialStopLoss,
              s.target_t1 AS TargetT1,
              s.target_t2 AS TargetT2,
              s.target_t3 AS TargetT3,
              s.relative_volume AS RelativeVolume,
              s.rvol_percentile AS RvolPercentile,
              s.rvol_ok AS RvolOk,
              s.strong_close AS StrongClose,
              s.sweep_side AS SweepSide,
              s.swept_zone_type AS SweptZoneType,
              s.swept_zone_price AS SweptZonePrice,
              s.nearest_zone_type AS NearestZoneType,
              s.nearest_zone_price AS NearestZonePrice,
              s.distance_pct AS DistancePct,
              s.zone_tags AS ZoneTags,
              s.timeframe_context AS TimeframeContext
            FROM liquidity_signals s
            JOIN instruments i ON i.id = s.instrument_id
            WHERE s.id = @signalId AND s.user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.QuerySingleOrDefaultAsync<LiquiditySignalRow>(
            new CommandDefinition(sql, new { signalId, userId }, cancellationToken: ct));
    }

    public async Task<Guid> OpenPositionFromLiquiditySignalAsync(Guid userId, Guid signalId, int quantityLots, CancellationToken ct = default)
    {
        var signal = await GetLiquiditySignalAsync(signalId, userId, ct)
                     ?? throw new InvalidOperationException("Liquidity signal not found.");

        const string sql = """
            INSERT INTO positions (
              user_id, instrument_id, signal_id, liquidity_signal_id, side, status,
              quantity_lots, lot_size, quantity_units,
              entry_price, entry_as_of_date, current_stop_loss,
              target_t1, target_t2, target_t3, last_price)
            VALUES (
              @userId, @instrumentId, NULL, @signalId, @side::signal_side, 'open',
              @quantityLots, 1, @quantityLots,
              @entry, @asOf, @sl,
              @t1, @t2, @t3, @entry)
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            userId,
            instrumentId = signal.InstrumentId,
            signalId,
            side = signal.Side,
            quantityLots,
            entry = signal.EntryPrice,
            asOf = signal.AsOfDate,
            sl = signal.InitialStopLoss,
            t1 = signal.TargetT1,
            t2 = signal.TargetT2,
            t3 = signal.TargetT3
        }, cancellationToken: ct));
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync("SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
