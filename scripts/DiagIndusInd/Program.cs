using System.Text.Json;
using Npgsql;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var settingsPath = Path.Combine(root, "backend", "src", "StockYouNeed.Api", "appsettings.json");
var localPath = Path.Combine(root, "backend", "src", "StockYouNeed.Api", "appsettings.Development.local.json");
var json = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
var cs = json.RootElement.GetProperty("Database").GetProperty("ConnectionString").GetString()!;
if (File.Exists(localPath))
{
    var local = JsonDocument.Parse(await File.ReadAllTextAsync(localPath));
    if (local.RootElement.TryGetProperty("Database", out var db) &&
        db.TryGetProperty("ConnectionString", out var csProp) && csProp.GetString() is { } s)
        cs = s;
}

await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

async Task Q(string label, string sql)
{
    Console.WriteLine($"\n=== {label} ===");
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var r = await cmd.ExecuteReaderAsync();
    Console.WriteLine(string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(r.GetName)));
    var n = 0;
    while (await r.ReadAsync())
    {
        Console.WriteLine(string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : $"{r.GetValue(i)}")));
        n++;
    }
    if (n == 0) Console.WriteLine("(no rows)");
}

await Q("Overview", @"
SELECT
  (SELECT COUNT(*) FROM instruments WHERE kind='equity' AND is_active) AS equities,
  (SELECT COUNT(*) FROM angel_instrument_map WHERE is_active) AS tokens,
  (SELECT COUNT(*) FROM market_bars) AS bars,
  (SELECT COUNT(DISTINCT instrument_id) FROM market_bars) AS instruments_with_bars,
  (SELECT COUNT(*) FROM market_ohlc) AS ohlc_rows,
  (SELECT COUNT(*) FROM analysis_runs) AS runs,
  (SELECT COUNT(*) FROM analysis_signals) AS signals
");

await Q("Latest runs", @"SELECT id, status, as_of_date, started_at, finished_at FROM analysis_runs ORDER BY started_at DESC LIMIT 5");
await Q("Signal counts by run", @"
SELECT r.id, r.started_at, r.status, COUNT(s.id) AS signal_count
FROM analysis_runs r
LEFT JOIN analysis_signals s ON s.analysis_run_id = r.id
GROUP BY r.id, r.started_at, r.status
ORDER BY r.started_at DESC LIMIT 5");

await Q("INDUSINDBK bars", @"
SELECT b.trade_date, b.open, b.high, b.low, b.close, b.volume
FROM market_bars b JOIN instruments i ON i.id=b.instrument_id
WHERE i.symbol='INDUSINDBK' ORDER BY b.trade_date DESC LIMIT 10");

await Q("INDUSINDBK ohlc/ltp", @"
SELECT o.ltp, o.open, o.high, o.low, o.close, o.trade_volume, o.fetched_at
FROM market_ohlc o JOIN instruments i ON i.id=o.instrument_id WHERE i.symbol='INDUSINDBK'");

await Q("All signals", @"
SELECT i.symbol, s.side, s.entry_price, s.last_2d_high, s.last_2d_low, s.volume_ok, s.as_of_date, s.created_at
FROM analysis_signals s JOIN instruments i ON i.id=s.instrument_id
ORDER BY s.created_at DESC LIMIT 30");

// Evaluate every equity with bars using CURRENT buggy logic vs FIXED logic
Console.WriteLine("\n=== Screen all equities (current vs fixed) ===");
await using (var cmd = new NpgsqlCommand(@"
SELECT i.symbol, b.trade_date, b.high, b.low, b.close, b.volume,
       o.ltp, o.high AS sess_high, o.low AS sess_low, o.trade_volume AS sess_vol, o.close AS quote_close
FROM instruments i
JOIN market_bars b ON b.instrument_id = i.id
LEFT JOIN market_ohlc o ON o.instrument_id = i.id
WHERE i.kind='equity' AND i.is_active
ORDER BY i.symbol, b.trade_date DESC
", conn))
await using (var r = await cmd.ExecuteReaderAsync())
{
    var bySym = new Dictionary<string, List<(DateOnly d, decimal h, decimal l, decimal c, long v, decimal? ltp, decimal? sh, decimal? sl, long? sv, decimal? qc)>>();
    while (await r.ReadAsync())
    {
        var sym = r.GetString(0);
        if (!bySym.TryGetValue(sym, out var list)) bySym[sym] = list = new();
        list.Add((
            DateOnly.FromDateTime(r.GetDateTime(1)),
            r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4), r.GetInt64(5),
            r.IsDBNull(6) ? null : r.GetDecimal(6),
            r.IsDBNull(7) ? null : r.GetDecimal(7),
            r.IsDBNull(8) ? null : r.GetDecimal(8),
            r.IsDBNull(9) ? null : r.GetInt64(9),
            r.IsDBNull(10) ? null : r.GetDecimal(10)));
    }

    Console.WriteLine($"Symbols with any bars: {bySym.Count}");
    var currentBuys = new List<string>();
    var fixedBuys = new List<string>();
    var skipped = 0;
    foreach (var (sym, bars) in bySym.OrderBy(x => x.Key))
    {
        // bars already desc by date from query order within symbol... actually order is symbol, date desc so yes
        if (bars.Count < 5) { skipped++; continue; }
        var latest = bars[0];
        var prev = bars.Skip(1).Take(2).ToList();
        if (prev.Count < 2) { skipped++; continue; }
        var last2High = prev.Max(b => b.h);
        var last2Low = prev.Min(b => b.l);

        // CURRENT logic
        var avgVol5 = bars.Take(5).Average(b => (double)b.v);
        var volOkCurrent = latest.v >= (long)(avgVol5 * 1.0);
        var priceCurrent = latest.ltp ?? latest.c;
        var buyCurrent = priceCurrent > last2High && volOkCurrent;

        // FIXED: vol vs prior 3 days; breakout uses session high; close for today uses LTP
        var prior3 = bars.Skip(1).Take(3).ToList();
        var avgVol3 = prior3.Count > 0 ? prior3.Average(b => (double)b.v) : 0;
        var sessVol = latest.sv ?? latest.v;
        var volOkFixed = sessVol >= (long)avgVol3;
        var sessHigh = latest.sh ?? latest.h;
        var sessLow = latest.sl ?? latest.l;
        var buyFixed = sessHigh > last2High && volOkFixed;
        var sellFixed = sessLow < last2Low && volOkFixed;

        if (buyCurrent) currentBuys.Add($"{sym} ltp={priceCurrent} > {last2High} vol={latest.v}/{avgVol5:F0}");
        if (buyFixed) fixedBuys.Add($"{sym} high={sessHigh} > {last2High} vol={sessVol}/{avgVol3:F0}");
        if (sym == "INDUSINDBK")
        {
            Console.WriteLine($"\nINDUSINDBK detail:");
            Console.WriteLine($"  bars={bars.Count} latest={latest.d} barH={latest.h} barC={latest.c} barV={latest.v}");
            Console.WriteLine($"  quote LTP={latest.ltp} sessH={latest.sh} quoteClose={latest.qc} sessV={latest.sv}");
            Console.WriteLine($"  last2High={last2High} last2Low={last2Low}");
            Console.WriteLine($"  CURRENT buy={buyCurrent} (price={priceCurrent} volOk={volOkCurrent})");
            Console.WriteLine($"  FIXED   buy={buyFixed} sell={sellFixed} (high={sessHigh} volOk={volOkFixed} avgVol3={avgVol3:F0})");
            Console.WriteLine($"  note: Angel quote close often = PREV day close; bar.close may be stale vs LTP");
        }
    }
    Console.WriteLine($"\nSkipped (<5 bars): {skipped}");
    Console.WriteLine($"\nCURRENT would BUY ({currentBuys.Count}):");
    foreach (var x in currentBuys) Console.WriteLine("  " + x);
    Console.WriteLine($"\nFIXED would BUY ({fixedBuys.Count}):");
    foreach (var x in fixedBuys) Console.WriteLine("  " + x);
}
