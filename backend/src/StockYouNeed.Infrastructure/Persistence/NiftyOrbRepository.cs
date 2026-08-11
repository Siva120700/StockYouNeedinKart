using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class NiftyOrbRepository : INiftyOrbRepository
{
    private readonly IDbConnectionFactory _db;

    public NiftyOrbRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Guid> CreateRunAsync(Guid userId, DateOnly asOfDate, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO nifty_orb_runs (user_id, as_of_date, status)
            VALUES (@userId, @asOfDate, 'running')
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(sql, new { userId, asOfDate }, cancellationToken: ct));
    }

    public async Task CompleteRunAsync(
        Guid runId, Guid userId, string status, string? errorMessage, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE nifty_orb_runs
            SET status = @status, error_message = @errorMessage, finished_at = now()
            WHERE id = @runId AND user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { runId, userId, status, errorMessage }, cancellationToken: ct));
    }

    public async Task InsertRecommendationAsync(NiftyOrbRecommendationRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO nifty_orb_recommendations (
              id, run_id, user_id, instrument_id, app_symbol, instrument_name, side, signal_source,
              status, skip_reason, spot_ltp, orb_high, orb_low, orb_range,
              underlying_entry, underlying_stop_loss, underlying_target_t1, underlying_target_t2, underlying_target_t3,
              confidence_score, reasons,
              contract_trading_symbol, contract_expiry_label, contract_strike, contract_option_type,
              contract_token, contract_lot_size, premium_ltp,
              premium_stop_loss, premium_target_t1, premium_target_t2, premium_target_t3,
              delta, gamma, theta, vega, implied_volatility, trade_volume,
              alt_trading_symbol, alt_strike, alt_delta, alt_implied_volatility, alt_premium_ltp,
              flat_by_ist)
            VALUES (
              @Id, @RunId, @UserId, @InstrumentId, @AppSymbol, @InstrumentName, @Side::signal_side, @SignalSource,
              @Status, @SkipReason, @SpotLtp, @OrbHigh, @OrbLow, @OrbRange,
              @UnderlyingEntry, @UnderlyingStopLoss, @UnderlyingTargetT1, @UnderlyingTargetT2, @UnderlyingTargetT3,
              @ConfidenceScore, @Reasons,
              @ContractTradingSymbol, @ContractExpiryLabel, @ContractStrike, @ContractOptionType,
              @ContractToken, @ContractLotSize, @PremiumLtp,
              @PremiumStopLoss, @PremiumTargetT1, @PremiumTargetT2, @PremiumTargetT3,
              @Delta, @Gamma, @Theta, @Vega, @ImpliedVolatility, @TradeVolume,
              @AltTradingSymbol, @AltStrike, @AltDelta, @AltImpliedVolatility, @AltPremiumLtp,
              CAST(@FlatByIst AS time))
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<NiftyOrbRecommendationRow>> GetRecommendationsAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
    {
        var includeNotified = runId is null;
        var asOfDate = includeNotified
            ? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime)
            : default(DateOnly?);

        const string sql = """
            WITH latest AS (
              SELECT id FROM nifty_orb_runs
              WHERE user_id = @userId AND status = 'succeeded'
              ORDER BY started_at DESC
              LIMIT 1
            ),
            notified AS (
              SELECT DISTINCT recommendation_id
              FROM index_option_notifications
              WHERE @includeNotified
                AND user_id = @userId
                AND recommendation_id IS NOT NULL
                AND as_of_date = @asOfDate
            )
            SELECT
              r.id AS Id,
              r.run_id AS RunId,
              r.user_id AS UserId,
              r.instrument_id AS InstrumentId,
              r.app_symbol AS AppSymbol,
              r.instrument_name AS InstrumentName,
              r.side::text AS Side,
              r.signal_source AS SignalSource,
              r.status AS Status,
              r.skip_reason AS SkipReason,
              r.spot_ltp AS SpotLtp,
              r.orb_high AS OrbHigh,
              r.orb_low AS OrbLow,
              r.orb_range AS OrbRange,
              r.underlying_entry AS UnderlyingEntry,
              r.underlying_stop_loss AS UnderlyingStopLoss,
              r.underlying_target_t1 AS UnderlyingTargetT1,
              r.underlying_target_t2 AS UnderlyingTargetT2,
              r.underlying_target_t3 AS UnderlyingTargetT3,
              r.confidence_score AS ConfidenceScore,
              r.reasons AS Reasons,
              r.contract_trading_symbol AS ContractTradingSymbol,
              r.contract_expiry_label AS ContractExpiryLabel,
              r.contract_strike AS ContractStrike,
              r.contract_option_type AS ContractOptionType,
              r.contract_token AS ContractToken,
              r.contract_lot_size AS ContractLotSize,
              r.premium_ltp AS PremiumLtp,
              r.premium_stop_loss AS PremiumStopLoss,
              r.premium_target_t1 AS PremiumTargetT1,
              r.premium_target_t2 AS PremiumTargetT2,
              r.premium_target_t3 AS PremiumTargetT3,
              r.delta AS Delta,
              r.gamma AS Gamma,
              r.theta AS Theta,
              r.vega AS Vega,
              r.implied_volatility AS ImpliedVolatility,
              r.trade_volume AS TradeVolume,
              r.alt_trading_symbol AS AltTradingSymbol,
              r.alt_strike AS AltStrike,
              r.alt_delta AS AltDelta,
              r.alt_implied_volatility AS AltImpliedVolatility,
              r.alt_premium_ltp AS AltPremiumLtp,
              to_char(r.flat_by_ist, 'HH24:MI') AS FlatByIst
            FROM nifty_orb_recommendations r
            WHERE r.user_id = @userId
              AND (
                (@runId IS NOT NULL AND r.run_id = @runId)
                OR (@runId IS NULL AND (
                  r.run_id = (SELECT id FROM latest)
                  OR r.id IN (SELECT recommendation_id FROM notified)
                ))
              )
            ORDER BY
              CASE r.status WHEN 'recommended' THEN 0 WHEN 'waiting' THEN 1 ELSE 2 END,
              r.created_at DESC
            LIMIT 50
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<NiftyOrbRecommendationRow>(
            new CommandDefinition(
                sql,
                new { userId, runId, includeNotified, asOfDate },
                cancellationToken: ct));
        return rows.ToList();
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync(
            "SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
