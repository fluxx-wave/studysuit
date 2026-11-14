using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader.Views;

public partial class ChartBuilderDialog : FluentWindow
{
    private bool _isReady;

    public BitmapSource? RenderedChart { get; private set; }

    public ChartBuilderDialog(StudyChartType initialType = StudyChartType.Bar)
    {
        InitializeComponent();
        DataRowsBox.Text = "Mon, 2.5\nTue, 3.2\nWed, 1.8\nThu, 4.0\nFri, 3.5";

        Loaded += (_, _) =>
        {
            _isReady = true;
            SelectChartType(initialType);
            UpdatePreview();
        };
    }

    private void SelectChartType(StudyChartType type)
    {
        foreach (ComboBoxItem item in ChartTypeBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), type.ToString(), StringComparison.Ordinal))
            {
                ChartTypeBox.SelectedItem = item;
                break;
            }
        }
    }

    private StudyChartType GetSelectedChartType()
    {
        if (ChartTypeBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<StudyChartType>(item.Tag?.ToString(), out var parsed))
        {
            return parsed;
        }

        return StudyChartType.Bar;
    }

    private void ChartTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady)
        {
            return;
        }

        var type = GetSelectedChartType();
        bool isFn = type == StudyChartType.FunctionPlot;
        DataPanel.Visibility = isFn ? Visibility.Collapsed : Visibility.Visible;
        FunctionPanel.Visibility = isFn ? Visibility.Visible : Visibility.Collapsed;

        if (isFn && TitleBox.Text == "Weekly Study Hours")
        {
            TitleBox.Text = "Trigonometric Wave";
        }

        UpdatePreview();
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (_isReady)
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        try
        {
            var type = GetSelectedChartType();
            if (type == StudyChartType.FunctionPlot)
            {
                double xMin = ParseDouble(XMinBox.Text, -10);
                double xMax = ParseDouble(XMaxBox.Text, 10);
                double yMin = ParseDouble(YMinBox.Text, -5);
                double yMax = ParseDouble(YMaxBox.Text, 5);
                RenderedChart = ChartRenderer.RenderFunctionGraph(
                    FunctionExprBox.Text,
                    TitleBox.Text,
                    xMin,
                    xMax,
                    yMin,
                    yMax);
            }
            else
            {
                var points = ParseDataRows(DataRowsBox.Text);
                RenderedChart = ChartRenderer.RenderDataChart(
                    type,
                    TitleBox.Text,
                    XLabelBox.Text,
                    YLabelBox.Text,
                    points);
            }

            PreviewImage.Source = RenderedChart;
        }
        catch
        {
        }
    }

    private static List<ChartDataPoint> ParseDataRows(string raw)
    {
        var list = new List<ChartDataPoint>();
        var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string[] parts = lines[i].Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3 &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double xv) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double yv))
            {
                list.Add(new ChartDataPoint { Label = parts[0], XValue = xv, Value = yv });
            }
            else if (parts.Length >= 2 &&
                     double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                list.Add(new ChartDataPoint { Label = parts[0], XValue = i + 1, Value = val });
            }
        }

        return list;
    }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

