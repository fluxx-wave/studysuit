using System.Collections.ObjectModel;

namespace StudySuit.PdfReader.Models;

public enum PdfViewMode
{
    Continuous,
    SinglePage
}

public enum PdfFitMode
{
    Custom,
    FitWidth,
    FitPage
}

public sealed record PdfPageSize(double WidthPoints, double HeightPoints);

public sealed record NormalizedRect(double X, double Y, double Width, double Height)
{
    public bool Intersects(NormalizedRect other)
    {
        return X < other.X + other.Width &&
               X + Width > other.X &&
               Y < other.Y + other.Height &&
               Y + Height > other.Y;
    }

    /// <summary>
    /// Transforms a normalized [0..1] rectangle from 0-degree page space into clockwise-rotated page space.
    /// </summary>
    public NormalizedRect Rotate(int rotationDegrees)
    {
        int normRot = ((rotationDegrees % 360) + 360) % 360;
        return normRot switch
        {
            90 => new NormalizedRect(1.0 - (Y + Height), X, Height, Width),
            180 => new NormalizedRect(1.0 - (X + Width), 1.0 - (Y + Height), Width, Height),
            270 => new NormalizedRect(Y, 1.0 - (X + Width), Height, Width),
            _ => this
        };
    }

    /// <summary>
    /// Inverse-transforms a normalized [0..1] rectangle from rotated display space back to 0-degree page space.
    /// </summary>
    public NormalizedRect Unrotate(int rotationDegrees)
    {
        int normRot = ((rotationDegrees % 360) + 360) % 360;
        int inverse = (360 - normRot) % 360;
        return Rotate(inverse);
    }
}

public sealed record PdfWordBox(string Text, NormalizedRect Bounds);

public sealed class PdfBookmarkItem
{
    public string Title { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public ObservableCollection<PdfBookmarkItem> Children { get; } = [];
}

public sealed class PdfDocumentInfo
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Creator { get; init; } = string.Empty;
    public string Producer { get; init; } = string.Empty;
    public string PdfVersion { get; init; } = string.Empty;
    public string CreationDate { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public int PageCount { get; init; }
    public IReadOnlyList<PdfPageSize> PageSizes { get; init; } = [];
    public IReadOnlyList<PdfBookmarkItem> Bookmarks { get; init; } = [];
}

public sealed class SearchMatch
{
    public required int MatchIndex { get; init; }
    public required int PageNumber { get; init; }
    public required string Snippet { get; init; }
    public required IReadOnlyList<NormalizedRect> Rects { get; init; }
}

public sealed class RecentFileItem
{
    public required string FilePath { get; set; }
    public required string FileName { get; set; }
    public DateTime LastOpenedUtc { get; set; }
    public int LastPage { get; set; } = 1;
    public int PageCount { get; set; }
    public bool IsPinned { get; set; }
    public int AnnotationCount { get; set; }

    public double ProgressPercent => PageCount > 0
        ? Math.Clamp(Math.Round(LastPage * 100.0 / PageCount, 0), 0, 100)
        : 0;

    public string ProgressDisplay => PageCount > 0
        ? $"Page {LastPage} of {PageCount} ({ProgressPercent:0}%)"
        : $"Page {LastPage}";

    public string LastOpenedDisplay => LastOpenedUtc.ToLocalTime().ToString("MMM dd, yyyy • HH:mm");
}

