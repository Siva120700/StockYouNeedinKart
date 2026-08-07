using System.Text.Json;
using Npgsql;
using StockYouNeed.Application.Services;
using StockYouNeed.Domain;

var cs = ResolveConnectionString(args);
static string ResolveConnectionString(string[] args)
{
    if (args.Length > 0 && args[0].Contains("Host=", StringComparison.OrdinalIgnoreCase))
        return args[0];

    var candidates = new[]
    {
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "StockYouNeed.Api", "appsettings.json")),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "StockYouNeed.Api", "appsettings.json")),
    };
    foreach (var path in candidates)
    {
        if (!File.Exists(path)) continue;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("Database", out var db)
            && db.TryGetProperty("ConnectionString", out var c))
            return c.GetString() ?? "";
    }
    return "Host=localhost;Port=5432;Database=stockyouneed;Username=postgres;Password=siva1207";
}

var ist = TimeSpan.FromHours(5.5);
var asOf = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(ist).DateTime);
Console.WriteLine($"asOf={asOf} IST={DateTimeOffset.UtcNow.ToOffset(ist):HH:mm:ss}");

await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

var instruments = new List<(Guid Id, string Symbol)>();
await using (var cmd = new NpgsqlCommand("""
    SELECT DISTINCT i.id, i.symbol
    FROM instruments i
    JOIN universe_memberships u ON u.instrument_id = i.id
    WHERE i.kind = 'equity' AND i.is_active
    ORDER BY i.symbol
    """, conn))
await using (var r = await cmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
        instruments.Add((r.GetGuid(0), r.GetString(1)));
}

var ltp = new Dictionary<Guid, decimal>();
await using (var cmd = new NpgsqlCommand("SELECT instrument_id, ltp FROM market_ltp", conn))
await using (var r = await cmd.ExecuteReaderAsync())
{
    while (await r.ReadAsync())
        ltp[r.GetGuid(0)] = r.GetDecimal(1);
}

var counts = new Dictionary<string, int>
{
    ["scanned"] = 0,
    ["fewBars"] = 0,
    ["noSide"] = 0,
    ["noT1_afterRoll"] = 0,
    ["rrFail"] = 0,
    ["actionableFail"] = 0,
    ["ok_actionable"] = 0,
};
var samples = new List<string>();

foreach (var (id, symbol) in instruments)
{
    counts["scanned"]++;
    var bars = await LoadBars(conn, id, symbol);
    if (bars.Count < 5)
    {
        counts["fewBars"]++;
        continue;
    }

    decimal? live = ltp.TryGetValue(id, out var px) && px > 0 ? px : null;
    var reason = Classify(asOf, bars, live);
    counts[reason]++;

    if (samples.Count >= 35) continue;
    if (reason == "ok_actionable")
        samples.Add($"OK {symbol}");
    else if (reason is "actionableFail" or "rrFail" or "noT1_afterRoll")
    {
        var mark = live ?? bars[0].Close;
        samples.Add($"{reason} {symbol} mark={mark:F2} H={bars[0].High:F2} L={bars[0].Low:F2} vol={bars[0].Volume}");
    }
}

Console.WriteLine("--- reject funnel ---");
foreach (var kv in counts)
    Console.WriteLine($"{kv.Key}={kv.Value}");
Console.WriteLine("--- samples ---");
foreach (var s in samples)
    Console.WriteLine(s);

static async Task<List<MarketBarRow>> LoadBars(NpgsqlConnection conn, Guid id, string symbol)
{
    var bars = new List<MarketBarRow>();
    await using var cmd = new NpgsqlCommand("""
        SELECT trade_date, open, high, low, close, volume
        FROM market_bars
        WHERE instrument_id = @id
        ORDER BY trade_date DESC
        LIMIT 10
        """, conn);
    cmd.Parameters.AddWithValue("id", id);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        bars.Add(new MarketBarRow
        {
            InstrumentId = id,
            AppSymbol = symbol,
            TradeDate = DateOnly.FromDateTime(r.GetDateTime(0)),
            Open = r.GetDecimal(1),
            High = r.GetDecimal(2),
            Low = r.GetDecimal(3),
            Close = r.GetDecimal(4),
            Volume = r.GetInt64(5),
        });
    }
    return bars;
}

