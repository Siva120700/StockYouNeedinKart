using System.Data;
using Dapper;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Domain;

namespace StockYouNeed.Infrastructure.Persistence;

public sealed class InstrumentRepository : IInstrumentRepository
{
    private readonly IDbConnectionFactory _db;

    public InstrumentRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<Instrument>> GetUniverseEquitiesAsync(CancellationToken ct = default)
    {
        var sql = $"""
            SELECT DISTINCT i.id AS Id, i.kind AS Kind, i.symbol AS Symbol, i.name AS Name,
                   i.exchange AS Exchange, i.is_active AS IsActive
            FROM instruments i
            JOIN universe_memberships u ON u.instrument_id = i.id
            WHERE i.is_active
              AND i.kind = 'equity'
              AND u.valid_to IS NULL
              AND u.universe IN ({UniverseCodes.SqlEquityScanIn})
            ORDER BY i.symbol
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<Instrument>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Instrument?> GetEquityByIdAsync(Guid instrumentId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT i.id AS Id, i.kind AS Kind, i.symbol AS Symbol, i.name AS Name,
                   i.exchange AS Exchange, i.is_active AS IsActive
            FROM instruments i
            WHERE i.id = @instrumentId AND i.kind = 'equity' AND i.is_active
            """;
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Instrument>(
            new CommandDefinition(sql, new { instrumentId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AngelTokenRow>> GetActiveTokensForUniversesAsync(CancellationToken ct = default)
    {
        var sql = $"""
            SELECT DISTINCT
              m.instrument_id AS InstrumentId,
              m.exchange::text AS Exchange,
              m.symbol_token AS SymbolToken,
              m.trading_symbol AS TradingSymbol,
              m.name AS Name,
              i.symbol AS AppSymbol
            FROM angel_instrument_map m
            JOIN instruments i ON i.id = m.instrument_id
            JOIN universe_memberships u ON u.instrument_id = m.instrument_id
            WHERE m.is_active
              AND u.valid_to IS NULL
              AND u.universe IN ({UniverseCodes.SqlEquityScanIn})
            ORDER BY i.symbol
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<AngelTokenRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task UpsertAngelTokenAsync(AngelTokenRow row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO angel_instrument_map (
              instrument_id, exchange, symbol_token, trading_symbol, name, is_active, updated_at)
            VALUES (
              @InstrumentId, @Exchange::angel_exchange, @SymbolToken, @TradingSymbol, @Name, true, now())
            ON CONFLICT (instrument_id) DO UPDATE SET
              exchange = EXCLUDED.exchange,
              symbol_token = EXCLUDED.symbol_token,
              trading_symbol = EXCLUDED.trading_symbol,
              name = EXCLUDED.name,
              is_active = true,
              updated_at = now()
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    public async Task EnsureDemoUserAsync(Guid userId, string email, string displayName, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO users (id, email, display_name)
            VALUES (@userId, @email, @displayName)
            ON CONFLICT (email) DO UPDATE SET display_name = EXCLUDED.display_name
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { userId, email, displayName }, cancellationToken: ct));

        // Ensure fixed id if email existed with different id
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO users (id, email, display_name)
            VALUES (@userId, @email, @displayName)
            ON CONFLICT (id) DO NOTHING
            """,
            new { userId, email, displayName },
            cancellationToken: ct));
    }

    public async Task SeedInstrumentIfMissingAsync(string symbol, string name, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO instruments (kind, symbol, name, exchange)
            VALUES ('equity', @symbol, @name, 'NSE')
            ON CONFLICT (exchange, symbol, kind) DO UPDATE SET name = EXCLUDED.name, is_active = true
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { symbol, name }, cancellationToken: ct));
    }

