using System.IO;
using System.Windows;
using StudySuit.PdfReader.Services;
using StudySuit.PdfReader.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var pdfService = new PdfService();
        var recentFilesService = new RecentFilesService();
        var annotationService = new AnnotationService();
        var studyPlannerService = new StudyPlannerService();
        _mainViewModel = new MainViewModel(pdfService, recentFilesService, annotationService, studyPlannerService);

        ApplyTheme(_mainViewModel.IsDarkTheme);
        _mainViewModel.ApplyThemeAction = ApplyTheme;

        var mainWindow = new MainWindow(_mainViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();

        // Open any PDF file passed on the command line (e.g., "Open with" or CLI).
        foreach (string arg in e.Args)
        {
            if (File.Exists(arg) && string.Equals(Path.GetExtension(arg), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await _mainViewModel.OpenFileAsync(arg);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }

    private static void ApplyTheme(bool isDark)
    {
        ApplicationTheme target = isDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(target, WindowBackdropType.Mica, updateAccent: true);

        if (Current?.MainWindow is MainWindow mw)
        {
            WindowBackgroundManager.UpdateBackground(mw, target, WindowBackdropType.Mica);
            mw.AnimateThemeTransition();
        }
    }
}
