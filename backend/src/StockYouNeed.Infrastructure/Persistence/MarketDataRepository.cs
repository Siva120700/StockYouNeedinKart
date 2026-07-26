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
