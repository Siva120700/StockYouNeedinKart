using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class OptionsIntradayRepository : IOptionsIntradayRepository
{
    private readonly IDbConnectionFactory _db;

    public OptionsIntradayRepository(IDbConnectionFactory db) => _db = db;

    public async Task ReplaceNfoContractsAsync(IReadOnlyList<NfoContractRow> rows, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        // Shared market data — no RLS user required.
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM nfo_contracts", cancellationToken: ct));
        if (rows.Count == 0) return;

        const string sql = """
            INSERT INTO nfo_contracts (
              id, underlying_instrument_id, app_symbol, angel_name, kind, option_type, strike,
              expiry, expiry_label, symbol_token, trading_symbol, lot_size, tick_size)
            VALUES (
              @Id, @UnderlyingInstrumentId, @AppSymbol, @AngelName, @Kind, @OptionType, @Strike,
              @Expiry, @ExpiryLabel, @SymbolToken, @TradingSymbol, @LotSize, @TickSize)
            ON CONFLICT (symbol_token) DO UPDATE SET
              underlying_instrument_id = EXCLUDED.underlying_instrument_id,
              app_symbol = EXCLUDED.app_symbol,
              angel_name = EXCLUDED.angel_name,
              kind = EXCLUDED.kind,
              option_type = EXCLUDED.option_type,
              strike = EXCLUDED.strike,
              expiry = EXCLUDED.expiry,
              expiry_label = EXCLUDED.expiry_label,
              trading_symbol = EXCLUDED.trading_symbol,
              lot_size = EXCLUDED.lot_size,
              tick_size = EXCLUDED.tick_size,
              updated_at = now()
            """;
        foreach (var row in rows)
            await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<NfoContractRow>> GetNfoForUnderlyingAsync(
        Guid underlyingInstrumentId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              id AS Id,
              underlying_instrument_id AS UnderlyingInstrumentId,
              app_symbol AS AppSymbol,
              angel_name AS AngelName,
              kind AS Kind,
              option_type AS OptionType,
              strike AS Strike,
              expiry AS Expiry,
              expiry_label AS ExpiryLabel,
              symbol_token AS SymbolToken,
              trading_symbol AS TradingSymbol,
              lot_size AS LotSize,
              tick_size AS TickSize,
              last_oi AS LastOi,
              last_ltp AS LastLtp
            FROM nfo_contracts
            WHERE underlying_instrument_id = @underlyingInstrumentId
            ORDER BY expiry, kind, option_type, strike
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<NfoContractRow>(
            new CommandDefinition(sql, new { underlyingInstrumentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task UpdateNfoQuoteAsync(string symbolToken, decimal? ltp, long? oi, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE nfo_contracts SET
              last_ltp = COALESCE(@ltp, last_ltp),
              last_oi = COALESCE(@oi, last_oi),
              updated_at = now()
            WHERE symbol_token = @symbolToken
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { symbolToken, ltp, oi }, cancellationToken: ct));
    }

    public async Task<Guid> CreateRunAsync(Guid userId, DateOnly asOfDate, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO options_intraday_runs (user_id, as_of_date, status)
            VALUES (@userId, @asOfDate, 'running')
            RETURNING id
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        return await conn.ExecuteScalarAsync<Guid>(
            new CommandDefinition(sql, new { userId, asOfDate }, cancellationToken: ct));
    }

    public async Task CompleteRunAsync(Guid runId, Guid userId, string status, string? errorMessage, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE options_intraday_runs SET
              status = @status,
              error_message = @errorMessage,
              finished_at = now()
            WHERE id = @runId AND user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { runId, userId, status, errorMessage }, cancellationToken: ct));
    }

    public async Task InsertRecommendationAsync(OptionsIntradayRecommendationRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO options_intraday_recommendations (
              id, run_id, user_id, instrument_id, app_symbol, instrument_name, side, signal_source,
              status, skip_reason, spot_ltp, underlying_entry, underlying_stop_loss,
              underlying_target_t1, underlying_target_t2, underlying_target_t3,
              futures_build_up, futures_premium_pct, confidence_score, reasons,
              contract_trading_symbol, contract_expiry_label, contract_strike, contract_option_type,
              contract_token, contract_lot_size, premium_ltp,
              delta, gamma, theta, vega, implied_volatility, trade_volume,
              alt_trading_symbol, alt_strike, alt_delta, alt_implied_volatility, alt_premium_ltp,
              flat_by_ist, liquidity_signal_id, analysis_signal_id)
            VALUES (
              @Id, @RunId, @UserId, @InstrumentId, @AppSymbol, @InstrumentName, @Side::signal_side, @SignalSource,
              @Status, @SkipReason, @SpotLtp, @UnderlyingEntry, @UnderlyingStopLoss,
              @UnderlyingTargetT1, @UnderlyingTargetT2, @UnderlyingTargetT3,
              @FuturesBuildUp, @FuturesPremiumPct, @ConfidenceScore, @Reasons,
              @ContractTradingSymbol, @ContractExpiryLabel, @ContractStrike, @ContractOptionType,
              @ContractToken, @ContractLotSize, @PremiumLtp,
              @Delta, @Gamma, @Theta, @Vega, @ImpliedVolatility, @TradeVolume,
              @AltTradingSymbol, @AltStrike, @AltDelta, @AltImpliedVolatility, @AltPremiumLtp,
              CAST(@FlatByIst AS time), @LiquiditySignalId, @AnalysisSignalId)
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OptionsIntradayRecommendationRow>> GetRecommendationsAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
    {
        const string sql = """
            WITH latest AS (
              SELECT id
              FROM options_intraday_runs
              WHERE user_id = @userId AND status = 'succeeded'
              ORDER BY started_at DESC
              LIMIT 1
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
              r.underlying_entry AS UnderlyingEntry,
              r.underlying_stop_loss AS UnderlyingStopLoss,
              r.underlying_target_t1 AS UnderlyingTargetT1,
              r.underlying_target_t2 AS UnderlyingTargetT2,
              r.underlying_target_t3 AS UnderlyingTargetT3,
              r.futures_build_up AS FuturesBuildUp,
              r.futures_premium_pct AS FuturesPremiumPct,
              r.confidence_score AS ConfidenceScore,
              r.reasons AS Reasons,
              r.contract_trading_symbol AS ContractTradingSymbol,
              r.contract_expiry_label AS ContractExpiryLabel,
              r.contract_strike AS ContractStrike,
              r.contract_option_type AS ContractOptionType,
              r.contract_token AS ContractToken,
              r.contract_lot_size AS ContractLotSize,
              r.premium_ltp AS PremiumLtp,
              ABS(r.delta) AS Delta,
              r.gamma AS Gamma,
              r.theta AS Theta,
              r.vega AS Vega,
              r.implied_volatility AS ImpliedVolatility,
              r.trade_volume AS TradeVolume,
              r.alt_trading_symbol AS AltTradingSymbol,
              r.alt_strike AS AltStrike,
              ABS(r.alt_delta) AS AltDelta,
              r.alt_implied_volatility AS AltImpliedVolatility,
              r.alt_premium_ltp AS AltPremiumLtp,
              to_char(r.flat_by_ist, 'HH24:MI') AS FlatByIst,
              r.liquidity_signal_id AS LiquiditySignalId,
              r.analysis_signal_id AS AnalysisSignalId
            FROM options_intraday_recommendations r
            WHERE r.user_id = @userId
              AND (
                (@runId IS NOT NULL AND r.run_id = @runId)
                OR (@runId IS NULL AND r.run_id = (SELECT id FROM latest))
              )
            ORDER BY r.confidence_score DESC, r.app_symbol
            LIMIT 500
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<OptionsIntradayRecommendationRow>(
            new CommandDefinition(sql, new { userId, runId }, cancellationToken: ct));
        return rows.ToList();
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync(
            "SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
