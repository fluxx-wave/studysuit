using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public static class ChartRenderer
{
    private static readonly Color[] SeriesColors =
    [
        Color.FromRgb(56, 189, 248),  // Sky Blue
        Color.FromRgb(74, 222, 128),  // Mint Green
        Color.FromRgb(251, 113, 133), // Rose Pink
        Color.FromRgb(250, 204, 21),  // Amber Yellow
        Color.FromRgb(192, 132, 252), // Lavender
        Color.FromRgb(249, 115, 22)   // Orange
    ];

    public static BitmapSource RenderDataChart(
        StudyChartType chartType,
        string title,
        string xAxisLabel,
        string yAxisLabel,
        IReadOnlyList<ChartDataPoint> points,
        int width = 640,
        int height = 380)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var bg = CreateBrush(Color.FromRgb(249, 250, 253));
            var borderPen = CreatePen(Color.FromRgb(215, 222, 234), 1.2);
            var textBrush = CreateBrush(Color.FromRgb(26, 30, 42));
            var mutedBrush = CreateBrush(Color.FromRgb(100, 110, 128));
            var gridPen = CreatePen(Color.FromRgb(228, 233, 242), 1.0);
            var axisPen = CreatePen(Color.FromRgb(130, 140, 160), 1.4);

            dc.DrawRoundedRectangle(bg, borderPen, new Rect(0.5, 0.5, width - 1, height - 1), 10, 10);

            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var regularTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            DrawText(dc, string.IsNullOrWhiteSpace(title) ? "Study Chart" : title, 20, 14, 16, typeface, textBrush);

            if (points.Count == 0)
            {
                DrawText(dc, "No data points provided.", width / 2.0 - 70, height / 2.0 - 10, 13, regularTypeface, mutedBrush);
            }
            else if (chartType == StudyChartType.Pie)
            {
                DrawPieChart(dc, points, width, height, regularTypeface, textBrush, mutedBrush);
            }
            else
            {
                var plotRect = new Rect(64, 52, width - 92, height - 104);
                DrawCartesianDataChart(dc, chartType, points, plotRect, xAxisLabel, yAxisLabel, regularTypeface, textBrush, mutedBrush, gridPen, axisPen);
            }
        }

        return RenderVisualToBitmap(visual, width, height);
    }

    public static BitmapSource RenderFunctionGraph(
        string expression,
        string title,
        double xMin,
        double xMax,
        double yMin,
        double yMax,
        int width = 640,
        int height = 380)
    {
        if (xMax <= xMin) xMax = xMin + 10;
        if (yMax <= yMin) yMax = yMin + 10;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var bg = CreateBrush(Color.FromRgb(249, 250, 253));
            var borderPen = CreatePen(Color.FromRgb(215, 222, 234), 1.2);
            var textBrush = CreateBrush(Color.FromRgb(26, 30, 42));
            var mutedBrush = CreateBrush(Color.FromRgb(100, 110, 128));
            var gridPen = CreatePen(Color.FromRgb(226, 232, 240), 1.0);
            var axisPen = CreatePen(Color.FromRgb(95, 105, 125), 1.6);
            var curvePen = CreatePen(Color.FromRgb(37, 99, 235), 2.5);

            dc.DrawRoundedRectangle(bg, borderPen, new Rect(0.5, 0.5, width - 1, height - 1), 10, 10);

            var boldFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var regFace = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            string heading = string.IsNullOrWhiteSpace(title) ? $"y = {expression}" : $"{title}   (y = {expression})";
            DrawText(dc, heading, 20, 14, 15, boldFace, textBrush);

            var plot = new Rect(58, 50, width - 84, height - 88);
            dc.DrawRectangle(Brushes.White, borderPen, plot);

            // Gridlines & tick labels
            const int ticks = 6;
            for (int i = 0; i <= ticks; i++)
            {
                double frac = i / (double)ticks;
                double gx = plot.Left + frac * plot.Width;
                double gy = plot.Bottom - frac * plot.Height;
                dc.DrawLine(gridPen, new Point(gx, plot.Top), new Point(gx, plot.Bottom));
                dc.DrawLine(gridPen, new Point(plot.Left, gy), new Point(plot.Right, gy));

                double xv = xMin + frac * (xMax - xMin);
                double yv = yMin + frac * (yMax - yMin);
                DrawText(dc, xv.ToString("0.##", CultureInfo.InvariantCulture), gx - 12, plot.Bottom + 6, 10.5, regFace, mutedBrush);
                DrawText(dc, yv.ToString("0.##", CultureInfo.InvariantCulture), 12, gy - 7, 10.5, regFace, mutedBrush);
            }

            // Draw X=0 and Y=0 axes if inside bounds
            if (xMin <= 0 && xMax >= 0)
            {
                double x0 = plot.Left + (-xMin / (xMax - xMin)) * plot.Width;
                dc.DrawLine(axisPen, new Point(x0, plot.Top), new Point(x0, plot.Bottom));
            }

            if (yMin <= 0 && yMax >= 0)
            {
                double y0 = plot.Bottom - (-yMin / (yMax - yMin)) * plot.Height;
                dc.DrawLine(axisPen, new Point(plot.Left, y0), new Point(plot.Right, y0));
            }

            // Plot curve clipped to plot rectangle
            dc.PushClip(new RectangleGeometry(plot));
            const int samples = 360;
            var segments = new List<List<Point>>();
            List<Point>? current = null;

            for (int i = 0; i <= samples; i++)
            {
                double t = i / (double)samples;
                double x = xMin + t * (xMax - xMin);
                double y;
                try
                {
                    y = MathExpressionEvaluator.Evaluate(expression, x);
                }
                catch
                {
                    y = double.NaN;
                }

                if (double.IsNaN(y) || double.IsInfinity(y) || Math.Abs(y) > 1e6)
                {
                    current = null;
                    continue;
                }

                double px = plot.Left + t * plot.Width;
                double py = plot.Bottom - ((y - yMin) / (yMax - yMin)) * plot.Height;

                if (current is not null && Math.Abs(py - current[^1].Y) > plot.Height * 1.4)
                {
                    // Break across vertical asymptotes (e.g. tan(x), 1/x)
                    current = null;
                }

                if (current is null)
                {
                    current = [];
                    segments.Add(current);
                }

                current.Add(new Point(px, py));
            }

            foreach (var seg in segments)
            {
                if (seg.Count < 2) continue;
                var figure = new PathFigure { StartPoint = seg[0], IsClosed = false, IsFilled = false };
                figure.Segments.Add(new PolyLineSegment(seg.Skip(1), true));
                var geo = new PathGeometry([figure]);
                geo.Freeze();
                dc.DrawGeometry(null, curvePen, geo);
            }

            dc.Pop();
        }

        return RenderVisualToBitmap(visual, width, height);
    }

    private static void DrawCartesianDataChart(
        DrawingContext dc,
        StudyChartType chartType,
        IReadOnlyList<ChartDataPoint> points,
        Rect plot,
        string xAxisLabel,
        string yAxisLabel,
        Typeface typeface,
        Brush textBrush,
        Brush mutedBrush,
        Pen gridPen,
        Pen axisPen)
    {
        double minVal = Math.Min(0, points.Min(p => p.Value));
        double maxVal = Math.Max(1, points.Max(p => p.Value));
        double range = Math.Max(1e-6, maxVal - minVal);
        maxVal += range * 0.1;

        dc.DrawRectangle(Brushes.White, null, plot);

        const int gridSteps = 5;
        for (int i = 0; i <= gridSteps; i++)
        {
            double frac = i / (double)gridSteps;
            double y = plot.Bottom - frac * plot.Height;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            double v = minVal + frac * (maxVal - minVal);
            DrawText(dc, v.ToString("0.##", CultureInfo.InvariantCulture), 12, y - 7, 10.5, typeface, mutedBrush);
        }

        dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        if (chartType == StudyChartType.Bar)
        {
            double slotW = plot.Width / points.Count;
            double barW = Math.Max(10, Math.Min(52, slotW * 0.62));
            double zeroY = plot.Bottom - ((0 - minVal) / (maxVal - minVal)) * plot.Height;

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                double valY = plot.Bottom - ((pt.Value - minVal) / (maxVal - minVal)) * plot.Height;
                double topY = Math.Min(zeroY, valY);
                double barH = Math.Max(2, Math.Abs(zeroY - valY));
                double x = plot.Left + i * slotW + (slotW - barW) / 2.0;

                var brush = CreateBrush(SeriesColors[i % SeriesColors.Length]);
                dc.DrawRoundedRectangle(brush, null, new Rect(x, topY, barW, barH), 3, 3);
                DrawText(dc, pt.Value.ToString("0.##", CultureInfo.InvariantCulture), x + barW / 2 - 12, topY - 16, 10.5, typeface, textBrush);
                DrawText(dc, Truncate(pt.Label, 10), x + barW / 2 - 18, plot.Bottom + 6, 10.5, typeface, textBrush);
            }
        }
        else if (chartType == StudyChartType.Line)
        {
            var screenPts = new List<Point>(points.Count);
            double stepX = points.Count > 1 ? plot.Width / (points.Count - 1) : plot.Width / 2;

            for (int i = 0; i < points.Count; i++)
            {
                double x = points.Count == 1 ? plot.Left + plot.Width / 2 : plot.Left + i * stepX;
                double y = plot.Bottom - ((points[i].Value - minVal) / (maxVal - minVal)) * plot.Height;
                screenPts.Add(new Point(x, y));
                DrawText(dc, Truncate(points[i].Label, 10), x - 14, plot.Bottom + 6, 10.5, typeface, textBrush);
            }

            if (screenPts.Count >= 2)
            {
                var linePen = CreatePen(SeriesColors[0], 2.6);
                var fig = new PathFigure { StartPoint = screenPts[0], IsClosed = false, IsFilled = false };
                fig.Segments.Add(new PolyLineSegment(screenPts.Skip(1), true));
                var geo = new PathGeometry([fig]);
                geo.Freeze();
                dc.DrawGeometry(null, linePen, geo);
            }

            var dotBrush = CreateBrush(SeriesColors[0]);
            var dotPen = CreatePen(Colors.White, 1.5);
            for (int i = 0; i < screenPts.Count; i++)
            {
                dc.DrawEllipse(dotBrush, dotPen, screenPts[i], 5, 5);
                DrawText(dc, points[i].Value.ToString("0.##", CultureInfo.InvariantCulture), screenPts[i].X - 10, screenPts[i].Y - 18, 10.5, typeface, textBrush);
            }
        }
        else if (chartType == StudyChartType.Scatter)
        {
            double xMin = points.Min(p => p.XValue);
            double xMax = points.Max(p => p.XValue);
            if (Math.Abs(xMax - xMin) < 1e-6) { xMin -= 1; xMax += 1; }

            var dotBrush = CreateBrush(SeriesColors[4]);
            var dotPen = CreatePen(Colors.White, 1.5);
            foreach (var pt in points)
            {
                double x = plot.Left + ((pt.XValue - xMin) / (xMax - xMin)) * plot.Width;
                double y = plot.Bottom - ((pt.Value - minVal) / (maxVal - minVal)) * plot.Height;
                dc.DrawEllipse(dotBrush, dotPen, new Point(x, y), 6, 6);
                if (!string.IsNullOrWhiteSpace(pt.Label))
                {
                    DrawText(dc, Truncate(pt.Label, 10), x + 8, y - 8, 10, typeface, mutedBrush);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(xAxisLabel))
        {
            DrawText(dc, xAxisLabel, plot.Left + plot.Width / 2 - 40, plot.Bottom + 24, 11, typeface, mutedBrush);
        }

        if (!string.IsNullOrWhiteSpace(yAxisLabel))
        {
            DrawText(dc, yAxisLabel, 10, plot.Top - 18, 11, typeface, mutedBrush);
        }
    }

    private static void DrawPieChart(
        DrawingContext dc,
        IReadOnlyList<ChartDataPoint> points,
        int width,
        int height,
        Typeface typeface,
        Brush textBrush,
        Brush mutedBrush)
    {
        double total = points.Sum(p => Math.Max(0, p.Value));
        if (total <= 0)
        {
            DrawText(dc, "Pie chart requires positive values.", width / 2.0 - 90, height / 2.0, 13, typeface, mutedBrush);
            return;
        }

        double radius = Math.Min(width * 0.28, height * 0.34);
        var center = new Point(width * 0.33, height * 0.54);
        double startAngle = -Math.PI / 2.0;

        for (int i = 0; i < points.Count; i++)
        {
            double val = Math.Max(0, points[i].Value);
            double sweep = (val / total) * Math.PI * 2.0;
            if (sweep <= 0) continue;

            var color = SeriesColors[i % SeriesColors.Length];
            var sliceBrush = CreateBrush(color);

            if (sweep >= Math.PI * 1.999)
            {
                dc.DrawEllipse(sliceBrush, CreatePen(Colors.White, 1.5), center, radius, radius);
            }
            else
            {
                double endAngle = startAngle + sweep;
                var p1 = new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle));
                var p2 = new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle));

                var fig = new PathFigure { StartPoint = center, IsClosed = true, IsFilled = true };
                fig.Segments.Add(new LineSegment(p1, true));
                fig.Segments.Add(new ArcSegment(p2, new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise, true));
                var geo = new PathGeometry([fig]);
                geo.Freeze();
                dc.DrawGeometry(sliceBrush, CreatePen(Colors.White, 1.5), geo);
            }

            // Legend entry
            double lx = width * 0.62;
            double ly = 72 + i * 26;
            if (ly < height - 24)
            {
                dc.DrawRoundedRectangle(sliceBrush, null, new Rect(lx, ly + 2, 14, 14), 3, 3);
                double pct = (val / total) * 100.0;
                DrawText(dc, $"{Truncate(points[i].Label, 14)} ({pct:0.#}%)", lx + 22, ly, 12, typeface, textBrush);
            }

            startAngle += sweep;
        }
    }

    private static BitmapSource RenderVisualToBitmap(DrawingVisual visual, int width, int height)
    {
        const double scale = 2.0;
        var rtb = new RenderTargetBitmap(
            (int)(width * scale),
            (int)(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Typeface typeface, Brush brush)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            1.0);
        dc.DrawText(ft, new Point(x, y));
    }

    private static SolidColorBrush CreateBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen CreatePen(Color c, double thickness)
    {
        var p = new Pen(CreateBrush(c), thickness);
        p.Freeze();
        return p;
    }

    private static string Truncate(string s, int max) => s.Length > max ? $"{s[..max]}…" : s;
}

