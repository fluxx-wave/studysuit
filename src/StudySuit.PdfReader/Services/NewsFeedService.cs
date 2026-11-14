using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface INewsFeedService
{
    Task<IReadOnlyList<NewsFeedItem>> FetchNewsAsync(
        NewsTimeframe timeframe,
        NewsCategory category,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<string?> FetchArticleContentAsync(
        string url,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches and caches Daily, Weekly, and Monthly news digests with lead images and in-app readability extraction.
/// </summary>
public sealed partial class NewsFeedService : INewsFeedService
{
    private readonly HttpClient _http;
    private readonly Dictionary<(NewsTimeframe, NewsCategory), (DateTime FetchedUtc, IReadOnlyList<NewsFeedItem> Items)> _cache = [];

    public NewsFeedService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? CreateDefaultClient();
    }

    public async Task<IReadOnlyList<NewsFeedItem>> FetchNewsAsync(
        NewsTimeframe timeframe,
        NewsCategory category,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var key = (timeframe, category);
        lock (_cache)
        {
            if (!forceRefresh &&
                _cache.TryGetValue(key, out var cached) &&
                (DateTime.UtcNow - cached.FetchedUtc).TotalMinutes < 10)
            {
                return cached.Items;
            }
        }

        var categoryInfo = NewsFeedCatalog.GetCategory(category);
        IReadOnlyList<NewsFeedItem> items = [];

        // For Daily news, try high-fidelity curated direct feeds first (rich images & direct links)
        if (timeframe == NewsTimeframe.Daily)
        {
            string? directUrl = GetDirectFeedUrl(category);
            if (!string.IsNullOrWhiteSpace(directUrl))
            {
                try
                {
                    using var resp = await _http.GetAsync(directUrl, cancellationToken).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string xml = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        items = ParseRssFeed(xml, timeframe, categoryInfo.Label, maxItems: 25);
                    }
                }
                catch
                {
                    // Fall back to Google News search aggregator below
                }
            }
        }

        // If direct feed was not used or yielded 0 items, use Google News search feed
        if (items.Count == 0)
        {
            string url = BuildGoogleNewsUrl(timeframe, category);
            using var resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string xml = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            items = ParseRssFeed(xml, timeframe, categoryInfo.Label, maxItems: 25);
        }

        lock (_cache)
        {
            _cache[key] = (DateTime.UtcNow, items);
        }

        return items;
    }

    public static string? GetDirectFeedUrl(NewsCategory category) => category switch
    {
        NewsCategory.TopStories => "https://feeds.feedburner.com/ndtvnews-top-stories",
        NewsCategory.IndiaPolity => "https://www.thehindu.com/news/national/feeder/default.rss",
        NewsCategory.Economy => "https://www.thehindu.com/business/Economy/feeder/default.rss",
        NewsCategory.WorldIR => "https://www.thehindu.com/news/international/feeder/default.rss",
        NewsCategory.ScienceTech => "https://www.thehindu.com/sci-tech/feeder/default.rss",
        NewsCategory.Environment => "https://www.thehindu.com/sci-tech/energy-and-environment/feeder/default.rss",
        _ => null
    };

    public static string BuildGoogleNewsUrl(NewsTimeframe timeframe, NewsCategory category)
    {
        string window = timeframe switch
        {
            NewsTimeframe.Daily => "when:1d",
            NewsTimeframe.Weekly => "when:7d",
            _ => "when:30d"
        };

        var cat = NewsFeedCatalog.GetCategory(category);
        string query = Uri.EscapeDataString($"{cat.QueryKeywords} {window}");
        return $"https://news.google.com/rss/search?q={query}&hl=en-IN&gl=IN&ceid=IN:en";
    }

    public static string BuildFeedUrl(NewsTimeframe timeframe, NewsCategory category) =>
        BuildGoogleNewsUrl(timeframe, category);

    public static IReadOnlyList<NewsFeedItem> ParseRssFeed(
        string xmlContent,
        NewsTimeframe timeframe,
        string categoryLabel,
        int maxItems = 30)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return [];
        }

        var doc = XDocument.Parse(xmlContent);
        var cutoff = timeframe switch
        {
            NewsTimeframe.Daily => DateTime.UtcNow.AddDays(-2),
            NewsTimeframe.Weekly => DateTime.UtcNow.AddDays(-9),
            _ => DateTime.UtcNow.AddDays(-35)
        };

        var list = new List<NewsFeedItem>();
        foreach (var item in doc.Descendants("item"))
        {
            string rawTitle = item.Element("title")?.Value?.Trim() ?? string.Empty;
            string link = item.Element("link")?.Value?.Trim() ?? string.Empty;
            string rawDesc = item.Element("description")?.Value?.Trim() ?? string.Empty;
            string source = item.Element("source")?.Value?.Trim() ?? string.Empty;
            string pubRaw = item.Element("pubDate")?.Value?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                int dashIdx = rawTitle.LastIndexOf(" - ", StringComparison.Ordinal);
                if (dashIdx > 8)
                {
                    source = rawTitle[(dashIdx + 3)..].Trim();
                    rawTitle = rawTitle[..dashIdx].Trim();
                }
            }
            else if (rawTitle.EndsWith($" - {source}", StringComparison.OrdinalIgnoreCase))
            {
                rawTitle = rawTitle[..^(source.Length + 3)].Trim();
            }

            DateTime publishedUtc = DateTimeOffset.TryParse(pubRaw, out var dto)
                ? dto.UtcDateTime
                : DateTime.UtcNow;

            if (publishedUtc < cutoff)
            {
                continue;
            }

            string cleanSummary = CleanHtmlSnippet(rawDesc);
            if (string.Equals(cleanSummary, rawTitle, StringComparison.OrdinalIgnoreCase))
            {
                cleanSummary = string.Empty;
            }

            // Extract image from media:content, media:thumbnail, enclosure, or description <img>
            string? imageUrl = ExtractImageUrl(item, rawDesc);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                imageUrl = NewsFeedCatalog.GetFallbackImageUrl(categoryLabel);
            }

            list.Add(new NewsFeedItem
            {
                Title = WebUtility.HtmlDecode(rawTitle),
                Summary = cleanSummary,
                Source = string.IsNullOrWhiteSpace(source) ? "News Wire" : WebUtility.HtmlDecode(source),
                Url = link,
                ImageUrl = imageUrl,
                PublishedUtc = publishedUtc,
                Timeframe = timeframe,
                CategoryLabel = categoryLabel
            });
        }

        return list
            .OrderByDescending(i => i.PublishedUtc)
            .Take(maxItems)
            .ToList();
    }

    public static string? ExtractImageUrl(XElement item, string description)
    {
        // 1. Check media:content
        var mediaContent = item.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "content", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Attribute("url")?.Value));
        if (mediaContent != null)
        {
            string url = mediaContent.Attribute("url")!.Value.Trim();
            if (IsValidHttpUrl(url)) return url;
        }

        // 2. Check media:thumbnail
        var mediaThumb = item.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "thumbnail", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Attribute("url")?.Value));
        if (mediaThumb != null)
        {
            string url = mediaThumb.Attribute("url")!.Value.Trim();
            if (IsValidHttpUrl(url)) return url;
        }

        // 3. Check enclosure
        var enclosure = item.Elements("enclosure").FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.Attribute("url")?.Value) &&
            (e.Attribute("type")?.Value?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
             IsImageExtension(e.Attribute("url")!.Value)));
        if (enclosure != null)
        {
            string url = enclosure.Attribute("url")!.Value.Trim();
            if (IsValidHttpUrl(url)) return url;
        }

        // 4. Check <img> tags inside description
        if (!string.IsNullOrWhiteSpace(description))
        {
            var match = ImgSrcRegex().Match(description);
            if (match.Success)
            {
                string url = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                if (IsValidHttpUrl(url)) return url;
            }
        }

        // 5. Check <content:encoded>
        var encoded = item.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "encoded", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            var match = ImgSrcRegex().Match(encoded);
            if (match.Success)
            {
                string url = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                if (IsValidHttpUrl(url)) return url;
            }
        }

        return null;
    }

    public async Task<string?> FetchArticleContentAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !IsValidHttpUrl(url))
        {
            return null;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ExtractArticleBody(html);
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractArticleBody(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        // 1. Find main article container if available
        string targetHtml = html;
        var containerMatch = ArticleContainerRegex().Match(html);
        if (containerMatch.Success)
        {
            targetHtml = containerMatch.Value;
        }

        // 2. Extract paragraph tags
        var matches = ParagraphRegex().Matches(targetHtml);
        var paragraphs = new List<string>();

        foreach (Match match in matches)
        {
            string clean = CleanHtmlSnippet(match.Groups[1].Value);
            if (clean.Length >= 40 && !IsBoilerplate(clean))
            {
                paragraphs.Add(clean);
            }
        }

        if (paragraphs.Count >= 2)
        {
            return string.Join("\n\n", paragraphs.Take(30));
        }

        // Fallback: search the entire page for substantial paragraphs
        if (containerMatch.Success)
        {
            paragraphs.Clear();
            var allMatches = ParagraphRegex().Matches(html);
            foreach (Match match in allMatches)
            {
                string clean = CleanHtmlSnippet(match.Groups[1].Value);
                if (clean.Length >= 60 && !IsBoilerplate(clean))
                {
                    paragraphs.Add(clean);
                }
            }

            if (paragraphs.Count >= 2)
            {
                return string.Join("\n\n", paragraphs.Take(25));
            }
        }

        return paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : null;
    }

    private static bool IsBoilerplate(string text)
    {
        string lower = text.ToLowerInvariant();
        return lower.Contains("subscribe to") ||
               lower.Contains("subscription") ||
               lower.Contains("all rights reserved") ||
               lower.Contains("click here to") ||
               lower.Contains("read also:") ||
               lower.Contains("also read:") ||
               lower.Contains("terms of use") ||
               lower.Contains("privacy policy") ||
               lower.Contains("cookie policy") ||
               lower.Contains("sign in with") ||
               lower.Contains("log in to") ||
               lower.Contains("download our app") ||
               lower.Contains("share this story") ||
               lower.Contains("whatsapp channel");
    }

    private static bool IsValidHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsImageExtension(string url)
    {
        int queryIdx = url.IndexOf('?');
        string clean = queryIdx > 0 ? url[..queryIdx] : url;
        return clean.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
               clean.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanHtmlSnippet(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string noTags = HtmlTagRegex().Replace(html, " ");
        string decoded = WebUtility.HtmlDecode(noTags);
        string compact = MultiSpaceRegex().Replace(decoded, " ").Trim();
        return compact;
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"<img[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcRegex();

    [GeneratedRegex(@"<article[^>]*>([\s\S]*?)</article>|<div[^>]+class=[""'][^""']*(?:article-body|story-body|content-body|entry-content|article__body|story_details)[^""']*[""'][^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleContainerRegex();

    [GeneratedRegex(@"<p[^>]*>([\s\S]*?)</p>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();
}