    public async Task EnsureUniverseMembershipAsync(string universe, string symbol, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO universe_memberships (universe, instrument_id, valid_from, valid_to)
            SELECT @universe::universe_code, i.id, CURRENT_DATE, NULL
            FROM instruments i
            WHERE i.symbol = @symbol AND i.exchange = 'NSE' AND i.kind = 'equity'
            AND NOT EXISTS (
              SELECT 1 FROM universe_memberships u
              WHERE u.universe = @universe::universe_code
                AND u.instrument_id = i.id
                AND u.valid_to IS NULL
            )
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { universe, symbol }, cancellationToken: ct));
    }

    public async Task RetireEquitySymbolsAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        if (symbols.Count == 0)
            return;

        const string sql = """
            UPDATE instruments
            SET is_active = false, updated_at = now()
            WHERE kind = 'equity'
              AND exchange = 'NSE'
              AND symbol = ANY(@symbols);

            UPDATE universe_memberships um
            SET valid_to = CURRENT_DATE
            WHERE um.valid_to IS NULL
              AND um.instrument_id IN (
                SELECT i.id FROM instruments i
                WHERE i.kind = 'equity' AND i.exchange = 'NSE' AND i.symbol = ANY(@symbols)
              );
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { symbols = symbols.ToArray() }, cancellationToken: ct));
    }

    public async Task RetireEquitySymbolsLikeAsync(string symbolPattern, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbolPattern))
            return;

        const string sql = """
            UPDATE instruments
            SET is_active = false, updated_at = now()
            WHERE kind = 'equity'
              AND exchange = 'NSE'
              AND symbol ILIKE @symbolPattern;

            UPDATE universe_memberships um
            SET valid_to = CURRENT_DATE
            WHERE um.valid_to IS NULL
              AND um.instrument_id IN (
                SELECT i.id FROM instruments i
                WHERE i.kind = 'equity' AND i.exchange = 'NSE' AND i.symbol ILIKE @symbolPattern
              );
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { symbolPattern }, cancellationToken: ct));
    }

    public async Task SeedSectorIndexIfMissingAsync(string symbol, string name, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO instruments (kind, symbol, name, exchange)
            VALUES ('sector_index', @symbol, @name, 'NSE')
            ON CONFLICT (exchange, symbol, kind) DO UPDATE SET name = EXCLUDED.name, is_active = true
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { symbol, name }, cancellationToken: ct));
    }

    public async Task LinkEquityToSectorAsync(string equitySymbol, string sectorSymbol, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE instruments e
            SET sector_instrument_id = s.id, updated_at = now()
            FROM instruments s
            WHERE e.symbol = @equitySymbol AND e.kind = 'equity' AND e.exchange = 'NSE'
              AND s.symbol = @sectorSymbol AND s.kind = 'sector_index' AND s.exchange = 'NSE'
            """;
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { equitySymbol, sectorSymbol }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Instrument>> GetSectorIndexesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, kind AS Kind, symbol AS Symbol, name AS Name,
                   exchange AS Exchange, is_active AS IsActive
            FROM instruments
            WHERE kind = 'sector_index' AND is_active
            ORDER BY symbol
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<Instrument>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetSectorInstrumentIdsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT id FROM instruments WHERE kind = 'sector_index' AND is_active
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<Guid>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid?> GetSectorIdForInstrumentAsync(Guid instrumentId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT sector_instrument_id FROM instruments WHERE id = @instrumentId
            """;
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(sql, new { instrumentId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AngelTokenRow>> GetActiveTokensForSectorsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              m.instrument_id AS InstrumentId,
              m.exchange::text AS Exchange,
              m.symbol_token AS SymbolToken,
              m.trading_symbol AS TradingSymbol,
              m.name AS Name,
              i.symbol AS AppSymbol
            FROM angel_instrument_map m
            JOIN instruments i ON i.id = m.instrument_id
            WHERE m.is_active
              AND i.kind = 'sector_index'
              AND i.is_active
            ORDER BY i.symbol
            """;
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<AngelTokenRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Instrument?> FindBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id AS Id, kind AS Kind, symbol AS Symbol, name AS Name,
                   exchange AS Exchange, is_active AS IsActive
            FROM instruments
            WHERE is_active
              AND exchange = 'NSE'
              AND upper(symbol) = upper(@symbol)
            ORDER BY CASE kind
              WHEN 'index' THEN 0
              WHEN 'sector_index' THEN 1
              ELSE 2
            END
            LIMIT 1
            """;
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Instrument>(
            new CommandDefinition(sql, new { symbol }, cancellationToken: ct));
    }
}
