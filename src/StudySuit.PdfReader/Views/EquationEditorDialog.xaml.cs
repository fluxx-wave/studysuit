using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using StudySuit.PdfReader.Services;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader.Views;

public partial class EquationEditorDialog : FluentWindow
{
    public BitmapSource? RenderedEquation { get; private set; }
    public string FormulaText => FormulaBox.Text.Trim();

    public EquationEditorDialog(string initialFormula = @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}")
    {
        InitializeComponent();
        FormulaBox.Text = initialFormula;
        Loaded += (_, _) =>
        {
            UpdatePreview();
            FormulaBox.Focus();
            FormulaBox.CaretIndex = FormulaBox.Text.Length;
        };
    }

    private void InsertTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string snippet)
        {
            int caret = FormulaBox.CaretIndex;
            FormulaBox.Text = FormulaBox.Text.Insert(caret, snippet);
            FormulaBox.CaretIndex = caret + snippet.Length;
            FormulaBox.Focus();
        }
    }

    private void FormulaBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        try
        {
            RenderedEquation = MathEquationRenderer.RenderEquation(FormulaBox.Text, fontSize: 26, darkBackground: false);
            PreviewImage.Source = RenderedEquation;
        }
        catch
        {
        }
    }

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

