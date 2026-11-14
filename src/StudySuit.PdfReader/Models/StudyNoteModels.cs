namespace StudySuit.PdfReader.Models;

public enum StudyChartType
{
    Bar,
    Line,
    Pie,
    Scatter,
    FunctionPlot
}

public sealed class ChartDataPoint
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
    public double XValue { get; set; }
}

public sealed class StudyNoteEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Note";
    public string PreviewText { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public string UpdatedDisplay => UpdatedUtc.ToLocalTime().ToString("MMM dd • HH:mm");
}

