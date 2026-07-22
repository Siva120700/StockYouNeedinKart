using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtpNet;
using StockYouNeed.Application.Abstractions;
using StockYouNeed.Application.Options;

namespace StockYouNeed.Infrastructure.Angel;

public sealed class AngelMarketDataClient : IAngelMarketDataClient
{
    private readonly HttpClient _http;
    private readonly AngelOptions _options;
    private readonly ILogger<AngelMarketDataClient> _logger;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _jwt;
    private string? _feedToken;
    private DateTimeOffset _sessionExpiresAt = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public AngelMarketDataClient(HttpClient http, IOptions<AngelOptions> options, ILogger<AngelMarketDataClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-PrivateKey", _options.ApiKey);
    }

    public async Task EnsureSessionAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return;

        if (!string.IsNullOrEmpty(_jwt) && DateTimeOffset.UtcNow < _sessionExpiresAt)
            return;

        await _sessionLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_jwt) && DateTimeOffset.UtcNow < _sessionExpiresAt)
                return;

            if (string.IsNullOrWhiteSpace(_options.ClientCode)
                || string.IsNullOrWhiteSpace(_options.Password)
                || string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException(
                    "Angel credentials missing. Set Angel:ApiKey, ClientCode, Password, TotpSecret.");
            }

            var totp = "";
            if (!string.IsNullOrWhiteSpace(_options.TotpSecret))
            {
                var key = Base32Encoding.ToBytes(_options.TotpSecret.Replace(" ", ""));
                totp = new Totp(key).ComputeTotp();
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, "rest/auth/angelbroking/user/v1/loginByPassword");
            req.Headers.TryAddWithoutValidation("X-UserType", "USER");
            req.Headers.TryAddWithoutValidation("X-SourceID", "WEB");
            req.Headers.TryAddWithoutValidation("X-ClientLocalIP", "127.0.0.1");
            req.Headers.TryAddWithoutValidation("X-ClientPublicIP", "127.0.0.1");
            req.Headers.TryAddWithoutValidation("X-MACAddress", "00:00:00:00:00:00");
            req.Content = JsonContent.Create(new
            {
                clientcode = _options.ClientCode,
                password = _options.Password,
                totp
            });

            var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var st) && st.GetBoolean();
            if (!status)
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : body;
                throw new InvalidOperationException($"Angel login failed: {msg}");
            }

            var data = root.GetProperty("data");
            _jwt = data.GetProperty("jwtToken").GetString();
            _feedToken = data.TryGetProperty("feedToken", out var ft) ? ft.GetString() : null;
            _sessionExpiresAt = DateTimeOffset.UtcNow.AddHours(7);
            _logger.LogInformation("Angel session established.");
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task<IReadOnlyList<AngelQuote>> GetQuotesAsync(
        string mode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> exchangeTokens,
        CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        using var req = CreateSecureRequest(HttpMethod.Post, "rest/secure/angelbroking/market/v1/quote/");
        req.Content = JsonContent.Create(new { mode, exchangeTokens });

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var ok = (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.True)
                 || (root.TryGetProperty("success", out var su) && su.ValueKind == JsonValueKind.True);
        if (!ok)
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : body;
            throw new InvalidOperationException($"Angel quote failed: {msg}");
        }

        var list = new List<AngelQuote>();
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("fetched", out var fetched)
            || fetched.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in fetched.EnumerateArray())
        {
            list.Add(new AngelQuote
            {
                Exchange = GetString(item, "exchange"),
                TradingSymbol = GetString(item, "tradingSymbol"),
                SymbolToken = GetString(item, "symbolToken"),
                Ltp = GetDecimal(item, "ltp"),
                Open = GetDecimal(item, "open"),
                High = GetDecimal(item, "high"),
                Low = GetDecimal(item, "low"),
                Close = GetDecimal(item, "close"),
                TradeVolume = GetLong(item, "tradeVolume"),
                RawJson = item.GetRawText()
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<AngelCandle>> GetDailyCandlesAsync(
        string exchange,
        string symbolToken,
        DateTime fromIst,
        DateTime toIst,
        CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        using var req = CreateSecureRequest(HttpMethod.Post, "rest/secure/angelbroking/historical/v1/getCandleData");
        req.Content = JsonContent.Create(new
        {
            exchange,
            symboltoken = symbolToken,
            interval = "ONE_DAY",
            fromdate = fromIst.ToString("yyyy-MM-dd HH:mm"),
            todate = toIst.ToString("yyyy-MM-dd HH:mm")
        });

        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("status", out var st) && st.GetBoolean();
        if (!ok)
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : body;
            _logger.LogWarning("Candle fetch failed for {Token}: {Message}", symbolToken, msg);
            return Array.Empty<AngelCandle>();
        }

        var candles = new List<AngelCandle>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return candles;

        // [[timestamp, open, high, low, close, volume], ...]
        foreach (var row in data.EnumerateArray())
        {
            if (row.GetArrayLength() < 6)
                continue;
            var ts = row[0].GetString() ?? "";
            if (!DateTime.TryParse(ts, out var dt))
                continue;
            candles.Add(new AngelCandle
            {
                TradeDate = DateOnly.FromDateTime(dt),
                Open = row[1].GetDecimal(),
                High = row[2].GetDecimal(),
                Low = row[3].GetDecimal(),
                Close = row[4].GetDecimal(),
                Volume = row[5].GetInt64()
            });
        }

        return candles;
    }

    public async Task<IReadOnlyList<AngelScrip>> DownloadScripMasterAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        await using var stream = await http.GetStreamAsync(_options.ScripMasterUrl, ct);
        var scrips = await JsonSerializer.DeserializeAsync<List<AngelScripDto>>(stream, JsonOpts, ct)
                     ?? new List<AngelScripDto>();

        return scrips.Select(s => new AngelScrip
        {
            Token = s.Token ?? "",
            Symbol = s.Symbol ?? "",
            Name = s.Name ?? "",
            ExchSeg = s.ExchSeg ?? "",
            InstrumentType = s.InstrumentType ?? "",
            LotSize = s.LotSize ?? "1",
            TickSize = s.TickSize ?? "0.05",
            Expiry = s.Expiry ?? ""
        }).ToList();
    }

    private HttpRequestMessage CreateSecureRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt);
        req.Headers.TryAddWithoutValidation("X-PrivateKey", _options.ApiKey);
        req.Headers.TryAddWithoutValidation("X-UserType", "USER");
        req.Headers.TryAddWithoutValidation("X-SourceID", "WEB");
        req.Headers.TryAddWithoutValidation("X-ClientLocalIP", "127.0.0.1");
        req.Headers.TryAddWithoutValidation("X-ClientPublicIP", "127.0.0.1");
        req.Headers.TryAddWithoutValidation("X-MACAddress", "00:00:00:00:00:00");
        if (!string.IsNullOrEmpty(_feedToken))
            req.Headers.TryAddWithoutValidation("X-FeedToken", _feedToken);
        return req;
    }

    private static string GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static decimal? GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(p.GetString(), out var d) => d,
            _ => null
        };
    }

    private static long? GetLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetInt64(),
            JsonValueKind.String when long.TryParse(p.GetString(), out var d) => d,
            _ => null
        };
    }

    private sealed class AngelScripDto
    {
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("exch_seg")] public string? ExchSeg { get; set; }
        [JsonPropertyName("instrumenttype")] public string? InstrumentType { get; set; }
        [JsonPropertyName("lotsize")] public string? LotSize { get; set; }
        [JsonPropertyName("tick_size")] public string? TickSize { get; set; }
        [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    }
}
