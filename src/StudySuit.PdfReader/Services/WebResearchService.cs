using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IWebResearchService
{
    Task<IReadOnlyList<WebSourceReference>> SearchAsync(
        string query,
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebImageReference>> SearchImagesAsync(
        string query,
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed partial class WebResearchService : IWebResearchService
{
    private static readonly Regex CutoffYearRegex = new(@"\b(202[0-4]|201\d)\b", RegexOptions.Compiled);
    private static readonly Regex AnyFourDigitYearRegex = new(@"\b(19\d\d|20\d\d)\b", RegexOptions.Compiled);
    private static readonly Regex RecencyTriggerRegex = new(
        @"\b(latest|current|recent|recently|today|tonight|now|this\s+year|upcoming|newest|updates?|present|live|ongoing)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NewsOrAffairsRegex = new(
        @"\b(current\s+affairs|latest\s+news|today'?s\s+news|daily\s+news|breaking\s+news|annual\s+budget|union\s+budget)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public WebResearchService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? CreateDefaultClient();
    }

    public static string SanitizeAndGroundTemporalQuery(string rawQuery, DateTime? customNow = null)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return string.Empty;
        }

        DateTime now = customNow ?? DateTime.Now;
        string query = rawQuery.Trim();
        bool hasRecency = RecencyTriggerRegex.IsMatch(query) || NewsOrAffairsRegex.IsMatch(query);

        if (hasRecency && CutoffYearRegex.IsMatch(query))
        {
            query = CutoffYearRegex.Replace(query, now.Year.ToString());
        }
        else if (Regex.IsMatch(query, @"\b(2023|2024)\b") &&
                 Regex.IsMatch(query, @"\b(affairs|news|budget|prelims|syllabus|election|scheme|updates?)\b", RegexOptions.IgnoreCase))
        {
            query = Regex.Replace(query, @"\b(2023|2024)\b", now.Year.ToString());
        }
        else if (hasRecency && !AnyFourDigitYearRegex.IsMatch(query))
        {
            query = $"{query} {now.Year}".Trim();
        }

        return query;
    }

    public async Task<IReadOnlyList<WebSourceReference>> SearchAsync(
        string query,
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string effectiveQuery = SanitizeAndGroundTemporalQuery(query);
        int maxResults = Math.Clamp(settings.MaxWebResults, 1, 8);
        if (settings.WebSearchProvider == WebSearchProviderKind.Tavily &&
            !string.IsNullOrWhiteSpace(settings.WebSearchApiKey))
        {
            var tavily = await SearchTavilyAsync(effectiveQuery, settings.WebSearchApiKey, maxResults, cancellationToken);
            if (tavily.Count > 0)
            {
                return tavily;
            }
        }

        var results = await SearchDuckDuckGoAsync(effectiveQuery, maxResults, cancellationToken);
        if (results.Count < 2)
        {
            var wiki = await SearchWikipediaAsync(effectiveQuery, maxResults - results.Count, cancellationToken);
            foreach (var item in wiki)
            {
                if (!results.Any(r => string.Equals(r.Url, item.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new WebSourceReference
                    {
                        Index = results.Count + 1,
                        Title = item.Title,
                        Url = item.Url,
                        Snippet = item.Snippet
                    });
                }
            }
        }

        return results;
    }

    private async Task<List<WebSourceReference>> SearchTavilyAsync(
        string query,
        string apiKey,
        int maxResults,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                api_key = apiKey,
                query,
                max_results = maxResults,
                include_answer = false
            };

            using var resp = await _http.PostAsJsonAsync("https://api.tavily.com/search", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return [];
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<WebSourceReference>();
            foreach (var item in arr.EnumerateArray())
            {
                string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                string url = item.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                string snippet = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    list.Add(new WebSourceReference
                    {
                        Index = list.Count + 1,
                        Title = string.IsNullOrWhiteSpace(title) ? url : title,
                        Url = url,
                        Snippet = Truncate(snippet.Trim(), 500)
                    });
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private async Task<List<WebSourceReference>> SearchDuckDuckGoAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        try
        {
            string url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) StudyMateAI/1.0");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return [];
            }

            string html = await resp.Content.ReadAsStringAsync(ct);
            return ParseDuckDuckGoHtml(html, maxResults);
        }
        catch
        {
            return [];
        }
    }

    internal static List<WebSourceReference> ParseDuckDuckGoHtml(string html, int maxResults)
    {
        var results = new List<WebSourceReference>();
        var linkMatches = DdgLinkRegex().Matches(html);
        var snippetMatches = DdgSnippetRegex().Matches(html);

        for (int i = 0; i < linkMatches.Count && results.Count < maxResults; i++)
        {
            string rawHref = WebUtility.HtmlDecode(linkMatches[i].Groups[1].Value);
            string resolvedUrl = DecodeDuckDuckGoUrl(rawHref);
            if (string.IsNullOrWhiteSpace(resolvedUrl) ||
                !resolvedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string title = CleanHtmlText(linkMatches[i].Groups[2].Value);
            string snippet = i < snippetMatches.Count
                ? CleanHtmlText(snippetMatches[i].Groups[1].Value)
                : string.Empty;

            results.Add(new WebSourceReference
            {
                Index = results.Count + 1,
                Title = string.IsNullOrWhiteSpace(title) ? resolvedUrl : title,
                Url = resolvedUrl,
                Snippet = Truncate(snippet, 420)
            });
        }

        return results;
    }

    private async Task<List<WebSourceReference>> SearchWikipediaAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        try
        {
            // Primary: Generator search with pageimages (authentic high-res thumbnails) and extracts:
            string url = $"https://en.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(query)}&gsrlimit={maxResults}&prop=pageimages|extracts&piprop=thumbnail&pithumbsize=600&exintro=1&explaintext=1&exsentences=3&format=json&utf8=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "StudyMateAI/1.0 (https://localhost)");

            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("query", out var qObj) &&
                    qObj.TryGetProperty("pages", out var pagesObj) &&
                    pagesObj.ValueKind == JsonValueKind.Object)
                {
                    var list = new List<WebSourceReference>();
                    foreach (var prop in pagesObj.EnumerateObject())
                    {
                        var page = prop.Value;
                        string title = page.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                        string extract = page.TryGetProperty("extract", out var e) ? e.GetString() ?? string.Empty : string.Empty;
                        string thumbUrl = string.Empty;
                        if (page.TryGetProperty("thumbnail", out var thumb) && thumb.TryGetProperty("source", out var src))
                        {
                            thumbUrl = src.GetString() ?? string.Empty;
                        }

                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            string snippet = extract;
                            if (!string.IsNullOrWhiteSpace(thumbUrl))
                            {
                                snippet = $"[Authentic Image URL: {thumbUrl}]\n{snippet}".Trim();
                            }

                            list.Add(new WebSourceReference
                            {
                                Index = list.Count + 1,
                                Title = $"{title} - Wikipedia",
                                Url = $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}",
                                Snippet = Truncate(snippet, 520)
                            });
                        }
                    }

                    if (list.Count > 0)
                    {
                        return list;
                    }
                }
            }

            // Fallback to standard list=search if generator returned no results:
            string fallbackUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&srlimit={maxResults}&format=json&utf8=1";
            using var fallbackReq = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
            fallbackReq.Headers.TryAddWithoutValidation("User-Agent", "StudySuitPdfReader/1.0 (https://localhost)");
            using var fallbackResp = await _http.SendAsync(fallbackReq, ct);
            if (!fallbackResp.IsSuccessStatusCode) return [];

            using var fallbackDoc = await JsonDocument.ParseAsync(await fallbackResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!fallbackDoc.RootElement.TryGetProperty("query", out var fqObj) ||
                !fqObj.TryGetProperty("search", out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var fallbackList = new List<WebSourceReference>();
            foreach (var item in arr.EnumerateArray())
            {
                string title = item.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                string snippet = item.TryGetProperty("snippet", out var s) ? CleanHtmlText(s.GetString() ?? string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    fallbackList.Add(new WebSourceReference
                    {
                        Index = fallbackList.Count + 1,
                        Title = $"{title} - Wikipedia",
                        Url = $"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}",
                        Snippet = Truncate(snippet, 420)
                    });
                }
            }

            return fallbackList;
        }
        catch
        {
            return [];
        }
    }

    private static string DecodeDuckDuckGoUrl(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            href = "https:" + href;
        }

        int uddgIdx = href.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase);
        if (uddgIdx >= 0)
        {
            string encoded = href[(uddgIdx + 5)..];
            int amp = encoded.IndexOf('&');
            if (amp >= 0)
            {
                encoded = encoded[..amp];
            }

            return Uri.UnescapeDataString(encoded);
        }

        return href;
    }

    private static string CleanHtmlText(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string noTags = HtmlTagRegex().Replace(raw, " ");
        string decoded = WebUtility.HtmlDecode(noTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    public async Task<IReadOnlyList<WebImageReference>> SearchImagesAsync(
        string query,
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string effectiveQuery = SanitizeAndGroundTemporalQuery(query);
        int maxResults = Math.Clamp(settings.MaxWebResults, 2, 8);

        // 1. If Tavily is configured with an API key, try Tavily image search first
        if (settings.WebSearchProvider == WebSearchProviderKind.Tavily &&
            !string.IsNullOrWhiteSpace(settings.WebSearchApiKey))
        {
            var tavilyImages = await SearchTavilyImagesAsync(effectiveQuery, settings.WebSearchApiKey, maxResults, cancellationToken).ConfigureAwait(false);
            if (tavilyImages.Count > 0)
            {
                return tavilyImages;
            }
        }

        // 2. High-speed direct web & Google Images search (returns direct image links with full metadata)
        var list = await SearchWebImagesAsync(effectiveQuery, maxResults, cancellationToken).ConfigureAwait(false);
        if (list.Count >= 2)
        {
            return list;
        }

        // 3. Educational diagrams, science figures & maps from Wikimedia Commons
        var wikiImages = await SearchWikimediaImagesAsync(effectiveQuery, maxResults - list.Count, cancellationToken).ConfigureAwait(false);
        foreach (var img in wikiImages)
        {
            if (!list.Any(i => string.Equals(i.ImageUrl, img.ImageUrl, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new WebImageReference
                {
                    Index = list.Count + 1,
                    Title = img.Title,
                    ImageUrl = img.ImageUrl,
                    SourcePageUrl = img.SourcePageUrl
                });
            }
        }

        return list;
    }

    private async Task<List<WebImageReference>> SearchWebImagesAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        var results = new List<WebImageReference>();
        try
        {
            string url = $"https://www.bing.com/images/async?q={Uri.EscapeDataString(query)}&first=1&count={Math.Max(maxResults * 2, 12)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return results;
            }

            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var matches = BingImageCardRegex().Matches(html);

            foreach (Match m in matches)
            {
                if (results.Count >= maxResults) break;

                try
                {
                    string jsonStr = WebUtility.HtmlDecode(m.Groups[1].Value);
                    using var doc = JsonDocument.Parse(jsonStr);
                    var root = doc.RootElement;

                    string murl = root.TryGetProperty("murl", out var mu) ? mu.GetString() ?? string.Empty : string.Empty;
                    string turl = root.TryGetProperty("turl", out var tu) ? tu.GetString() ?? string.Empty : string.Empty;
                    string title = root.TryGetProperty("t", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    string purl = root.TryGetProperty("purl", out var pu) ? pu.GetString() ?? string.Empty : string.Empty;

                    // Prefer reliable CDN image: if murl is unsecure, paywalled, or from a known hotlink-blocking site
                    // (e.g. freepik, alamy, shutterstock, pinterest), swap to Microsoft Bing CDN (turl) which never blocks.
                    string effectiveImageUrl = murl;
                    if (string.IsNullOrWhiteSpace(effectiveImageUrl) ||
                        !effectiveImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        IsBlockedImageHost(effectiveImageUrl))
                    {
                        if (!string.IsNullOrWhiteSpace(turl))
                        {
                            effectiveImageUrl = turl.Contains('?') ? $"{turl}&w=800" : $"{turl}?w=800";
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(effectiveImageUrl) &&
                        effectiveImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                        !results.Any(r => string.Equals(r.ImageUrl, effectiveImageUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        title = CleanHtmlText(title.Replace("&quot;", "\""));
                        results.Add(new WebImageReference
                        {
                            Index = results.Count + 1,
                            Title = string.IsNullOrWhiteSpace(title) ? query : title,
                            ImageUrl = effectiveImageUrl,
                            SourcePageUrl = purl
                        });
                    }
                }
                catch
                {
                    // Ignore parse errors on individual cards
                }
            }
        }
        catch
        {
            // Safeguard against network timeouts
        }

        return results;
    }

    private async Task<List<WebImageReference>> SearchWikimediaImagesAsync(
        string query,
        int maxResults,
        CancellationToken ct)
    {
        var results = new List<WebImageReference>();
        if (maxResults <= 0) return results;

        try
        {
            string url = $"https://commons.wikimedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(query)}&gsrnamespace=6&prop=imageinfo&iiprop=url|size|mime&iiurlwidth=800&format=json&utf8=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "StudyMateAI/1.0 (https://localhost)");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return results;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("query", out var qObj) &&
                qObj.TryGetProperty("pages", out var pagesObj) &&
                pagesObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in pagesObj.EnumerateObject())
                {
                    if (results.Count >= maxResults) break;

                    var page = prop.Value;
                    string rawTitle = page.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    if (rawTitle.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
                    {
                        rawTitle = rawTitle[5..].Trim();
                    }

                    if (page.TryGetProperty("imageinfo", out var infoArr) &&
                        infoArr.ValueKind == JsonValueKind.Array &&
                        infoArr.GetArrayLength() > 0)
                    {
                        var first = infoArr[0];
                        string thumbUrl = first.TryGetProperty("thumburl", out var tu) ? tu.GetString() ?? string.Empty : string.Empty;
                        string originalUrl = first.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;

                        string effectiveUrl = !string.IsNullOrWhiteSpace(thumbUrl) ? thumbUrl : originalUrl;
                        if (!string.IsNullOrWhiteSpace(effectiveUrl) &&
                            effectiveUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                            !results.Any(r => string.Equals(r.ImageUrl, effectiveUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new WebImageReference
                            {
                                Index = results.Count + 1,
                                Title = string.IsNullOrWhiteSpace(rawTitle) ? query : rawTitle,
                                ImageUrl = effectiveUrl,
                                SourcePageUrl = originalUrl
                            });
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return results;
    }

    private async Task<List<WebImageReference>> SearchTavilyImagesAsync(
        string query,
        string apiKey,
        int maxResults,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                api_key = apiKey,
                query,
                max_results = maxResults,
                include_images = true
            };

            using var resp = await _http.PostAsJsonAsync("https://api.tavily.com/search", payload, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return [];

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("images", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<WebImageReference>();
            foreach (var item in arr.EnumerateArray())
            {
                string url = item.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new WebImageReference
                    {
                        Index = list.Count + 1,
                        Title = $"{query} (Visual {list.Count + 1})",
                        ImageUrl = url,
                        SourcePageUrl = url
                    });
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static string Truncate(string s, int max) => s.Length > max ? $"{s[..max]}…" : s;

    public static bool IsBlockedImageHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        string lower = url.ToLowerInvariant();
        return lower.Contains("freepik.com") ||
               lower.Contains("alamy.com") ||
               lower.Contains("shutterstock.com") ||
               lower.Contains("istockphoto.com") ||
               lower.Contains("gettyimages.com") ||
               lower.Contains("dreamstime.com") ||
               lower.Contains("123rf.com") ||
               lower.Contains("stock.adobe.com") ||
               lower.Contains("depositphotos.com") ||
               lower.Contains("pinterest.com") ||
               lower.Contains("pinimg.com") ||
               lower.Contains("vectorstock.com");
    }

    private static HttpClient CreateDefaultClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    [GeneratedRegex(@"class=""iusc""[^>]*m=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex BingImageCardRegex();

    [GeneratedRegex(@"<a[^>]*class=""[^""]*result__a[^""]*""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DdgLinkRegex();

    [GeneratedRegex(@"<a[^>]*class=""[^""]*result__snippet[^""]*""[^>]*>(.*?)</a>|<div[^>]*class=""[^""]*result__snippet[^""]*""[^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DdgSnippetRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

