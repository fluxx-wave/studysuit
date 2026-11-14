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

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasFullContent => !string.IsNullOrWhiteSpace(FullContent);

    partial void OnImageUrlChanged(string? value) => OnPropertyChanged(nameof(HasImage));
    partial void OnFullContentChanged(string? value) => OnPropertyChanged(nameof(HasFullContent));

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

    public static string GetFallbackImageUrl(NewsCategory category) => category switch
    {
        NewsCategory.TopStories => "https://images.unsplash.com/photo-1585829365295-ab7cd400c167?w=800&auto=format&fit=crop&q=80",
        NewsCategory.IndiaPolity => "https://images.unsplash.com/photo-1596176530529-78163a4f7af2?w=800&auto=format&fit=crop&q=80",
        NewsCategory.Economy => "https://images.unsplash.com/photo-1611974789855-9c2a0a7236a3?w=800&auto=format&fit=crop&q=80",
        NewsCategory.WorldIR => "https://images.unsplash.com/photo-1526778548025-fa2f459cd5c1?w=800&auto=format&fit=crop&q=80",
        NewsCategory.ScienceTech => "https://images.unsplash.com/photo-1518770660439-4636190af475?w=800&auto=format&fit=crop&q=80",
        NewsCategory.Environment => "https://images.unsplash.com/photo-1470071459604-3b5ec3a7fe05?w=800&auto=format&fit=crop&q=80",
        NewsCategory.ExamPrep => "https://images.unsplash.com/photo-1456513080510-7bf3a84b82f8?w=800&auto=format&fit=crop&q=80",
        _ => "https://images.unsplash.com/photo-1585829365295-ab7cd400c167?w=800&auto=format&fit=crop&q=80"
    };

    public static string GetFallbackImageUrl(string? categoryLabel)
    {
        var match = Categories.FirstOrDefault(c => string.Equals(c.Label, categoryLabel, StringComparison.OrdinalIgnoreCase));
        return GetFallbackImageUrl(match?.Category ?? NewsCategory.TopStories);
    }
}
