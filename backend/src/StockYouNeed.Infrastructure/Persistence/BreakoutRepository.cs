using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class BreakoutRepository : IBreakoutRepository
{
    private readonly IDbConnectionFactory _db;

    public BreakoutRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Guid> CreateRunAsync(
        Guid userId, string triggeredBy, DateOnly asOfDate, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO breakout_analysis_runs (user_id, triggered_by, as_of_date, status)
            VALUES (@userId, @triggeredBy, @asOfDate, 'running')
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(
            sql, new { userId, triggeredBy, asOfDate }, cancellationToken: ct));
    }

    public async Task CompleteRunAsync(
        Guid runId, Guid userId, string status, string? errorMessage, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE breakout_analysis_runs
            SET status = @status, error_message = @errorMessage, finished_at = now()
            WHERE id = @runId AND user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { runId, userId, status, errorMessage }, cancellationToken: ct));
    }

    public async Task InsertConfirmationAsync(BreakoutConfirmationRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO breakout_confirmations (
              id, run_id, user_id, instrument_id, side, as_of_date, confirmed,
              close_price, level_20d, volume_ratio, adx, rsi, atr, atr_expansion, pattern_type)
            VALUES (
              @Id, @RunId, @UserId, @InstrumentId, CAST(@Side AS signal_side), @AsOfDate, @Confirmed,
              @ClosePrice, @Level20d, @VolumeRatio, @Adx, @Rsi, @Atr, @AtrExpansion, @PatternType)
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<BreakoutConfirmationRow>> GetConfirmationsAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              b.id AS Id,
              b.run_id AS RunId,
              b.user_id AS UserId,
              b.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              b.side::text AS Side,
              b.as_of_date AS AsOfDate,
              b.confirmed AS Confirmed,
              b.close_price AS ClosePrice,
              b.level_20d AS Level20d,
              b.volume_ratio AS VolumeRatio,
              b.adx AS Adx,
              b.rsi AS Rsi,
              b.atr AS Atr,
              b.atr_expansion AS AtrExpansion,
              b.pattern_type AS PatternType
            FROM breakout_confirmations b
            JOIN instruments i ON i.id = b.instrument_id
            WHERE b.user_id = @userId
              AND (
                (@runId IS NOT NULL AND b.run_id = @runId)
                OR (
                  @runId IS NULL
                  AND b.run_id = (
                    SELECT r.id FROM breakout_analysis_runs r
                    WHERE r.user_id = @userId AND r.status = 'succeeded'
                    ORDER BY r.started_at DESC LIMIT 1
                  )
                )
              )
            ORDER BY b.confirmed DESC, b.volume_ratio DESC NULLS LAST, i.symbol
            LIMIT 500
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<BreakoutConfirmationRow>(new CommandDefinition(
            sql, new { userId, runId }, cancellationToken: ct));
        return rows.ToList();
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync(
            "SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
