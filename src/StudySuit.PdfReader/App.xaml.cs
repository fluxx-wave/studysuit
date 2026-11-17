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

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(@"D:\studysuit\debug_startup.log", $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public App()
    {
        Log("App ctor started");
        DispatcherUnhandledException += (s, e) =>
        {
            Log($"DispatcherUnhandledException: {e.Exception}");
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudySuit_crash.log");
            File.WriteAllText(logPath, e.Exception.ToString());
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log($"AppDomain.UnhandledException: {e.ExceptionObject}");
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudySuit_crash.log");
            File.WriteAllText(logPath, e.ExceptionObject?.ToString() ?? "Unknown crash");
        };
        Log("App ctor finished");
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("OnStartup entered");

        try
        {
            Log("Creating services...");
            var pdfService = new PdfService();
            var recentFilesService = new RecentFilesService();
            var annotationService = new AnnotationService();
            var studyPlannerService = new StudyPlannerService();
            Log("Creating MainViewModel...");
            _mainViewModel = new MainViewModel(pdfService, recentFilesService, annotationService, studyPlannerService);

            Log("Applying theme...");
            ApplyTheme(_mainViewModel.IsDarkTheme);
            _mainViewModel.ApplyThemeAction = ApplyTheme;

            Log("Instantiating MainWindow...");
            var mainWindow = new MainWindow(_mainViewModel);
            MainWindow = mainWindow;
            Log("Showing MainWindow...");
            mainWindow.Show();
            Log("MainWindow.Show() returned");

            // Open any PDF file passed on the command line (e.g., "Open with" or CLI).
            foreach (string arg in e.Args)
            {
                if (File.Exists(arg) && string.Equals(Path.GetExtension(arg), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await _mainViewModel.OpenFileAsync(arg);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"OnStartup Exception: {ex}");
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudySuit_crash.log");
            File.WriteAllText(logPath, ex.ToString());
            System.Windows.MessageBox.Show($"Startup error: {ex.Message}\n\nCheck {logPath} for details.", "StudyMateAI Startup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit called with ApplicationExitCode={e.ApplicationExitCode}");
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
