using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.ViewModels;

public sealed record HighlightRectViewModel(
    double X,
    double Y,
    double Width,
    double Height,
    bool IsActiveMatch);

public sealed class AnnotationVectorVisual
{
    public required string AnnotationId { get; init; }
    public required AnnotationType Type { get; init; }
    public required Geometry Geometry { get; init; }
    public required Brush StrokeBrush { get; init; }
    public required Brush FillBrush { get; init; }
    public required double StrokeThickness { get; init; }
    public required double Opacity { get; init; }
    public required string TooltipText { get; init; }
}

public sealed class AnnotationPinVisual
{
    public required string AnnotationId { get; init; }
    public required AnnotationType Type { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required Brush AccentBrush { get; init; }
    public required string Text { get; init; }
    public required string NoteComment { get; init; }
    public required double FontSize { get; init; }
    public bool IsStickyNote => Type == AnnotationType.StickyNote;
    public bool IsTextBox => Type == AnnotationType.TextBox;
    public bool HasNoteComment => !string.IsNullOrWhiteSpace(NoteComment);
}

public sealed record LiveSelectionRect(
    double X,
    double Y,
    double Width,
    double Height,
    Brush FillBrush);

public sealed partial class PdfPageViewModel : ObservableObject
{
    public int PageNumber { get; }
    public double WidthPoints { get; }
    public double HeightPoints { get; }

    [ObservableProperty]
    private double _displayWidth;

    [ObservableProperty]
    private double _displayHeight;

    [ObservableProperty]
    private BitmapSource? _renderedImage;

    [ObservableProperty]
    private BitmapSource? _thumbnailImage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isCurrentPage;

    public int RenderedDpi { get; set; }
    public int RenderedRotation { get; set; }
    public bool RenderedInverted { get; set; }

    public ObservableCollection<HighlightRectViewModel> HighlightRects { get; } = [];
    public ObservableCollection<LiveSelectionRect> LiveWordSelectionRects { get; } = [];
    public ObservableCollection<AnnotationVectorVisual> VectorAnnotations { get; } = [];
    public ObservableCollection<AnnotationPinVisual> PinAnnotations { get; } = [];

    public PdfPageViewModel(int pageNumber, double widthPoints, double heightPoints)
    {
        PageNumber = pageNumber;
        WidthPoints = Math.Max(1, widthPoints);
        HeightPoints = Math.Max(1, heightPoints);
    }

    public void UpdateDisplaySize(double zoomFactor, int rotationDegrees)
    {
        bool swapped = ((rotationDegrees % 180) + 180) % 180 == 90;
        double effectivePointsW = swapped ? HeightPoints : WidthPoints;
        double effectivePointsH = swapped ? WidthPoints : HeightPoints;

        const double ptsToDip = 96.0 / 72.0;
        DisplayWidth = Math.Max(16, effectivePointsW * ptsToDip * zoomFactor);
        DisplayHeight = Math.Max(16, effectivePointsH * ptsToDip * zoomFactor);
    }

    public void UpdateHighlights(
        IEnumerable<(NormalizedRect Rect, bool IsActive)> pageHighlights,
        int rotationDegrees)
    {
        HighlightRects.Clear();
        foreach (var (rect, isActive) in pageHighlights)
        {
            var rotated = rect.Rotate(rotationDegrees);
            HighlightRects.Add(new HighlightRectViewModel(
                rotated.X * DisplayWidth,
                rotated.Y * DisplayHeight,
                Math.Max(4, rotated.Width * DisplayWidth),
                Math.Max(4, rotated.Height * DisplayHeight),
                isActive));
        }
    }

