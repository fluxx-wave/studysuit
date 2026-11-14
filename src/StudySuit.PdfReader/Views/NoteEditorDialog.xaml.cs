using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader.Views;

public partial class NoteEditorDialog : FluentWindow
{
    public string EnteredText { get; private set; } = string.Empty;
    public bool DeleteRequested { get; private set; }

    public NoteEditorDialog(string title, string subtitle, string initialText = "", bool allowDelete = false)
    {
        InitializeComponent();
        DialogTitleBar.Title = title;
        SubtitleLabel.Text = subtitle;
        NoteInputBox.Text = initialText;
        DeleteButton.Visibility = allowDelete ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            NoteInputBox.Focus();
            NoteInputBox.CaretIndex = NoteInputBox.Text.Length;
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelButton_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                SaveButton_Click(this, new RoutedEventArgs());
            }
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredText = NoteInputBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

