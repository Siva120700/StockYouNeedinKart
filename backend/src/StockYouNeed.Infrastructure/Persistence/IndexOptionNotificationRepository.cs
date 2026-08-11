using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class IndexOptionNotificationRepository : IIndexOptionNotificationRepository
{
    private readonly IDbConnectionFactory _db;

    public IndexOptionNotificationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<bool> TryInsertAsync(IndexOptionNotificationRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO index_option_notifications (
              id, user_id, recommendation_id, signal_source, side, as_of_date,
              contract_strike, contract_option_type, premium_ltp, premium_stop_loss, premium_target_t1,
              confidence_score, title, body)
            VALUES (
              @Id, @UserId, @RecommendationId, @SignalSource, @Side::signal_side, @AsOfDate,
              @ContractStrike, @ContractOptionType, @PremiumLtp, @PremiumStopLoss, @PremiumTargetT1,
              @ConfidenceScore, @Title, @Body)
            ON CONFLICT (user_id, signal_source, side, as_of_date, contract_strike) DO NOTHING
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        var id = await conn.ExecuteScalarAsync<Guid?>(
            new CommandDefinition(sql, row, cancellationToken: ct));
        return id is not null;
    }

    public async Task<IReadOnlyList<IndexOptionNotificationRow>> GetAsync(
        Guid userId, bool unreadOnly, int limit, CancellationToken ct = default)
    {
        var take = limit <= 0 ? 30 : Math.Min(limit, 100);
        var sql = $"""
            SELECT
              id AS Id,
              user_id AS UserId,
              recommendation_id AS RecommendationId,
              signal_source AS SignalSource,
              side::text AS Side,
              as_of_date AS AsOfDate,
              contract_strike AS ContractStrike,
              contract_option_type AS ContractOptionType,
              premium_ltp AS PremiumLtp,
              premium_stop_loss AS PremiumStopLoss,
              premium_target_t1 AS PremiumTargetT1,
              confidence_score AS ConfidenceScore,
              title AS Title,
              body AS Body,
              read_at AS ReadAt,
              created_at AS CreatedAt
            FROM index_option_notifications
            WHERE user_id = @userId
              AND (@unreadOnly = false OR read_at IS NULL)
            ORDER BY created_at DESC
            LIMIT {take}
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<IndexOptionNotificationRow>(
            new CommandDefinition(sql, new { userId, unreadOnly }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> MarkReadAsync(
        Guid userId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return 0;
        const string sql = """
            UPDATE index_option_notifications
            SET read_at = now()
            WHERE user_id = @userId
              AND id = ANY(@ids)
              AND read_at IS NULL
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteAsync(
            new CommandDefinition(sql, new { userId, ids = ids.ToArray() }, cancellationToken: ct));
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync(
            "SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
