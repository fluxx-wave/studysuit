using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private static readonly ConcurrentDictionary<string, ImageSource> _imageCache = new();
    private static readonly SemaphoreSlim _imageDownloadThrottle = new(4, 4);

    public HttpClient HttpClient => _http;

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
                foreach (var item in cached.Items)
                {
                    if (item.ImageBitmap == null)
                    {
                        _ = LoadItemBitmapAsync(item, _http);
                    }
                }
                return cached.Items;
            }
        }

        var categoryInfo = NewsFeedCatalog.GetCategory(category);
        IReadOnlyList<NewsFeedItem> items = [];

        // For Daily and Weekly news, query direct high-fidelity verified feeds across premier publishers:
        var directSources = GetDirectFeedSources(category);
        if (directSources.Count > 0)
        {
            var feedTasks = directSources.Select(async src =>
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, src.Url);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        string xml = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        return ParseRssFeed(xml, timeframe, categoryInfo.Label, maxItems: 20, defaultPublisher: src.PublisherName);
                    }
                }
                catch
                {
                }
                return (IReadOnlyList<NewsFeedItem>)[];
            });

            var results = await Task.WhenAll(feedTasks).ConfigureAwait(false);
            var combinedList = new List<NewsFeedItem>();
            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var feedList in results)
            {
                foreach (var item in feedList)
                {
                    string norm = NormalizeTitleForDeduplication(item.Title);
                    if (!string.IsNullOrWhiteSpace(norm) && seenTitles.Add(norm))
                    {
                        combinedList.Add(item);
                    }
                }
            }

            if (combinedList.Count > 0)
            {
                var sorted = combinedList
                    .OrderByDescending(i => i.PublishedUtc)
                    .Take(35)
                    .ToList();

                for (int idx = 0; idx < sorted.Count; idx++)
                {
                    var it = sorted[idx];
                    bool isHero = (idx == 0 || idx == 5 || (idx > 5 && (idx - 5) % 6 == 0));
                    it.IsHero = isHero;
                    it.CardWidth = isHero ? 690 : 335;
                    it.CardHeight = 360;
                }

                items = sorted;
            }
        }

        // If direct feeds yielded 0 items, use Google News search aggregator
        if (items.Count == 0)
        {
            string url = BuildGoogleNewsUrl(timeframe, category);
            using var resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string xml = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            items = ParseRssFeed(xml, timeframe, categoryInfo.Label, maxItems: 25);
        }

        // Asynchronously load thumbnail image bitmaps for all items:
        foreach (var item in items)
        {
            _ = LoadItemBitmapAsync(item, _http);
        }

        lock (_cache)
        {
            _cache[key] = (DateTime.UtcNow, items);
        }

        return items;
    }

    public sealed record NewsFeedSource(string Url, string PublisherName);

    public static IReadOnlyList<NewsFeedSource> GetDirectFeedSources(NewsCategory category) => category switch
    {
        NewsCategory.TopStories =>
        [
            new("https://www.thehindu.com/news/national/feeder/default.rss", "The Hindu"),
            new("https://indianexpress.com/section/india/feed/", "The Indian Express"),
            new("https://timesofindia.indiatimes.com/rssfeedstopstories.cms", "The Times of India"),
            new("https://feeds.bbci.co.uk/news/world/asia/india/rss.xml", "BBC News")
        ],
        NewsCategory.IndiaPolity =>
        [
            new("https://www.thehindu.com/news/national/feeder/default.rss", "The Hindu"),
            new("https://indianexpress.com/section/india/feed/", "The Indian Express"),
            new("https://timesofindia.indiatimes.com/rssfeeds/-2128936835.cms", "The Times of India")
        ],
        NewsCategory.Economy =>
        [
            new("https://www.thehindu.com/business/Economy/feeder/default.rss", "The Hindu"),
            new("https://indianexpress.com/section/business/feed/", "The Indian Express"),
            new("https://timesofindia.indiatimes.com/rssfeeds/1898055.cms", "The Times of India")
        ],
        NewsCategory.WorldIR =>
        [
            new("https://www.thehindu.com/news/international/feeder/default.rss", "The Hindu"),
            new("https://indianexpress.com/section/world/feed/", "The Indian Express"),
            new("https://timesofindia.indiatimes.com/rssfeeds/296589292.cms", "The Times of India"),
            new("https://feeds.bbci.co.uk/news/world/rss.xml", "BBC News")
        ],
        NewsCategory.ScienceTech =>
        [
            new("https://www.thehindu.com/sci-tech/feeder/default.rss", "The Hindu"),
            new("https://indianexpress.com/section/technology/feed/", "The Indian Express"),
            new("https://timesofindia.indiatimes.com/rssfeeds/66949542.cms", "The Times of India"),
            new("https://feeds.bbci.co.uk/news/science_and_environment/rss.xml", "BBC News")
        ],
        NewsCategory.Environment =>
        [
            new("https://www.thehindu.com/sci-tech/energy-and-environment/feeder/default.rss", "The Hindu"),
            new("https://timesofindia.indiatimes.com/rssfeeds/2647163.cms", "The Times of India"),
            new("https://feeds.bbci.co.uk/news/science_and_environment/rss.xml", "BBC News")
        ],
        _ =>
        [
            new("https://www.thehindu.com/news/national/feeder/default.rss", "The Hindu"),
            new("https://indianexpress.com/section/india/feed/", "The Indian Express")
        ]
    };

    public static string? GetDirectFeedUrl(NewsCategory category) =>
        GetDirectFeedSources(category).FirstOrDefault()?.Url;

    public static string ResolveSourceFromUrl(string url, string? fallback = null)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            string host = uri.Host.ToLowerInvariant();
            if (host.Contains("thehindu.com")) return "The Hindu";
            if (host.Contains("indianexpress.com")) return "The Indian Express";
            if (host.Contains("timesofindia") || host.Contains("indiatimes.com")) return "The Times of India";
            if (host.Contains("bbc.co.uk") || host.Contains("bbc.com")) return "BBC News";
            if (host.Contains("ndtv.com")) return "NDTV";
            if (host.Contains("hindustantimes.com")) return "Hindustan Times";
            if (host.Contains("downtoearth.org.in")) return "Down To Earth";
            if (host.Contains("livemint.com")) return "Mint";
            if (host.Contains("theprint.in")) return "The Print";
            if (host.Contains("reuters.com")) return "Reuters";
        }
        if (!string.IsNullOrWhiteSpace(fallback) && !fallback.Equals("News Wire", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }
        return "National Edition";
    }

    private static string NormalizeTitleForDeduplication(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var rg = new Regex(@"[^\w\d\s]", RegexOptions.Compiled);
        return rg.Replace(title.ToLowerInvariant(), " ").Trim();
    }

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
        int maxItems = 30,
        string? defaultPublisher = null)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return [];
        }

        var doc = XDocument.Parse(xmlContent);
        var cutoff = timeframe switch
        {
            NewsTimeframe.Daily => DateTime.UtcNow.AddDays(-7),
            NewsTimeframe.Weekly => DateTime.UtcNow.AddDays(-21),
            _ => DateTime.UtcNow.AddDays(-90)
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
                else
                {
                    source = ResolveSourceFromUrl(link, defaultPublisher);
                }
            }
            else if (rawTitle.EndsWith($" - {source}", StringComparison.OrdinalIgnoreCase))
            {
                rawTitle = rawTitle[..^(source.Length + 3)].Trim();
            }

            if (string.IsNullOrWhiteSpace(source) || source.Equals("News Wire", StringComparison.OrdinalIgnoreCase))
            {
                source = ResolveSourceFromUrl(link, defaultPublisher);
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
                imageUrl = NewsFeedCatalog.GetContextualImageUrl(rawTitle, categoryLabel, list.Count);
            }

            list.Add(new NewsFeedItem
            {
                Title = WebUtility.HtmlDecode(rawTitle),
                Summary = cleanSummary,
                Source = WebUtility.HtmlDecode(source),
                Url = link,
                ImageUrl = imageUrl,
                PublishedUtc = publishedUtc,
                Timeframe = timeframe,
                CategoryLabel = categoryLabel
            });
        }

        var sorted = list
            .OrderByDescending(i => i.PublishedUtc)
            .Take(maxItems)
            .ToList();

        for (int idx = 0; idx < sorted.Count; idx++)
        {
            var it = sorted[idx];
            // MSN layout rhythm:
            // Item 0 is Hero wide card (690px wide)
            // Item 1, 2, 3, 4 are standard (335px)
            // Item 5 is Hero wide card (690px wide)
            // Items 6, 7, 8, 9 are standard (335px)
            // Item 10 is Hero wide card (690px wide)...
            bool isHero = (idx == 0 || idx == 5 || (idx > 5 && (idx - 5) % 6 == 0));
            it.IsHero = isHero;
            it.CardWidth = isHero ? 690 : 335;
            it.CardHeight = 360;
        }

        return sorted;
    }

    public static string NormalizeNewsImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        // Times of India CDN optimization:
        // Raw URL: https://static.toiimg.com/photo/msid-135908977,imgsize-1793657.cms (1.8MB)
        // High-speed resized CDN Thumbnail: https://static.toiimg.com/thumb/msid-135908977,width-640,resizemode-4/135908977.jpg (20KB)
        if (url.Contains("static.toiimg.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("indiatimes.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(url, @"msid-(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string msid = match.Groups[1].Value;
                return $"https://static.toiimg.com/thumb/msid-{msid},width-640,resizemode-4/{msid}.jpg";
            }
        }

        return url;
    }

    public static async Task LoadItemBitmapAsync(NewsFeedItem item, HttpClient httpClient)
    {
        if (item.ImageBitmap != null)
        {
            return;
        }

        string rawUrl = item.ImageUrl ?? NewsFeedCatalog.GetContextualImageUrl(item.Title, item.CategoryLabel);
        string url = NormalizeNewsImageUrl(rawUrl);

        if (_imageCache.TryGetValue(url, out var cached))
        {
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    item.ImageBitmap = cached;
                    item.IsImageLoading = false;
                });
            }
            else
            {
                item.ImageBitmap = cached;
                item.IsImageLoading = false;
            }
            return;
        }

        await _imageDownloadThrottle.WaitAsync().ConfigureAwait(false);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/*;q=0.8");
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
            {
                if (parsedUri.Host.Contains("toiimg.com", StringComparison.OrdinalIgnoreCase) ||
                    parsedUri.Host.Contains("indiatimes.com", StringComparison.OrdinalIgnoreCase))
                {
                    req.Headers.Referrer = new Uri("https://timesofindia.indiatimes.com/");
                }
                else
                {
                    req.Headers.Referrer = new Uri($"{parsedUri.Scheme}://{parsedUri.Host}/");
                }
            }
            using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length > 0)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.DecodePixelWidth = 640;
                    bi.StreamSource = new MemoryStream(bytes);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();

                    // Guard against 1x1 tracking or broken placeholder pixels
                    if (bi.PixelWidth > 15 && bi.PixelHeight > 15)
                    {
                        _imageCache[url] = bi;

                        if (System.Windows.Application.Current?.Dispatcher != null)
                        {
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                item.ImageBitmap = bi;
                                item.IsImageLoading = false;
                            });
                        }
                        else
                        {
                            item.ImageBitmap = bi;
                            item.IsImageLoading = false;
                        }
                        return;
                    }
                }
            }
        }
        catch { }
        finally
        {
            _imageDownloadThrottle.Release();
        }

        // Fallback to high-res Unsplash direct JPEG:
        try
        {
            string fallbackUrl = NewsFeedCatalog.GetContextualImageUrl(item.Title, item.CategoryLabel);
            if (_imageCache.TryGetValue(fallbackUrl, out var cachedFallback))
            {
                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        item.ImageBitmap = cachedFallback;
                        item.IsImageLoading = false;
                    });
                }
                else
                {
                    item.ImageBitmap = cachedFallback;
                    item.IsImageLoading = false;
                }
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.StreamSource = new MemoryStream(bytes);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();

                _imageCache[fallbackUrl] = bi;

                if (System.Windows.Application.Current?.Dispatcher != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        item.ImageBitmap = bi;
                        item.IsImageLoading = false;
                    });
                }
                else
                {
                    item.ImageBitmap = bi;
                    item.IsImageLoading = false;
                }
                return;
            }
        }
        catch { }

        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                item.IsImageLoading = false;
            });
        }
        else
        {
            item.IsImageLoading = false;
        }
    }

    public static string? ExtractImageUrl(XElement item, string description)
    {
        // 1. Check media:content (The Hindu, Indian Express, Reuters, etc.)
        var mediaContent = item.Descendants().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "content", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Attribute("url")?.Value ?? e.Attribute("URL")?.Value ?? e.Attribute("src")?.Value));
        if (mediaContent != null)
        {
            string url = (mediaContent.Attribute("url")?.Value ?? mediaContent.Attribute("URL")?.Value ?? mediaContent.Attribute("src")?.Value)!.Trim();
            if (IsValidHttpUrl(url)) return url;
        }

        // 2. Check media:thumbnail (BBC News, Yahoo, etc.)
        var mediaThumb = item.Descendants().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "thumbnail", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Attribute("url")?.Value ?? e.Attribute("URL")?.Value ?? e.Attribute("src")?.Value));
        if (mediaThumb != null)
        {
            string url = (mediaThumb.Attribute("url")?.Value ?? mediaThumb.Attribute("URL")?.Value ?? mediaThumb.Attribute("src")?.Value)!.Trim();
            if (IsValidHttpUrl(url)) return url;
        }

        // 3. Check enclosure (Times of India, HT, etc.)
        var enclosure = item.Descendants().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "enclosure", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Attribute("url")?.Value ?? e.Attribute("URL")?.Value) &&
            (e.Attribute("type")?.Value?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
             IsImageExtension((e.Attribute("url")?.Value ?? e.Attribute("URL")?.Value)!)));
        if (enclosure != null)
        {
            string url = (enclosure.Attribute("url")?.Value ?? enclosure.Attribute("URL")?.Value)!.Trim();
            if (IsValidHttpUrl(url)) return url;
        }

        // 4. Check <img> tags inside description
        if (!string.IsNullOrWhiteSpace(description))
        {
            string decodedDesc = WebUtility.HtmlDecode(description);
            var match = ImgSrcRegex().Match(decodedDesc);
            if (match.Success)
            {
                string url = match.Groups[1].Value.Trim();
                if (IsValidHttpUrl(url)) return url;
            }
        }

        // 5. Check <content:encoded>
        var encoded = item.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "encoded", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            string decodedEncoded = WebUtility.HtmlDecode(encoded);
            var match = ImgSrcRegex().Match(decodedEncoded);
            if (match.Success)
            {
                string url = match.Groups[1].Value.Trim();
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
               clean.EndsWith(".cms", StringComparison.OrdinalIgnoreCase) ||
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