    public void UpdateLiveWordSelection(
        IEnumerable<NormalizedRect> unrotatedRects,
        int rotationDegrees,
        Brush fillBrush,
        AnnotationType annotationType = AnnotationType.Highlight)
    {
        LiveWordSelectionRects.Clear();
        foreach (var rect in unrotatedRects)
        {
            var rotated = rect.Rotate(rotationDegrees);
            double x = rotated.X * DisplayWidth;
            double y = rotated.Y * DisplayHeight;
            double width = Math.Max(3, rotated.Width * DisplayWidth);
            double height = Math.Max(3, rotated.Height * DisplayHeight);

            if (annotationType == AnnotationType.Underline)
            {
                y = (rotated.Y + rotated.Height * 0.92) * DisplayHeight;
                height = Math.Max(2.5, 3.0);
            }
            else if (annotationType == AnnotationType.Strikethrough)
            {
                y = (rotated.Y + rotated.Height * 0.50) * DisplayHeight;
                height = Math.Max(2.5, 3.0);
            }

            LiveWordSelectionRects.Add(new LiveSelectionRect(
                x,
                y,
                width,
                height,
                fillBrush));
        }
    }

    public void ClearLiveWordSelection()
    {
        if (LiveWordSelectionRects.Count > 0)
        {
            LiveWordSelectionRects.Clear();
        }
    }

    public void UpdateAnnotations(IEnumerable<PdfAnnotation> annotations, int rotationDegrees, double zoomFactor)
    {
        VectorAnnotations.Clear();
        PinAnnotations.Clear();

        foreach (var ann in annotations)
        {
            var brush = CreateFrozenBrush(ann.ColorHex);
            if (ann.Type is AnnotationType.StickyNote or AnnotationType.TextBox)
            {
                var anchor = ann.Rects.Count > 0
                    ? ann.Rects[0].Rotate(rotationDegrees)
                    : ann.Points.Count > 0
                        ? new NormalizedRect(ann.Points[0].Rotate(rotationDegrees).X, ann.Points[0].Rotate(rotationDegrees).Y, 0.2, 0.05)
                        : new NormalizedRect(0.1, 0.1, 0.2, 0.05);

                PinAnnotations.Add(new AnnotationPinVisual
                {
                    AnnotationId = ann.Id,
                    Type = ann.Type,
                    X = anchor.X * DisplayWidth,
                    Y = anchor.Y * DisplayHeight,
                    Width = Math.Max(120, anchor.Width * DisplayWidth),
                    Height = Math.Max(28, anchor.Height * DisplayHeight),
                    AccentBrush = brush,
                    Text = ann.Text,
                    NoteComment = ann.NoteComment,
                    FontSize = Math.Clamp(ann.FontSize * zoomFactor, 9, 48)
                });
                continue;
            }

            Geometry? geometry = BuildVectorGeometry(ann, rotationDegrees, zoomFactor);
            if (geometry is null)
            {
                continue;
            }

            geometry.Freeze();
            bool isFilledHighlight = ann.Type == AnnotationType.Highlight;
            bool isBrush = ann.Type == AnnotationType.Brush;
            double scaledThickness = isBrush
                ? Math.Max(10.0, ann.StrokeThickness * 4.0) * zoomFactor
                : Math.Max(1.2, ann.StrokeThickness * zoomFactor);

            VectorAnnotations.Add(new AnnotationVectorVisual
            {
                AnnotationId = ann.Id,
                Type = ann.Type,
                Geometry = geometry,
                StrokeBrush = isFilledHighlight ? Brushes.Transparent : brush,
                FillBrush = isFilledHighlight ? brush : Brushes.Transparent,
                StrokeThickness = isFilledHighlight ? 0 : scaledThickness,
                Opacity = isFilledHighlight ? 0.38 : isBrush ? 0.42 : 0.92,
                TooltipText = ann.SummaryText
            });
        }
    }

