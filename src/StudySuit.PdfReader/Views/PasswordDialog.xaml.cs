using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader.Views;

public partial class PasswordDialog : FluentWindow
{
    public string? EnteredPassword { get; private set; }

    public PasswordDialog(string fileName, bool incorrectPreviousAttempt)
    {
        InitializeComponent();
        PromptLabel.Text = $"\"{fileName}\" is password-protected.";
        if (incorrectPreviousAttempt)
        {
            ErrorLabel.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredPassword = PasswordInput.Password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredPassword = null;
        DialogResult = false;
        Close();
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UnlockButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
        }
    }
}

