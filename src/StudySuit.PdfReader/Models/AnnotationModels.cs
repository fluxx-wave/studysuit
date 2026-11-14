namespace StudySuit.PdfReader.Models;

public enum AnnotationTool
{
    Pan,
    SelectText,
    Highlight,
    Underline,
    Strikethrough,
    Brush,
    Ink,
    Rectangle,
    Ellipse,
    Arrow,
    StickyNote,
    TextBox,
    Eraser
}

public enum AnnotationType
{
    Highlight,
    Underline,
    Strikethrough,
    Brush,
    Ink,
    Rectangle,
    Ellipse,
    Arrow,
    StickyNote,
    TextBox
}

public sealed record NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Rotate(int rotationDegrees)
    {
        int normRot = ((rotationDegrees % 360) + 360) % 360;
        return normRot switch
        {
            90 => new NormalizedPoint(1.0 - Y, X),
            180 => new NormalizedPoint(1.0 - X, 1.0 - Y),
            270 => new NormalizedPoint(Y, 1.0 - X),
            _ => this
        };
    }

    public NormalizedPoint Unrotate(int rotationDegrees)
    {
        int normRot = ((rotationDegrees % 360) + 360) % 360;
        int inverse = (360 - normRot) % 360;
        return Rotate(inverse);
    }
}

public sealed class PdfAnnotation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public int PageNumber { get; init; }
    public AnnotationType Type { get; init; }
    public string ColorHex { get; set; } = "#FFFFE600";
    public double StrokeThickness { get; set; } = 3.0;
    public double FontSize { get; set; } = 14.0;
    public List<NormalizedRect> Rects { get; set; } = [];
    public List<NormalizedPoint> Points { get; set; } = [];
    public string Text { get; set; } = string.Empty;
    public string NoteComment { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public string TypeDisplay => Type switch
    {
        AnnotationType.Highlight => "Highlight",
        AnnotationType.Underline => "Underline",
        AnnotationType.Strikethrough => "Strikethrough",
        AnnotationType.Brush => "Paint Brush",
        AnnotationType.Ink => "Ink Drawing",
        AnnotationType.Rectangle => "Rectangle",
        AnnotationType.Ellipse => "Ellipse",
        AnnotationType.Arrow => "Arrow",
        AnnotationType.StickyNote => "Sticky Note",
        AnnotationType.TextBox => "Text Callout",
        _ => Type.ToString()
    };

    public string SummaryText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NoteComment) && !string.IsNullOrWhiteSpace(Text))
            {
                return $"{Text} — {NoteComment}";
            }

            if (!string.IsNullOrWhiteSpace(NoteComment))
            {
                return NoteComment;
            }

            if (!string.IsNullOrWhiteSpace(Text))
            {
                return Text;
            }

            return $"{TypeDisplay} on page {PageNumber}";
        }
    }

    public PdfAnnotation Clone() => new()
    {
        Id = Id,
        PageNumber = PageNumber,
        Type = Type,
        ColorHex = ColorHex,
        StrokeThickness = StrokeThickness,
        FontSize = FontSize,
        Rects = [.. Rects],
        Points = [.. Points],
        Text = Text,
        NoteComment = NoteComment,
        CreatedUtc = CreatedUtc
    };
}

public sealed class DocumentAnnotationStore
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<PdfAnnotation> Annotations { get; set; } = [];
}

public sealed class RecentAnnotationSummary
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required int PageNumber { get; init; }
    public required AnnotationType Type { get; init; }
    public required string ColorHex { get; init; }
    public required string PreviewText { get; init; }
    public required DateTime CreatedUtc { get; init; }

    public string TypeLabel => Type switch
    {
        AnnotationType.Highlight => "Highlight",
        AnnotationType.Underline => "Underline",
        AnnotationType.Strikethrough => "Strike",
        AnnotationType.Ink => "Ink",
        AnnotationType.StickyNote => "Note",
        AnnotationType.TextBox => "Text",
        _ => Type.ToString()
    };

    public string TimeDisplay => CreatedUtc.ToLocalTime().ToString("MMM dd • HH:mm");
}