static string Classify(DateOnly asOf, List<MarketBarRow> bars, decimal? livePrice)
{
    if (BreakoutSignalEvaluator.Evaluate(
            Guid.Empty, Guid.Empty, asOf, bars, livePrice,
            actionableOnly: true, projectPartialSessionVolume: true) is not null)
        return "ok_actionable";

    if (BreakoutSignalEvaluator.Evaluate(
            Guid.Empty, Guid.Empty, asOf, bars, livePrice,
            actionableOnly: false, projectPartialSessionVolume: true) is not null)
        return "actionableFail";

    var latest = bars[0];
    var prev = bars.Skip(1).Take(2).ToList();
    if (prev.Count < 2) return "fewBars";

    var last2High = prev.Max(b => b.High);
    var last2Low = prev.Min(b => b.Low);
    var prior3 = bars.Skip(1).Take(3).ToList();
    var avgVol = prior3.Average(b => (double)b.Volume);
    var effVol = BreakoutSignalEvaluator.EffectiveVolumeForGate(latest.Volume, asOf, true);
    var volumeOk = effVol >= (long)(avgVol * 0.25);
    var ltp = livePrice ?? latest.Close;

    var buyBreak = latest.High > last2High;
    var sellBreak = latest.Low < last2Low;
    var buyImm = !buyBreak && ltp >= last2High * 0.99m && ltp < last2High;
    var sellImm = !sellBreak && ltp <= last2Low * 1.01m && ltp > last2Low;

    string? side = null;
    if ((buyBreak || buyImm) && (sellBreak || sellImm) && volumeOk)
        side = ltp >= (last2High + last2Low) / 2m ? SignalSides.Buy : SignalSides.Sell;
    else if ((buyBreak || buyImm) && volumeOk)
        side = SignalSides.Buy;
    else if ((sellBreak || sellImm) && volumeOk)
        side = SignalSides.Sell;

    if (side is null)
        return "noSide";

    var entry = side == SignalSides.Buy ? last2High : last2Low;
    decimal sl;
    decimal? t1;
    decimal? t2;
    decimal? t3;

    if (side == SignalSides.Buy)
    {
        sl = last2Low;
        if (sl >= entry) sl = entry * 0.98m;
        var buyTargets = new List<decimal>();
        foreach (var n in new[] { 5, 3, 2 })
        {
            var avg = BreakoutSignalEvaluator.AvgDirectionalMovePct(bars, n, up: true);
            if (avg <= 0) continue;
            var t = Math.Round(entry * (1 + avg), 2, MidpointRounding.AwayFromZero);
            if (t > entry) buyTargets.Add(t);
        }
        buyTargets = buyTargets.Distinct().OrderBy(x => x).ToList();
        t1 = buyTargets.Count > 0 ? buyTargets[0] : null;
        t2 = buyTargets.Count > 1 ? buyTargets[1] : null;
        t3 = buyTargets.Count > 2 ? buyTargets[2] : null;
    }
    else
    {
        sl = last2High;
        if (sl <= entry) sl = entry * 1.02m;
        var sellTargets = new List<decimal>();
        foreach (var n in new[] { 5, 3, 2 })
        {
            var avg = BreakoutSignalEvaluator.AvgDirectionalMovePct(bars, n, up: false);
            if (avg <= 0) continue;
            var t = Math.Round(entry * (1 - avg), 2, MidpointRounding.AwayFromZero);
            if (t < entry) sellTargets.Add(t);
        }
        sellTargets = sellTargets.Distinct().OrderByDescending(x => x).ToList();
        t1 = sellTargets.Count > 0 ? sellTargets[0] : null;
        t2 = sellTargets.Count > 1 ? sellTargets[1] : null;
        t3 = sellTargets.Count > 2 ? sellTargets[2] : null;
    }

    (t1, t2, t3) = BreakoutSignalEvaluator.RollPastSpentTargets(side, t1, t2, t3, ltp, latest);
    if (t1 is null)
        return "noT1_afterRoll";

    var risk = Math.Abs(entry - sl);
    var reward = Math.Abs(t1.Value - entry);
    if (risk <= 0 || reward < risk)
        return "rrFail";

    return "actionableFail";
}
