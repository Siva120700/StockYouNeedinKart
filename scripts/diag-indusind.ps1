# Quick DB diagnostic for INDUSINDBK signal pipeline
$connStr = "Host=localhost;Port=5432;Database=stockyouneed;Username=postgres;Password=siva1207"

Add-Type -Path "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.23\System.Data.Common.dll" -ErrorAction SilentlyContinue

$query = @"
SELECT 'instrument' AS step, i.id::text, i.symbol, i.name, NULL::text AS detail
FROM instruments i WHERE i.symbol = 'INDUSINDBK'
UNION ALL
SELECT 'universe', u.universe::text, i.symbol, NULL, u.valid_to::text
FROM universe_memberships u JOIN instruments i ON i.id = u.instrument_id
WHERE i.symbol = 'INDUSINDBK' AND u.valid_to IS NULL
UNION ALL
SELECT 'angel_token', m.symbol_token, m.trading_symbol, m.exchange::text, m.is_active::text
FROM angel_instrument_map m JOIN instruments i ON i.id = m.instrument_id
WHERE i.symbol = 'INDUSINDBK'
ORDER BY step;
"@

# Use dotnet with inline Npgsql
$code = @'
using Npgsql;
var cs = args[0];
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

async Task Query(string label, string sql) {
    Console.WriteLine($"\n=== {label} ===");
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var r = await cmd.ExecuteReaderAsync();
    var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", cols));
    var n = 0;
    while (await r.ReadAsync()) {
        var vals = Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : r.GetValue(i)?.ToString()).ToArray();
        Console.WriteLine(string.Join(" | ", vals));
        n++;
    }
    if (n == 0) Console.WriteLine("(no rows)");
}

await Query("Instrument", "SELECT id, symbol, name, kind, sector_instrument_id FROM instruments WHERE symbol = 'INDUSINDBK'");
await Query("Universe", @"SELECT u.universe, u.valid_from, u.valid_to FROM universe_memberships u JOIN instruments i ON i.id = u.instrument_id WHERE i.symbol = 'INDUSINDBK'");
await Query("Angel token", @"SELECT exchange, symbol_token, trading_symbol, is_active, updated_at FROM angel_instrument_map m JOIN instruments i ON i.id = m.instrument_id WHERE i.symbol = 'INDUSINDBK'");
await Query("Market bars (last 10)", @"SELECT b.trade_date, b.open, b.high, b.low, b.close, b.volume, b.ingested_at FROM market_bars b JOIN instruments i ON i.id = b.instrument_id WHERE i.symbol = 'INDUSINDBK' ORDER BY b.trade_date DESC LIMIT 10");
await Query("Market OHLC", @"SELECT o.ltp, o.open, o.high, o.low, o.close, o.trade_volume, o.fetched_at FROM market_ohlc o JOIN instruments i ON i.id = o.instrument_id WHERE i.symbol = 'INDUSINDBK'");
await Query("Signals", @"SELECT s.side, s.as_of_date, s.entry_price, s.volume_ok, s.sector_confirmed, s.last_2d_high, s.last_2d_low, s.created_at FROM analysis_signals s JOIN instruments i ON i.id = s.instrument_id WHERE i.symbol = 'INDUSINDBK' ORDER BY s.created_at DESC LIMIT 5");
await Query("Latest analysis runs", @"SELECT id, status, as_of_date, started_at, finished_at, stats_json FROM analysis_runs ORDER BY started_at DESC LIMIT 3");

// Manual strategy eval on bars
await using (var cmd = new NpgsqlCommand(@"SELECT b.trade_date, b.high, b.low, b.close, b.volume FROM market_bars b JOIN instruments i ON i.id = b.instrument_id WHERE i.symbol = 'INDUSINDBK' ORDER BY b.trade_date DESC LIMIT 10", conn))
await using (var r = await cmd.ExecuteReaderAsync()) {
    var bars = new List<(DateOnly d, decimal h, decimal l, decimal c, long v)>();
    while (await r.ReadAsync())
        bars.Add((DateOnly.FromDateTime(r.GetDateTime(0)), r.GetDecimal(1), r.GetDecimal(2), r.GetDecimal(3), r.GetInt64(4)));
    if (bars.Count >= 3) {
        var latest = bars[0];
        var prev = bars.Skip(1).Take(2).ToList();
        var last2High = prev.Max(b => b.h);
        var last2Low = prev.Min(b => b.l);
        var avgVol5 = bars.Take(5).Average(b => (double)b.v);
        var volumeOk = latest.v >= (long)(avgVol5 * 1.0);
        var buy = latest.c > last2High && volumeOk;
        var sell = latest.c < last2Low && volumeOk;
        Console.WriteLine($"\n=== Strategy eval on DB bars ===");
        Console.WriteLine($"Latest bar date: {latest.d}, close={latest.c}, high={latest.h}, vol={latest.v}");
        Console.WriteLine($"Last2High={last2High}, Last2Low={last2Low}, AvgVol5={avgVol5:F0}, VolumeOk={volumeOk}");
        Console.WriteLine($"BUY signal? {buy} (close > last2High: {latest.c > last2High})");
        Console.WriteLine($"SELL signal? {sell}");
        Console.WriteLine($"Bar count: {bars.Count} (need >= 5 for analysis loop)");
    } else {
        Console.WriteLine($"\n=== Strategy eval: only {bars.Count} bars — SKIPPED by analysis (needs >= 5) ===");
    }
}
'@

$tmpDir = Join-Path $env:TEMP "syn-diag"
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
Set-Content -Path (Join-Path $tmpDir "Program.cs") -Value $code
Set-Content -Path (Join-Path $tmpDir "Diag.csproj") -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="Npgsql" Version="8.0.5" /></ItemGroup>
</Project>
'@
dotnet run --project (Join-Path $tmpDir "Diag.csproj") -- $connStr
