using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudySuit.PdfReader.Models;

public enum NewsTimeframe
{
    Daily,
    Weekly,
    Monthly
}

public enum NewsCategory
{
    TopStories,
    IndiaPolity,
    Economy,
    WorldIR,
    ScienceTech,
    Environment,
    ExamPrep
}

public sealed record NewsCategoryOption(NewsCategory Category, string Label, string QueryKeywords);

public sealed partial class NewsFeedItem : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Title { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Source { get; init; } = "News";
    public string Url { get; init; } = string.Empty;
    public DateTime PublishedUtc { get; init; } = DateTime.UtcNow;
    public NewsTimeframe Timeframe { get; init; }
    public string CategoryLabel { get; init; } = "Top Stories";

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private string? _fullContent;

    [ObservableProperty]
    private bool _isSpeaking;

    [ObservableProperty]
    private bool _isHero;

    [ObservableProperty]
    private double _cardWidth = 335;

    [ObservableProperty]
    private double _cardHeight = 360;

    [ObservableProperty]
    private ImageSource? _imageBitmap;

    [ObservableProperty]
    private bool _isImageLoading = true;

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool HasBitmap => ImageBitmap != null;
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasFullContent => !string.IsNullOrWhiteSpace(FullContent);

    partial void OnImageUrlChanged(string? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnFullContentChanged(string? value) => OnPropertyChanged(nameof(HasFullContent));
    partial void OnImageBitmapChanged(ImageSource? value) => OnPropertyChanged(nameof(HasBitmap));

    public string PublishedDisplay
    {
        get
        {
            TimeSpan age = DateTime.UtcNow - PublishedUtc;
            if (age.TotalMinutes < 60)
            {
                return $"{Math.Max(1, (int)age.TotalMinutes)}m ago";
            }

            if (age.TotalHours < 24)
            {
                return $"{(int)age.TotalHours}h ago";
            }

            if (age.TotalDays < 7)
            {
                return $"{(int)age.TotalDays}d ago • {PublishedUtc.ToLocalTime():MMM dd}";
            }

            return PublishedUtc.ToLocalTime().ToString("MMM dd, yyyy");
        }
    }
}

public static class NewsFeedCatalog
{
    public static IReadOnlyList<NewsCategoryOption> Categories { get; } =
    [
        new(NewsCategory.TopStories, "Top Stories", "top headlines India world"),
        new(NewsCategory.IndiaPolity, "India & Polity", "India parliament polity supreme court governance"),
        new(NewsCategory.Economy, "Economy & RBI", "Indian economy RBI inflation GDP budget markets"),
        new(NewsCategory.WorldIR, "World & IR", "international relations diplomacy geopolitics summits"),
        new(NewsCategory.ScienceTech, "Science & Tech", "science technology ISRO AI space research"),
        new(NewsCategory.Environment, "Environment", "climate change environment biodiversity energy"),
        new(NewsCategory.ExamPrep, "UPSC / SSC Brief", "current affairs UPSC SSC PIB India policy")
    ];

    public static NewsCategoryOption GetCategory(NewsCategory category) =>
        Categories.FirstOrDefault(c => c.Category == category) ?? Categories[0];

    // High quality direct JPEG images (fm=jpg strictly enforced, NO auto=format to avoid WebP/AVIF decoding issues in WPF)
    private static readonly string[] TopStoriesImages =
    [
        "https://images.unsplash.com/photo-1585829365295-ab7cd400c167?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1504711434969-e33886168f5c?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1495020689067-958852a7765e?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1526470608268-f674ce90ebd4?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1451187580459-43490279c0fa?w=1000&fit=crop&q=80&fm=jpg"
    ];

    private static readonly string[] PolityImages =
    [
        "https://images.unsplash.com/photo-1596176530529-78163a4f7af2?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1589829545856-d10d557cf95f?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1541872703-74c5e44368f9?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1521791136064-7986c2920216?w=1000&fit=crop&q=80&fm=jpg"
    ];

    private static readonly string[] EconomyImages =
    [
        "https://images.unsplash.com/photo-1611974789855-9c2a0a7236a3?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1590283603385-17ffb3a7f29f?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1559526324-4b87b5e36e44?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1628348068343-c6a848d2b6dd?w=1000&fit=crop&q=80&fm=jpg"
    ];

    private static readonly string[] WorldImages =
    [
        "https://images.unsplash.com/photo-1526778548025-fa2f459cd5c1?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1451187580459-43490279c0fa?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1446776811953-b23d57bd21aa?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1488521787991-ed7bbaae773c?w=1000&fit=crop&q=80&fm=jpg"
    ];

