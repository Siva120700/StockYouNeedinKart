using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StockYouNeed.Application.Options;

namespace StockYouNeed.Infrastructure.Persistence;

/// <summary>
/// Applies database/00x_*.sql once (idempotent where possible).
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly DatabaseOptions _db;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IOptions<DatabaseOptions> db, ILogger<DatabaseMigrator> logger)
    {
        _db = db.Value;
        _logger = logger;
    }

    public async Task MigrateAsync(string databaseRoot, CancellationToken ct = default)
    {
        var files = new[]
        {
            Path.Combine(databaseRoot, "001_init.sql"),
            Path.Combine(databaseRoot, "002_angel_market_data.sql"),
            Path.Combine(databaseRoot, "003_targets_pct_windows.sql"),
            Path.Combine(databaseRoot, "004_fresh_cross.sql"),
            Path.Combine(databaseRoot, "005_liquidity_signals.sql"),
            Path.Combine(databaseRoot, "006_liquidity_sector_confirmed.sql"),
            Path.Combine(databaseRoot, "007_backtest_notes.sql"),
            Path.Combine(databaseRoot, "008_backtest_source.sql"),
            Path.Combine(databaseRoot, "009_backtest_auto_notes.sql"),
            Path.Combine(databaseRoot, "010_liquidity_ruleset.sql"),
            Path.Combine(databaseRoot, "011_backtest_liquidity_fresh.sql"),
            Path.Combine(databaseRoot, "012_backtest_confluence.sql"),
            Path.Combine(databaseRoot, "013_trade_confidence.sql"),
            Path.Combine(databaseRoot, "014_breakout_analysis.sql"),
            Path.Combine(databaseRoot, "015_breakout_pattern_type.sql"),
            Path.Combine(databaseRoot, "016_signal_outcomes.sql"),
            Path.Combine(databaseRoot, "017_options_intraday.sql"),
            Path.Combine(databaseRoot, "018_sector_confirmed_filter.sql"),
        };

        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
              filename text PRIMARY KEY,
              applied_at timestamptz NOT NULL DEFAULT now()
            )
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                _logger.LogWarning("Migration file missing: {File}", file);
                continue;
            }

            var name = Path.GetFileName(file);
            await using (var check = new NpgsqlCommand(
                             "SELECT 1 FROM schema_migrations WHERE filename = @n", conn))
            {
                check.Parameters.AddWithValue("n", name);
                var exists = await check.ExecuteScalarAsync(ct);
                if (exists is not null)
                {
                    _logger.LogInformation("Skip already applied {File}", name);
                    continue;
                }
            }

            var sql = await File.ReadAllTextAsync(file, ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var apply = new NpgsqlCommand(sql, conn, tx))
                {
                    apply.CommandTimeout = 120;
                    await apply.ExecuteNonQueryAsync(ct);
                }

                await using (var mark = new NpgsqlCommand(
                                 "INSERT INTO schema_migrations (filename) VALUES (@n)", conn, tx))
                {
                    mark.Parameters.AddWithValue("n", name);
                    await mark.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                _logger.LogInformation("Applied migration {File}", name);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }
}
