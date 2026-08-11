using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockYouNeed.Application.Options;
using StockYouNeed.Domain;

namespace StockYouNeed.Application.News;

public sealed class MarketNewsService
{
    public const string HttpClientName = "market-news";
    private const string CacheKey = "market-news:aggregate";
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly NewsOptions _options;
    private readonly ILogger<MarketNewsService> _logger;

    public MarketNewsService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<NewsOptions> options,
        ILogger<MarketNewsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MarketNewsItem>> GetNewsAsync(int limit, CancellationToken ct = default)
    {
        var take = limit <= 0 ? 40 : Math.Clamp(limit, 1, 100);
        var all = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            var ttl = TimeSpan.FromSeconds(Math.Clamp(_options.CacheSeconds, 30, 3600));
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return await FetchAggregateAsync(ct);
        }) ?? Array.Empty<MarketNewsItem>();

        return all.Take(take).ToList();
    }

    private async Task<IReadOnlyList<MarketNewsItem>> FetchAggregateAsync(CancellationToken ct)
    {
        var feeds = (_options.FeedUrls ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (feeds.Length == 0)
            return Array.Empty<MarketNewsItem>();

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var tasks = feeds.Select(url => FetchFeedAsync(client, url, ct));
        var batches = await Task.WhenAll(tasks);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<MarketNewsItem>();
        foreach (var item in batches.SelectMany(x => x))
        {
            var key = $"{Normalize(item.Title)}|{Normalize(item.Url)}";
            if (!seen.Add(key))
                continue;
            merged.Add(item);
        }

        return merged
            .OrderByDescending(x => x.PublishedAt)
            .ToList();
    }

    private async Task<IReadOnlyList<MarketNewsItem>> FetchFeedAsync(
        HttpClient client,
        string feedUrl,
        CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(feedUrl, ct);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync(ct);
            return ParseRss(xml, FallbackSourceFromUrl(feedUrl));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Market news feed failed: {FeedUrl}", feedUrl);
            return Array.Empty<MarketNewsItem>();
        }
    }

    /// <summary>Parse an RSS 2.0 (or Atom-ish item) document into news rows.</summary>
    internal static IReadOnlyList<MarketNewsItem> ParseRss(string xml, string fallbackSource)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Array.Empty<MarketNewsItem>();

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch
        {
            return Array.Empty<MarketNewsItem>();
        }

        var channelTitle = CleanText(doc.Root?
            .Element("channel")?
            .Element("title")?
            .Value);
        var defaultSource = string.IsNullOrWhiteSpace(channelTitle)
            ? fallbackSource
            : channelTitle;

        var items = doc.Descendants("item");
        var results = new List<MarketNewsItem>();
        foreach (var item in items)
        {
            var title = CleanText(item.Element("title")?.Value);
            var link = CleanText(item.Element("link")?.Value);
            if (string.IsNullOrWhiteSpace(link))
            {
                var guid = item.Element("guid");
                if (guid is not null &&
                    (!guid.Attributes().Any(a => a.Name.LocalName == "isPermaLink") ||
                     string.Equals(guid.Attribute("isPermaLink")?.Value, "true", StringComparison.OrdinalIgnoreCase)))
                {
                    link = CleanText(guid.Value);
                }
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            var summary = CleanText(
                item.Element("description")?.Value
                ?? item.Element(MediaNs + "description")?.Value
                ?? "");

            var published = ParseDate(
                item.Element("pubDate")?.Value
                ?? item.Element(DcNs + "date")?.Value);

            var source = CleanText(
                item.Element("source")?.Value
                ?? defaultSource);

            results.Add(new MarketNewsItem
            {
                Id = StableId(link),
                Title = title,
                Summary = Truncate(summary, 400),
                Url = link,
                Source = string.IsNullOrWhiteSpace(source) ? fallbackSource : source,
                PublishedAt = published,
            });
        }

        return results;
    }

    private static string FallbackSourceFromUrl(string feedUrl)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri))
            return "RSS";
        if (uri.Host.Contains("ndtv", StringComparison.OrdinalIgnoreCase))
            return "NDTV Profit";
        if (uri.Host.Contains("google", StringComparison.OrdinalIgnoreCase))
            return "Google News";
        return uri.Host;
    }

    private static DateTimeOffset ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(raw, out var dto))
            return dto.ToUniversalTime();
        return DateTimeOffset.UtcNow;
    }

    private static string CleanText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var decoded = WebUtility.HtmlDecode(value);
        decoded = HtmlTagRegex.Replace(decoded, " ");
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..(max - 1)].TrimEnd() + "…";
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    private static string StableId(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim()));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
