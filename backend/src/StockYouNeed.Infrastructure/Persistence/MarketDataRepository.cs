using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class MarketDataRepository : IMarketDataRepository
{
    private readonly IDbConnectionFactory _db;

    public MarketDataRepository(IDbConnectionFactory db) => _db = db;

    public async Task UpsertLtpAsync(
        Guid instrumentId, string exchange, string tradingSymbol, string symbolToken,
        decimal ltp, string rawJson, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO market_ltp (
              instrument_id, exchange, trading_symbol, symbol_token, ltp, fetched_at, raw_payload)
            VALUES (
              @instrumentId, @exchange::angel_exchange, @tradingSymbol, @symbolToken, @ltp, now(), @rawJson::jsonb)
            ON CONFLICT (instrument_id) DO UPDATE SET
              exchange = EXCLUDED.exchange,
              trading_symbol = EXCLUDED.trading_symbol,
              symbol_token = EXCLUDED.symbol_token,
              ltp = EXCLUDED.ltp,
              fetched_at = now(),
              raw_payload = EXCLUDED.raw_payload
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            instrumentId, exchange, tradingSymbol, symbolToken, ltp, rawJson
        }, cancellationToken: ct));
    }

    public async Task UpsertOhlcAsync(
        Guid instrumentId, string exchange, string tradingSymbol, string symbolToken,
        decimal ltp, decimal open, decimal high, decimal low, decimal close, long tradeVolume,
        Guid? analysisRunId, string rawJson, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO market_ohlc (
              instrument_id, exchange, trading_symbol, symbol_token,
              ltp, open, high, low, close, trade_volume, fetched_at, analysis_run_id, raw_payload)
            VALUES (
              @instrumentId, @exchange::angel_exchange, @tradingSymbol, @symbolToken,
              @ltp, @open, @high, @low, @close, @tradeVolume, now(), @analysisRunId, @rawJson::jsonb)
            ON CONFLICT (instrument_id) DO UPDATE SET
              exchange = EXCLUDED.exchange,
              trading_symbol = EXCLUDED.trading_symbol,
              symbol_token = EXCLUDED.symbol_token,
              ltp = EXCLUDED.ltp,
              open = EXCLUDED.open,
              high = EXCLUDED.high,
              low = EXCLUDED.low,
              close = EXCLUDED.close,
              trade_volume = EXCLUDED.trade_volume,
              fetched_at = now(),
              analysis_run_id = EXCLUDED.analysis_run_id,
              raw_payload = EXCLUDED.raw_payload
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            instrumentId, exchange, tradingSymbol, symbolToken,
            ltp, open, high, low, close, tradeVolume, analysisRunId, rawJson
        }, cancellationToken: ct));
    }

    public async Task UpsertMarketBarAsync(
        Guid instrumentId, DateOnly tradeDate,
        decimal open, decimal high, decimal low, decimal close, long volume,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO market_bars (
              instrument_id, trade_date, open, high, low, close, volume, source, ingested_at)
            VALUES (
              @instrumentId, @tradeDate, @open, @high, @low, @close, @volume, 'angel', now())
            ON CONFLICT (instrument_id, trade_date) DO UPDATE SET
              open = EXCLUDED.open,
              high = EXCLUDED.high,
              low = EXCLUDED.low,
              close = EXCLUDED.close,
              volume = EXCLUDED.volume,
              source = 'angel',
              ingested_at = now()
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            instrumentId, tradeDate, open, high, low, close, volume
        }, cancellationToken: ct));
    }

    public async Task UpsertIntradayBarAsync(
        Guid instrumentId, string interval, DateTimeOffset barTime,
        decimal open, decimal high, decimal low, decimal close, long volume,
        CancellationToken ct = default)
    {
        // Normalize OHLC so Angel quirks never violate the check constraint.
        var hi = Math.Max(high, Math.Max(open, close));
        var lo = Math.Min(low, Math.Min(open, close));
        if (hi < lo) (hi, lo) = (lo, hi);

        // Npgsql timestamptz only accepts UTC (Offset=0).
        var barTimeUtc = barTime.ToUniversalTime();

        const string sql = """
            INSERT INTO market_intraday_bars (
              instrument_id, interval, bar_time, open, high, low, close, volume, source, ingested_at)
            VALUES (
              @instrumentId, @interval, @barTimeUtc, @open, @hi, @lo, @close, @volume, 'angel', now())
            ON CONFLICT (instrument_id, interval, bar_time) DO UPDATE SET
              open = EXCLUDED.open,
              high = EXCLUDED.high,
              low = EXCLUDED.low,
              close = EXCLUDED.close,
              volume = EXCLUDED.volume,
              source = 'angel',
              ingested_at = now()
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            instrumentId, interval, barTimeUtc, open, hi, lo, close, volume
        }, cancellationToken: ct));
    }

    public async Task TrimMarketBarsOlderThanAsync(int keepTradingDaysApprox, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM market_bars
            WHERE trade_date < CURRENT_DATE - (@keepTradingDaysApprox || ' days')::interval
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { keepTradingDaysApprox }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MarketLtpRow>> GetAllLtpAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              l.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              l.exchange::text AS Exchange,
              l.trading_symbol AS TradingSymbol,
              l.symbol_token AS SymbolToken,
              l.ltp AS Ltp,
              l.fetched_at AS FetchedAt
            FROM market_ltp l
            JOIN instruments i ON i.id = l.instrument_id
            ORDER BY i.symbol
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MarketLtpRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<MarketLtpRow>> GetUniverseLtpAsync(CancellationToken ct = default)
    {
        var sql = $"""
            SELECT DISTINCT
              i.id AS InstrumentId,
              i.symbol AS AppSymbol,
              i.name AS InstrumentName,
              COALESCE(l.exchange::text, 'NSE') AS Exchange,
              COALESCE(l.trading_symbol, '') AS TradingSymbol,
              COALESCE(l.symbol_token, '') AS SymbolToken,
              COALESCE(l.ltp, 0) AS Ltp,
              l.fetched_at AS FetchedAt
            FROM instruments i
            JOIN universe_memberships u ON u.instrument_id = i.id
            LEFT JOIN market_ltp l ON l.instrument_id = i.id
            WHERE i.is_active
              AND i.kind = 'equity'
              AND u.valid_to IS NULL
              AND u.universe IN ({UniverseCodes.SqlEquityScanIn})
            ORDER BY i.symbol
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MarketLtpRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<SectorScopeQuoteRow>> GetSectorScopeQuotesAsync(CancellationToken ct = default)
    {
        var sql = $"""
            WITH ranked AS (
              SELECT
                b.instrument_id,
                b.trade_date,
                b.open,
                b.close,
                ROW_NUMBER() OVER (PARTITION BY b.instrument_id ORDER BY b.trade_date DESC) AS rn
              FROM market_bars b
            ),
            ist_today AS (
              SELECT (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Kolkata')::date AS d
            )
            SELECT
              e.id AS InstrumentId,
              e.symbol AS Symbol,
              e.name AS Name,
              e.kind AS Kind,
              COALESCE(s.id, '00000000-0000-0000-0000-000000000001'::uuid) AS SectorId,
              COALESCE(s.symbol, 'UNLINKED') AS SectorSymbol,
              COALESCE(s.name, 'Other F&O') AS SectorName,
              COALESCE(NULLIF(l.ltp, 0), NULLIF(o.ltp, 0), last.close) AS Ltp,
              COALESCE(
                CASE
                  WHEN last.trade_date IS NOT NULL
                       AND last.trade_date < (SELECT d FROM ist_today)
                  THEN last.close
                  ELSE prev.close
                END,
                CASE
                  WHEN o.close IS NOT NULL AND o.close > 0
                       AND (l.ltp IS NULL OR l.ltp = 0 OR o.close <> l.ltp)
                  THEN o.close
                END,
                last.open
              ) AS PrevClose
            FROM instruments e
            LEFT JOIN instruments s ON s.id = e.sector_instrument_id AND s.kind = 'sector_index' AND s.is_active
            LEFT JOIN market_ltp l ON l.instrument_id = e.id
            LEFT JOIN market_ohlc o ON o.instrument_id = e.id
            LEFT JOIN ranked last ON last.instrument_id = e.id AND last.rn = 1
            LEFT JOIN ranked prev ON prev.instrument_id = e.id AND prev.rn = 2
            WHERE e.kind = 'equity' AND e.is_active
              AND EXISTS (
                SELECT 1
                FROM universe_memberships u
                WHERE u.instrument_id = e.id
                  AND u.valid_to IS NULL
                  AND u.universe IN ({UniverseCodes.SqlEquityScanIn})
              )

            UNION ALL

            SELECT
              s.id AS InstrumentId,
              s.symbol AS Symbol,
              s.name AS Name,
              s.kind AS Kind,
              s.id AS SectorId,
              s.symbol AS SectorSymbol,
              s.name AS SectorName,
              COALESCE(NULLIF(l.ltp, 0), NULLIF(o.ltp, 0), last.close) AS Ltp,
              COALESCE(
                CASE
                  WHEN last.trade_date IS NOT NULL
                       AND last.trade_date < (SELECT d FROM ist_today)
                  THEN last.close
                  ELSE prev.close
                END,
                CASE
                  WHEN o.close IS NOT NULL AND o.close > 0
                       AND (l.ltp IS NULL OR l.ltp = 0 OR o.close <> l.ltp)
                  THEN o.close
                END,
                last.open
              ) AS PrevClose
            FROM instruments s
            LEFT JOIN market_ltp l ON l.instrument_id = s.id
            LEFT JOIN market_ohlc o ON o.instrument_id = s.id
            LEFT JOIN ranked last ON last.instrument_id = s.id AND last.rn = 1
            LEFT JOIN ranked prev ON prev.instrument_id = s.id AND prev.rn = 2
            WHERE s.kind = 'sector_index' AND s.is_active
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<SectorScopeQuoteRow>(
            new CommandDefinition(sql, cancellationToken: ct));
        // Equities can sit in nifty_50 + nifty_100 + nifty_fno; never emit the same name twice.
        return rows
            .GroupBy(r => (r.Kind, r.InstrumentId, r.SectorId))
            .Select(g => g.First())
            .ToList();
    }

    public async Task<IReadOnlyList<MarketBarRow>> GetBarsAsync(Guid? instrumentId, int limitDays, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              b.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              b.trade_date AS TradeDate,
              b.open AS Open,
              b.high AS High,
              b.low AS Low,
              b.close AS Close,
              b.volume AS Volume,
              b.source AS Source
            FROM market_bars b
            JOIN instruments i ON i.id = b.instrument_id
            WHERE (@instrumentId IS NULL OR b.instrument_id = @instrumentId)
              AND b.trade_date >= CURRENT_DATE - (@limitDays || ' days')::interval
            ORDER BY i.symbol, b.trade_date DESC
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MarketBarRow>(new CommandDefinition(sql, new { instrumentId, limitDays }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<MarketBarRow>> GetBarsForInstrumentAsync(Guid instrumentId, int limitDays, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              b.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              b.trade_date AS TradeDate,
              b.open AS Open,
              b.high AS High,
              b.low AS Low,
              b.close AS Close,
              b.volume AS Volume,
              b.source AS Source
            FROM market_bars b
            JOIN instruments i ON i.id = b.instrument_id
            WHERE b.instrument_id = @instrumentId
            ORDER BY b.trade_date DESC
            LIMIT @limitDays
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MarketBarRow>(new CommandDefinition(sql, new { instrumentId, limitDays }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<MarketIntradayBarRow>> GetIntradayBarsForInstrumentAsync(
        Guid instrumentId, string interval, int limitBars, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              b.instrument_id AS InstrumentId,
              i.symbol AS AppSymbol,
              b.interval AS Interval,
              b.bar_time AS BarTime,
              b.open AS Open,
              b.high AS High,
              b.low AS Low,
              b.close AS Close,
              b.volume AS Volume
            FROM market_intraday_bars b
            JOIN instruments i ON i.id = b.instrument_id
            WHERE b.instrument_id = @instrumentId
              AND b.interval = @interval
            ORDER BY b.bar_time DESC
            LIMIT @limitBars
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MarketIntradayBarRow>(
            new CommandDefinition(sql, new { instrumentId, interval, limitBars }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<MarketIntradayBarRow>> GetIntradayBarsForUniverseAsync(
        string interval, int limitBarsPerInstrument, CancellationToken ct = default)
    {
        var sql = $"""
            WITH ranked AS (
              SELECT
                b.instrument_id AS InstrumentId,
                i.symbol AS AppSymbol,
                b.interval AS Interval,
                b.bar_time AS BarTime,
                b.open AS Open,
                b.high AS High,
                b.low AS Low,
                b.close AS Close,
                b.volume AS Volume,
                ROW_NUMBER() OVER (PARTITION BY b.instrument_id ORDER BY b.bar_time DESC) AS rn
              FROM market_intraday_bars b
              JOIN instruments i ON i.id = b.instrument_id
              WHERE b.interval = @interval
                AND i.kind = 'equity' AND i.is_active
                AND EXISTS (
                  SELECT 1
                  FROM universe_memberships u
                  WHERE u.instrument_id = i.id
                    AND u.valid_to IS NULL
                    AND u.universe IN ({UniverseCodes.SqlEquityScanIn})
                )
            )
            SELECT InstrumentId, AppSymbol, Interval, BarTime, Open, High, Low, Close, Volume
            FROM ranked
            WHERE rn <= @limitBarsPerInstrument
            ORDER BY InstrumentId, BarTime DESC
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<MarketIntradayBarRow>(
            new CommandDefinition(sql, new { interval, limitBarsPerInstrument }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CountIntradayBarsAsync(Guid instrumentId, string interval, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(*)::int FROM market_intraday_bars
            WHERE instrument_id = @instrumentId AND interval = @interval
            """;
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { instrumentId, interval }, cancellationToken: ct));
    }

    public async Task<DateTimeOffset?> GetLatestIntradayBarTimeAsync(
        Guid instrumentId, string interval, CancellationToken ct = default)
    {
        const string sql = """
            SELECT bar_time FROM market_intraday_bars
            WHERE instrument_id = @instrumentId AND interval = @interval
            ORDER BY bar_time DESC
            LIMIT 1
            """;
        using var conn = _db.CreateConnection();
        // Npgsql often returns timestamptz as DateTime via ExecuteScalar — not DateTimeOffset.
        var raw = await conn.ExecuteScalarAsync<object>(
            new CommandDefinition(sql, new { instrumentId, interval }, cancellationToken: ct));
        if (raw is null or DBNull)
            return null;
        return raw switch
        {
            DateTimeOffset dto => dto,
            DateTime dt when dt.Kind == DateTimeKind.Utc => new DateTimeOffset(dt),
            DateTime dt when dt.Kind == DateTimeKind.Local => new DateTimeOffset(dt),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"Unexpected bar_time type: {raw.GetType().FullName}")
        };
    }

    public async Task LogQuoteFetchBatchAsync(
        string mode, int requested, int fetched, int unfetched, bool statusOk,
        string? message, string? errorCode, string exchangeTokensJson, string unfetchedJson,
        Guid? analysisRunId, int? durationMs, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO market_quote_fetch_batches (
              mode, requested_count, fetched_count, unfetched_count, status_ok,
              message, error_code, exchange_tokens, unfetched, finished_at, duration_ms, analysis_run_id)
            VALUES (
              @mode::market_quote_mode, @requested, @fetched, @unfetched, @statusOk,
              @message, @errorCode, @exchangeTokensJson::jsonb, @unfetchedJson::jsonb,
              now(), @durationMs, @analysisRunId)
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            mode, requested, fetched, unfetched, statusOk,
            message, errorCode, exchangeTokensJson, unfetchedJson, durationMs, analysisRunId
        }, cancellationToken: ct));
    }
}
