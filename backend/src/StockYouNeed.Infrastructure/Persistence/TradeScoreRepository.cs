using System.Text.Json;
using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class TradeScoreRepository : ITradeScoreRepository
{
    private readonly IDbConnectionFactory _db;

    public TradeScoreRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Guid> CreateRunAsync(
        Guid userId, string triggeredBy, DateOnly asOfDate, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO trade_confidence_runs (user_id, triggered_by, as_of_date, status)
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
            UPDATE trade_confidence_runs
            SET status = @status, error_message = @errorMessage, finished_at = now()
            WHERE id = @runId AND user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(
            sql, new { runId, userId, status, errorMessage }, cancellationToken: ct));
    }

    public async Task InsertBreakoutAsync(
        Guid runId, Guid userId, Guid instrumentId, string side, DateOnly asOfDate,
        bool confirmed, decimal close, decimal level20d, decimal volRatio,
        decimal? adx, decimal? rsi, decimal? atr, bool atrExpansion, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO analysis_breakout (
              run_id, user_id, instrument_id, side, as_of_date, confirmed,
              close_price, level_20d, volume_ratio, adx, rsi, atr, atr_expansion)
            VALUES (
              @runId, @userId, @instrumentId, @side::signal_side, @asOfDate, @confirmed,
              @close, @level20d, @volRatio, @adx, @rsi, @atr, @atrExpansion)
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            runId, userId, instrumentId, side, asOfDate, confirmed,
            close, level20d, volRatio, adx, rsi, atr, atrExpansion
        }, cancellationToken: ct));
    }

    public async Task InsertScoreAsync(TradeConfidenceScoreRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO trade_confidence_scores (
              id, run_id, user_id, instrument_id, side, as_of_date,
              confidence_score, rating,
              signals_score, liquidity_score, breakout_score, futures_score, options_score,
              reasons, entry_price, initial_stop_loss, target_t1, target_t2, target_t3,
              analysis_signal_id, liquidity_signal_id)
            VALUES (
              @Id, @RunId, @UserId, @InstrumentId, @Side::signal_side, @AsOfDate,
              @ConfidenceScore, @Rating,
              @SignalsScore, @LiquidityScore, @BreakoutScore, @FuturesScore, @OptionsScore,
              @Reasons::jsonb, @EntryPrice, @InitialStopLoss, @TargetT1, @TargetT2, @TargetT3,
              @AnalysisSignalId, @LiquiditySignalId)
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, row.UserId);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            row.Id,
            row.RunId,
            row.UserId,
            row.InstrumentId,
            row.Side,
            row.AsOfDate,
            row.ConfidenceScore,
            row.Rating,
            row.SignalsScore,
            row.LiquidityScore,
            row.BreakoutScore,
            row.FuturesScore,
            row.OptionsScore,
            Reasons = JsonSerializer.Serialize(row.Reasons),
            row.EntryPrice,
            row.InitialStopLoss,
            row.TargetT1,
            row.TargetT2,
            row.TargetT3,
            row.AnalysisSignalId,
            row.LiquiditySignalId
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TradeConfidenceScoreRow>> GetScoresAsync(
        Guid userId, Guid? runId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.id AS Id,
              s.run_id AS RunId,
              s.user_id AS UserId,
              s.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              s.side::text AS Side,
              s.as_of_date AS AsOfDate,
              s.confidence_score AS ConfidenceScore,
              s.rating AS Rating,
              s.signals_score AS SignalsScore,
              s.liquidity_score AS LiquidityScore,
              s.breakout_score AS BreakoutScore,
              s.futures_score AS FuturesScore,
              s.options_score AS OptionsScore,
              s.reasons AS Reasons,
              s.entry_price AS EntryPrice,
              s.initial_stop_loss AS InitialStopLoss,
              s.target_t1 AS TargetT1,
              s.target_t2 AS TargetT2,
              s.target_t3 AS TargetT3,
              s.analysis_signal_id AS AnalysisSignalId,
              s.liquidity_signal_id AS LiquiditySignalId,
              COALESCE(b.confirmed, false) AS BreakoutConfirmed,
              b.adx AS BreakoutAdx,
              b.rsi AS BreakoutRsi
            FROM trade_confidence_scores s
            JOIN instruments i ON i.id = s.instrument_id
            LEFT JOIN analysis_breakout b
              ON b.run_id = s.run_id AND b.instrument_id = s.instrument_id AND b.side = s.side
            WHERE s.user_id = @userId
              AND (
                (@runId IS NOT NULL AND s.run_id = @runId)
                OR (
                  @runId IS NULL
                  AND s.run_id = (
                    SELECT r.id FROM trade_confidence_runs r
                    WHERE r.user_id = @userId AND r.status = 'succeeded'
                    ORDER BY r.started_at DESC LIMIT 1
                  )
                )
              )
            ORDER BY s.confidence_score DESC, i.symbol
            LIMIT 500
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var rows = await conn.QueryAsync<TradeConfidenceScoreRowDto>(new CommandDefinition(
            sql, new { userId, runId }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }

    public async Task<TradeConfidenceScoreRow?> GetScoreAsync(Guid scoreId, Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              s.id AS Id,
              s.run_id AS RunId,
              s.user_id AS UserId,
              s.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              s.side::text AS Side,
              s.as_of_date AS AsOfDate,
              s.confidence_score AS ConfidenceScore,
              s.rating AS Rating,
              s.signals_score AS SignalsScore,
              s.liquidity_score AS LiquidityScore,
              s.breakout_score AS BreakoutScore,
              s.futures_score AS FuturesScore,
              s.options_score AS OptionsScore,
              s.reasons AS Reasons,
              s.entry_price AS EntryPrice,
              s.initial_stop_loss AS InitialStopLoss,
              s.target_t1 AS TargetT1,
              s.target_t2 AS TargetT2,
              s.target_t3 AS TargetT3,
              s.analysis_signal_id AS AnalysisSignalId,
              s.liquidity_signal_id AS LiquiditySignalId,
              false AS BreakoutConfirmed,
              NULL::numeric AS BreakoutAdx,
              NULL::numeric AS BreakoutRsi
            FROM trade_confidence_scores s
            JOIN instruments i ON i.id = s.instrument_id
            WHERE s.id = @scoreId AND s.user_id = @userId
            """;
        using var conn = _db.CreateConnection();
        await SetUserAsync(conn, userId);
        var dto = await conn.QuerySingleOrDefaultAsync<TradeConfidenceScoreRowDto>(new CommandDefinition(
            sql, new { scoreId, userId }, cancellationToken: ct));
        return dto is null ? null : Map(dto);
    }

    private static TradeConfidenceScoreRow Map(TradeConfidenceScoreRowDto dto) => new()
    {
        Id = dto.Id,
        RunId = dto.RunId,
        UserId = dto.UserId,
        InstrumentId = dto.InstrumentId,
        AppSymbol = dto.AppSymbol,
        InstrumentName = dto.InstrumentName,
        Side = dto.Side,
        AsOfDate = dto.AsOfDate,
        ConfidenceScore = dto.ConfidenceScore,
        Rating = dto.Rating,
        SignalsScore = dto.SignalsScore,
        LiquidityScore = dto.LiquidityScore,
        BreakoutScore = dto.BreakoutScore,
        FuturesScore = dto.FuturesScore,
        OptionsScore = dto.OptionsScore,
        Reasons = ParseReasons(dto.Reasons),
        EntryPrice = dto.EntryPrice,
        InitialStopLoss = dto.InitialStopLoss,
        TargetT1 = dto.TargetT1,
        TargetT2 = dto.TargetT2,
        TargetT3 = dto.TargetT3,
        AnalysisSignalId = dto.AnalysisSignalId,
        LiquiditySignalId = dto.LiquiditySignalId,
        BreakoutConfirmed = dto.BreakoutConfirmed,
        BreakoutAdx = dto.BreakoutAdx,
        BreakoutRsi = dto.BreakoutRsi,
    };

    private static string[] ParseReasons(object? json)
    {
        if (json is null) return Array.Empty<string>();
        try
        {
            var s = json.ToString();
            return string.IsNullOrWhiteSpace(s)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(s) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private sealed class TradeConfidenceScoreRowDto
    {
        public Guid Id { get; set; }
        public Guid RunId { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public string AppSymbol { get; set; } = "";
        public string InstrumentName { get; set; } = "";
        public string Side { get; set; } = "";
        public DateOnly AsOfDate { get; set; }
        public int ConfidenceScore { get; set; }
        public string Rating { get; set; } = "";
        public int SignalsScore { get; set; }
        public int LiquidityScore { get; set; }
        public int BreakoutScore { get; set; }
        public int FuturesScore { get; set; }
        public int OptionsScore { get; set; }
        public object? Reasons { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal InitialStopLoss { get; set; }
        public decimal? TargetT1 { get; set; }
        public decimal? TargetT2 { get; set; }
        public decimal? TargetT3 { get; set; }
        public Guid? AnalysisSignalId { get; set; }
        public Guid? LiquiditySignalId { get; set; }
        public bool BreakoutConfirmed { get; set; }
        public decimal? BreakoutAdx { get; set; }
        public decimal? BreakoutRsi { get; set; }
    }

    private static async Task SetUserAsync(System.Data.IDbConnection conn, Guid userId)
    {
        await conn.ExecuteAsync(
            "SELECT set_config('app.current_user_id', @id, true)", new { id = userId.ToString() });
    }
}
