using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace StudySuit.PdfReader.Services;

public interface IWebpageFetchService
{
    Task<string> FetchPageContentAsync(string url, CancellationToken ct = default);
}

public sealed class WebpageFetchService : IWebpageFetchService
{
    private readonly HttpClient _http;

    public WebpageFetchService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        }
        if (!_http.DefaultRequestHeaders.Contains("Accept"))
        {
            _http.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }
        if (!_http.DefaultRequestHeaders.Contains("Accept-Language"))
        {
            _http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,hi;q=0.8");
        }
    }

    public async Task<string> FetchPageContentAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Error: Empty or invalid URL provided.";
        }

        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return $"Error fetching webpage: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
            }

            string html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
            {
                return "The webpage returned empty content.";
            }

            return CleanAndExtractReadableText(html, url);
        }
        catch (OperationCanceledException)
        {
            return "Webpage fetching timed out or was cancelled.";
        }
        catch (Exception ex)
        {
            return $"Failed to fetch webpage content: {ex.Message}";
        }
    }

    private static string CleanAndExtractReadableText(string html, string url)
    {
        var sb = new StringBuilder();

        // Extract title
        string title = "Untitled Page";
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (titleMatch.Success)
        {
            title = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
        }

        sb.AppendLine($"# {title}");
        sb.AppendLine($"Source URL: {url}");
        sb.AppendLine();

        // Extract meta description
        var descMatch = Regex.Match(html, @"<meta\s+name=[""']description[""']\s+content=[""'](.*?)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (descMatch.Success)
        {
            string desc = System.Net.WebUtility.HtmlDecode(descMatch.Groups[1].Value.Trim());
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine($"> **Summary**: {desc}");
                sb.AppendLine();
            }
        }

        // Remove script, style, svg, noscript, nav, header, footer, iframe tags
        string cleaned = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<svg[^>]*>[\s\S]*?</svg>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<noscript[^>]*>[\s\S]*?</noscript>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<footer[^>]*>[\s\S]*?</footer>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<nav[^>]*>[\s\S]*?</nav>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<!--[\s\S]*?-->", "");

        // Convert common block elements to markdown
        cleaned = Regex.Replace(cleaned, @"<h1[^>]*>(.*?)</h1>", "\n\n# $1\n\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<h2[^>]*>(.*?)</h2>", "\n\n## $1\n\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<h3[^>]*>(.*?)</h3>", "\n\n### $1\n\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<h[4-6][^>]*>(.*?)</h[4-6]>", "\n\n#### $1\n\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<li[^>]*>(.*?)</li>", "\n- $1", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<p[^>]*>(.*?)</p>", "\n\n$1\n\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<blockquote[^>]*>(.*?)</blockquote>", "\n> $1\n", RegexOptions.IgnoreCase);

        // Strip remaining HTML tags
        cleaned = Regex.Replace(cleaned, @"<[^>]+>", " ");

        // Decode HTML entities
        cleaned = System.Net.WebUtility.HtmlDecode(cleaned);

        // Normalize whitespaces and blank lines
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");

        string content = cleaned.Trim();

        // Limit content to ~10,000 characters to keep context clean and avoid exceeding model token budgets
        const int maxChars = 10000;
        if (content.Length > maxChars)
        {
            content = content[..maxChars] + "\n\n[...Content truncated for readability...]";
        }

        sb.AppendLine(content);
        return sb.ToString().Trim();
    }
}