    private static readonly string[] ScienceImages =
    [
        "https://images.unsplash.com/photo-1518770660439-4636190af475?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1507668077129-56e32842fceb?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1532094349884-543bc11b234d?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1507413245164-6160d8298b31?w=1000&fit=crop&q=80&fm=jpg"
    ];

    private static readonly string[] EnvironmentImages =
    [
        "https://images.unsplash.com/photo-1470071459604-3b5ec3a7fe05?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1464822759023-fed622ff2c3b?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1509391365360-2e959784a276?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1500534314209-a25ddb2bd429?w=1000&fit=crop&q=80&fm=jpg"
    ];

    private static readonly string[] ExamPrepImages =
    [
        "https://images.unsplash.com/photo-1456513080510-7bf3a84b82f8?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1497633762265-9d179a990aa6?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1434030216411-0b793f4b4173?w=1000&fit=crop&q=80&fm=jpg",
        "https://images.unsplash.com/photo-1503676260728-1c00da094a0b?w=1000&fit=crop&q=80&fm=jpg"
    ];

    public static string GetFallbackImageUrl(NewsCategory category, int index = 0)
    {
        var array = category switch
        {
            NewsCategory.IndiaPolity => PolityImages,
            NewsCategory.Economy => EconomyImages,
            NewsCategory.WorldIR => WorldImages,
            NewsCategory.ScienceTech => ScienceImages,
            NewsCategory.Environment => EnvironmentImages,
            NewsCategory.ExamPrep => ExamPrepImages,
            _ => TopStoriesImages
        };
        int i = Math.Abs(index) % array.Length;
        return array[i];
    }

    public static string GetFallbackImageUrl(string? categoryLabel, int index = 0)
    {
        var match = Categories.FirstOrDefault(c => string.Equals(c.Label, categoryLabel, StringComparison.OrdinalIgnoreCase));
        return GetFallbackImageUrl(match?.Category ?? NewsCategory.TopStories, index);
    }

    public static string GetContextualImageUrl(string title, string? categoryLabel, int index = 0)
    {
        string t = (title ?? "").ToLowerInvariant();

        // 1. Topic keyword matching:
        if (t.Contains("isro") || t.Contains("space") || t.Contains("satellite") || t.Contains("rocket") || t.Contains("moon") || t.Contains("mars"))
            return "https://images.unsplash.com/photo-1517976487502-5f79e51c8810?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("ai") || t.Contains("artificial intelligence") || t.Contains("robot") || t.Contains("chip") || t.Contains("quantum") || t.Contains("tech") || t.Contains("cyber"))
            return "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("court") || t.Contains("judge") || t.Contains("law") || t.Contains("bail") || t.Contains("legal") || t.Contains("justice") || t.Contains("verdict"))
            return "https://images.unsplash.com/photo-1589829545856-d10d557cf95f?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("parliament") || t.Contains("election") || t.Contains("vote") || t.Contains("minister") || t.Contains("govt") || t.Contains("government") || t.Contains("bjp") || t.Contains("congress"))
            return "https://images.unsplash.com/photo-1596176530529-78163a4f7af2?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("rbi") || t.Contains("bank") || t.Contains("inflation") || t.Contains("gdp") || t.Contains("market") || t.Contains("sensex") || t.Contains("rupee") || t.Contains("trade"))
            return "https://images.unsplash.com/photo-1611974789855-9c2a0a7236a3?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("climate") || t.Contains("pollution") || t.Contains("forest") || t.Contains("weather") || t.Contains("cyclone") || t.Contains("solar") || t.Contains("energy"))
            return "https://images.unsplash.com/photo-1470071459604-3b5ec3a7fe05?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("exam") || t.Contains("upsc") || t.Contains("ssc") || t.Contains("education") || t.Contains("student") || t.Contains("school") || t.Contains("college"))
            return "https://images.unsplash.com/photo-1456513080510-7bf3a84b82f8?w=1000&fit=crop&q=80&fm=jpg";
        if (t.Contains("war") || t.Contains("military") || t.Contains("army") || t.Contains("peace") || t.Contains("treaty") || t.Contains("summit") || t.Contains("foreign"))
            return "https://images.unsplash.com/photo-1526778548025-fa2f459cd5c1?w=1000&fit=crop&q=80&fm=jpg";

        // 2. Fall back to varied category images:
        int seed = Math.Abs(title?.GetHashCode() ?? index);
        return GetFallbackImageUrl(categoryLabel, seed + index);
    }
}
