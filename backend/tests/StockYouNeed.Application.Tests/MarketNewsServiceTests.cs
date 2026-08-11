using StockYouNeed.Application.News;

namespace StockYouNeed.Application.Tests;

public class MarketNewsServiceTests
{
    private const string SampleRss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>NDTV Profit - Latest</title>
            <item>
              <title>Sensex, Nifty open higher</title>
              <link>https://www.ndtvprofit.com/markets/sensex-nifty-open-higher</link>
              <description><![CDATA[<p>Benchmarks rise on strong Asian cues.</p>]]></description>
              <pubDate>Mon, 10 Aug 2026 07:05:16 +0530</pubDate>
            </item>
            <item>
              <title></title>
              <link>https://www.ndtvprofit.com/markets/empty-title</link>
              <pubDate>Mon, 10 Aug 2026 07:00:00 +0530</pubDate>
            </item>
            <item>
              <title>Duplicate title kept once</title>
              <link>https://www.ndtvprofit.com/markets/dup</link>
              <description>First</description>
              <pubDate>Mon, 10 Aug 2026 06:55:00 +0530</pubDate>
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public void ParseRss_ExtractsTitleSummarySourceAndDate()
    {
        var items = MarketNewsService.ParseRss(SampleRss, "fallback");

        Assert.Equal(2, items.Count);
        var first = items[0];
        Assert.Equal("Sensex, Nifty open higher", first.Title);
        Assert.Equal("https://www.ndtvprofit.com/markets/sensex-nifty-open-higher", first.Url);
        Assert.Equal("Benchmarks rise on strong Asian cues.", first.Summary);
        Assert.Equal("NDTV Profit - Latest", first.Source);
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 7, 5, 16, TimeSpan.FromHours(5.5)).ToUniversalTime(),
            first.PublishedAt);
    }

    [Fact]
    public void ParseRss_SkipsEmptyTitleAndStripsHtml()
    {
        var items = MarketNewsService.ParseRss(SampleRss, "fallback");
        Assert.DoesNotContain(items, i => i.Url.Contains("empty-title", StringComparison.Ordinal));
        Assert.DoesNotContain(items[0].Summary, "<");
    }

    [Fact]
    public void ParseRss_InvalidXml_ReturnsEmpty()
    {
        var items = MarketNewsService.ParseRss("<not-rss", "fallback");
        Assert.Empty(items);
    }
}