    private Geometry? BuildVectorGeometry(PdfAnnotation ann, int rotationDegrees, double zoomFactor)
    {
        switch (ann.Type)
        {
            case AnnotationType.Highlight:
            {
                if (ann.Rects.Count == 0)
                {
                    return null;
                }

                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                foreach (var nr in ann.Rects)
                {
                    var r = nr.Rotate(rotationDegrees);
                    group.Children.Add(new RectangleGeometry(
                        new Rect(
                            r.X * DisplayWidth,
                            r.Y * DisplayHeight,
                            Math.Max(3, r.Width * DisplayWidth),
                            Math.Max(3, r.Height * DisplayHeight)),
                        2,
                        2));
                }

                return group;
            }

            case AnnotationType.Underline:
            case AnnotationType.Strikethrough:
            {
                if (ann.Rects.Count == 0)
                {
                    if (ann.Points.Count >= 2)
                    {
                        var pts = ann.Points
                            .Select(p => p.Rotate(rotationDegrees))
                            .Select(p => new Point(p.X * DisplayWidth, p.Y * DisplayHeight))
                            .ToList();

                        var figure = new PathFigure { StartPoint = pts[0], IsClosed = false, IsFilled = false };
                        figure.Segments.Add(new PolyLineSegment(pts.Skip(1), isStroked: true));
                        return new PathGeometry([figure]);
                    }

                    return null;
                }

                double yRatio = ann.Type == AnnotationType.Underline ? 0.95 : 0.52;
                var group = new GeometryGroup();
                foreach (var nr in ann.Rects)
                {
                    var r = nr.Rotate(rotationDegrees);
                    double y = (r.Y + r.Height * yRatio) * DisplayHeight;
                    double x1 = r.X * DisplayWidth;
                    double x2 = (r.X + r.Width) * DisplayWidth;
                    group.Children.Add(new LineGeometry(new Point(x1, y), new Point(x2, y)));
                }

                return group;
            }

            case AnnotationType.Rectangle:
            case AnnotationType.Ellipse:
            {
                if (ann.Rects.Count == 0)
                {
                    return null;
                }

                var r = ann.Rects[0].Rotate(rotationDegrees);
                var rect = new Rect(
                    r.X * DisplayWidth,
                    r.Y * DisplayHeight,
                    Math.Max(4, r.Width * DisplayWidth),
                    Math.Max(4, r.Height * DisplayHeight));

                return ann.Type == AnnotationType.Rectangle
                    ? new RectangleGeometry(rect, 3, 3)
                    : new EllipseGeometry(rect);
            }

            case AnnotationType.Brush:
            case AnnotationType.Ink:
            {
                if (ann.Points.Count < 2)
                {
                    return null;
                }

                var pts = ann.Points
                    .Select(p => p.Rotate(rotationDegrees))
                    .Select(p => new Point(p.X * DisplayWidth, p.Y * DisplayHeight))
                    .ToList();

                var figure = new PathFigure { StartPoint = pts[0], IsClosed = false, IsFilled = false };
                figure.Segments.Add(new PolyLineSegment(pts.Skip(1), isStroked: true));
                return new PathGeometry([figure]);
            }

            case AnnotationType.Arrow:
            {
                if (ann.Points.Count < 2)
                {
                    return null;
                }

                var p1Norm = ann.Points[0].Rotate(rotationDegrees);
                var p2Norm = ann.Points[^1].Rotate(rotationDegrees);
                var p1 = new Point(p1Norm.X * DisplayWidth, p1Norm.Y * DisplayHeight);
                var p2 = new Point(p2Norm.X * DisplayWidth, p2Norm.Y * DisplayHeight);

                var shaft = new PathFigure { StartPoint = p1, IsClosed = false, IsFilled = false };
                shaft.Segments.Add(new LineSegment(p2, isStroked: true));

                double angle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                double headLen = Math.Clamp(12.0 * zoomFactor + ann.StrokeThickness, 10, 26);
                const double wingAngle = Math.PI / 6.5;

                var leftWing = new Point(
                    p2.X - headLen * Math.Cos(angle - wingAngle),
                    p2.Y - headLen * Math.Sin(angle - wingAngle));
                var rightWing = new Point(
                    p2.X - headLen * Math.Cos(angle + wingAngle),
                    p2.Y - headLen * Math.Sin(angle + wingAngle));

                var head = new PathFigure { StartPoint = leftWing, IsClosed = false, IsFilled = false };
                head.Segments.Add(new LineSegment(p2, isStroked: true));
                head.Segments.Add(new LineSegment(rightWing, isStroked: true));

                return new PathGeometry([shaft, head]);
            }

            default:
                return null;
        }
    }

    private static SolidColorBrush CreateFrozenBrush(string colorHex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(colorHex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gold;
        }
    }
}
