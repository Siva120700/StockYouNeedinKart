using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class BacktestRepository : IBacktestRepository
{
    private readonly IDbConnectionFactory _db;

    public BacktestRepository(IDbConnectionFactory db) => _db = db;

    private const string SelectNoteSql = """
        SELECT
          n.id AS Id,
          n.user_id AS UserId,
          n.instrument_id AS InstrumentId,
          i.symbol AS AppSymbol,
          i.name AS InstrumentName,
          n.strategy AS Strategy,
          n.side::text AS Side,
          n.signal_date AS SignalDate,
          n.entry_price AS EntryPrice,
          n.initial_stop_loss AS InitialStopLoss,
          n.target_t1 AS TargetT1,
          n.target_t2 AS TargetT2,
          n.target_t3 AS TargetT3,
          n.result AS Result,
          n.target_level AS TargetLevel,
          n.target_hit_pct AS TargetHitPct,
          n.exit_price AS ExitPrice,
          n.exit_date AS ExitDate,
          n.pnl_pct AS PnlPct,
          n.r_multiple AS RMultiple,
          n.notes AS Notes,
          n.would_take_live AS WouldTakeLive,
          n.source AS Source,
          n.created_at AS CreatedAt,
          n.updated_at AS UpdatedAt
        FROM backtest_notes n
        JOIN instruments i ON i.id = n.instrument_id
        """;

    public async Task<IReadOnlyList<BacktestNoteRow>> GetNotesAsync(
        Guid userId, Guid? instrumentId, string? strategy, CancellationToken ct = default)
    {
        var sql = SelectNoteSql + """
            WHERE n.user_id = @userId
              AND (@instrumentId IS NULL OR n.instrument_id = @instrumentId)
              AND (@strategy IS NULL OR n.strategy = @strategy)
            ORDER BY n.signal_date DESC, n.created_at DESC
            LIMIT 500
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<BacktestNoteRow>(new CommandDefinition(
            sql, new { userId, instrumentId, strategy }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<BacktestSymbolSummary> GetSymbolSummaryAsync(
        Guid userId, Guid instrumentId, string? strategy, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              @instrumentId AS InstrumentId,
              COALESCE(i.symbol, '') AS AppSymbol,
              COALESCE(i.name, '') AS InstrumentName,
              @strategy AS StrategyFilter,
              COUNT(*)::int AS TimesInStrategy,
              COUNT(*) FILTER (WHERE n.result = 'target')::int AS TargetHits,
              COUNT(*) FILTER (WHERE n.result = 'sl')::int AS SlHits,
              COUNT(*) FILTER (WHERE n.result = 'skipped')::int AS Skipped,
              COUNT(*) FILTER (WHERE n.result = 'open')::int AS OpenCount,
              CASE
                WHEN COUNT(*) FILTER (WHERE n.result IN ('target', 'sl')) = 0 THEN NULL
                ELSE ROUND(
                  100.0 * COUNT(*) FILTER (WHERE n.result = 'target')
                  / COUNT(*) FILTER (WHERE n.result IN ('target', 'sl')),
                  2)
              END AS TargetHitRatePct,
              AVG(n.target_hit_pct) FILTER (WHERE n.target_hit_pct IS NOT NULL) AS AvgTargetHitPct
            FROM instruments i
            LEFT JOIN backtest_notes n
              ON n.instrument_id = i.id
             AND n.user_id = @userId
             AND (@strategy IS NULL OR n.strategy = @strategy)
            WHERE i.id = @instrumentId
            GROUP BY i.symbol, i.name
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var row = await conn.QuerySingleOrDefaultAsync<BacktestSymbolSummary>(new CommandDefinition(
            sql, new { userId, instrumentId, strategy }, cancellationToken: ct));
        return row ?? new BacktestSymbolSummary
        {
            InstrumentId = instrumentId,
            StrategyFilter = strategy,
        };
    }

    public async Task<BacktestNoteRow> UpsertNoteAsync(BacktestNoteRow note, CancellationToken ct = default)
    {
        NormalizeNote(note);

        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, note.UserId);

        if (note.Id == Guid.Empty)
        {
            const string insertSql = """
                INSERT INTO backtest_notes (
                  user_id, instrument_id, strategy, side, signal_date,
                  entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
                  result, target_level, target_hit_pct, exit_price, exit_date,
                  pnl_pct, r_multiple, notes, would_take_live, source)
                VALUES (
                  @UserId, @InstrumentId, @Strategy, @Side::signal_side, @SignalDate,
                  @EntryPrice, @InitialStopLoss, @TargetT1, @TargetT2, @TargetT3,
                  @Result, @TargetLevel, @TargetHitPct, @ExitPrice, @ExitDate,
                  @PnlPct, @RMultiple, @Notes, @WouldTakeLive, COALESCE(NULLIF(@Source, ''), 'manual'))
                RETURNING id
                """;
            note.Id = await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(insertSql, note, cancellationToken: ct));
        }
        else
        {
            const string updateSql = """
                UPDATE backtest_notes SET
                  instrument_id = @InstrumentId,
                  strategy = @Strategy,
                  side = @Side::signal_side,
                  signal_date = @SignalDate,
                  entry_price = @EntryPrice,
                  initial_stop_loss = @InitialStopLoss,
                  target_t1 = @TargetT1,
                  target_t2 = @TargetT2,
                  target_t3 = @TargetT3,
                  result = @Result,
                  target_level = @TargetLevel,
                  target_hit_pct = @TargetHitPct,
                  exit_price = @ExitPrice,
                  exit_date = @ExitDate,
                  pnl_pct = @PnlPct,
                  r_multiple = @RMultiple,
                  notes = @Notes,
                  would_take_live = @WouldTakeLive,
                  source = COALESCE(NULLIF(@Source, ''), source),
                  updated_at = now()
                WHERE id = @Id AND user_id = @UserId
                """;
            var affected = await conn.ExecuteAsync(new CommandDefinition(updateSql, note, cancellationToken: ct));
            if (affected == 0)
                throw new InvalidOperationException("Backtest note not found.");
        }

        var saved = await conn.QuerySingleAsync<BacktestNoteRow>(new CommandDefinition(
            SelectNoteSql + " WHERE n.id = @id AND n.user_id = @userId",
            new { id = note.Id, userId = note.UserId },
            cancellationToken: ct));
        return saved;
    }

    public async Task<bool> DeleteNoteAsync(Guid userId, Guid noteId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var affected = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM backtest_notes WHERE id = @noteId AND user_id = @userId",
            new { noteId, userId },
            cancellationToken: ct));
        return affected > 0;
    }

    private static void NormalizeNote(BacktestNoteRow note)
    {
        note.Strategy = note.Strategy.Trim().ToLowerInvariant();
        if (note.Strategy is not ("signals" or "liquidity"))
            throw new ArgumentException("Strategy must be 'signals' or 'liquidity'.");

        note.Side = note.Side.Trim().ToLowerInvariant();
        if (note.Side is not ("buy" or "sell"))
            throw new ArgumentException("Side must be 'buy' or 'sell'.");

        note.Result = note.Result.Trim().ToLowerInvariant();
        if (note.Result is not ("target" or "sl" or "skipped" or "open" or "time_stop"))
            throw new ArgumentException("Invalid result.");

        if (note.Result == "target")
        {
            note.TargetLevel = (note.TargetLevel ?? "t1").Trim().ToLowerInvariant();
            if (note.TargetLevel is not ("t1" or "t2" or "t3"))
                throw new ArgumentException("Target level must be t1, t2, or t3 when result is target.");
            note.TargetHitPct ??= 100m;
        }
        else
        {
            note.TargetLevel = string.IsNullOrWhiteSpace(note.TargetLevel) ? null : note.TargetLevel.Trim().ToLowerInvariant();
            if (note.Result == "sl" && note.TargetHitPct is null)
                note.TargetHitPct = 0m;
        }

        note.Notes ??= "";
        if (string.IsNullOrWhiteSpace(note.Source))
            note.Source = "manual";
    }

    public async Task DeleteAutoNotesAsync(
        Guid userId, Guid instrumentId, string strategy, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM backtest_notes
            WHERE user_id = @userId
              AND instrument_id = @instrumentId
              AND strategy = @strategy
              AND source = 'auto'
            """,
            new { userId, instrumentId, strategy },
            cancellationToken: ct));
    }

    public async Task InsertAutoNotesAsync(IReadOnlyList<BacktestNoteRow> notes, CancellationToken ct = default)
    {
        if (notes.Count == 0)
            return;

        const string sql = """
            INSERT INTO backtest_notes (
              user_id, instrument_id, strategy, side, signal_date,
              entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
              result, target_level, target_hit_pct, exit_price, exit_date,
              pnl_pct, r_multiple, notes, would_take_live, source)
            VALUES (
              @UserId, @InstrumentId, @Strategy, @Side::signal_side, @SignalDate,
              @EntryPrice, @InitialStopLoss, @TargetT1, @TargetT2, @TargetT3,
              @Result, @TargetLevel, @TargetHitPct, @ExitPrice, @ExitDate,
              @PnlPct, @RMultiple, @Notes, @WouldTakeLive, 'auto')
            """;

        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, notes[0].UserId);
        foreach (var note in notes)
        {
            NormalizeNote(note);
            note.Source = "auto";
            await conn.ExecuteAsync(new CommandDefinition(sql, note, cancellationToken: ct));
        }
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync("SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
