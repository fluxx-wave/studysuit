using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed partial class AiTestViewModel : ObservableObject
{
    private readonly IAiTestService _testService;
    private readonly DispatcherTimer _countdownTimer;
    private readonly Func<Task<string?>>? _getActivePdfContextFunc;
    private readonly Func<string, Task<string?>>? _extractPdfFromFileFunc;
    private readonly Func<IReadOnlyList<NcertBookItem>>? _getDownloadedNcertBooksFunc;
    private readonly Func<(bool HasOpenPdf, string? FileName)>? _getOpenPdfInfoFunc;

    [ObservableProperty]
    private ObservableCollection<AiTestSession> _savedTests = [];

    [ObservableProperty]
    private ObservableCollection<AiTestSession> _filteredSavedTests = [];

    [ObservableProperty]
    private string _searchFilterText = string.Empty;

    // View Modes
    [ObservableProperty]
    private bool _isDashboardVisible = true;

    [ObservableProperty]
    private bool _isCreateDialogVisible;

    [ObservableProperty]
    private bool _isTakingTestVisible;

    [ObservableProperty]
    private bool _isReportVisible;

    [ObservableProperty]
    private bool _isGeneratingTest;

    [ObservableProperty]
    private bool _isEvaluating;

    [ObservableProperty]
    private string _generationStatusText = "Researching and preparing interactive test...";

    [ObservableProperty]
    private string _evaluationStatusText = "Evaluating answers and formulating comprehensive AI report...";

    [ObservableProperty]
    private string _statusBanner = string.Empty;

    public bool HasStatusBanner => !string.IsNullOrWhiteSpace(StatusBanner);

    partial void OnStatusBannerChanged(string value) => OnPropertyChanged(nameof(HasStatusBanner));

    // Create Test Form Inputs & Source Selection
    [ObservableProperty]
    private TestSourceKind _selectedSourceKind = TestSourceKind.Topic;

    [ObservableProperty]
    private bool _hasOpenPdf;

    [ObservableProperty]
    private string? _openPdfFileName;

    [ObservableProperty]
    private string? _pickedPdfPath;

    [ObservableProperty]
    private string? _pickedPdfFileName;

    [ObservableProperty]
    private NcertBookItem? _selectedNcertBook;

    [ObservableProperty]
    private ObservableCollection<NcertBookItem> _downloadedNcertBooks = [];

    public bool IsSourceTopic => SelectedSourceKind == TestSourceKind.Topic;
    public bool IsSourceOpenPdf => SelectedSourceKind == TestSourceKind.OpenPdf;
    public bool IsSourceLocalFile => SelectedSourceKind == TestSourceKind.LocalFile;
    public bool IsSourceDownloadedNcert => SelectedSourceKind == TestSourceKind.DownloadedNcert;

    // Critical user requirement: When user selects PDF/book for test, DO NOT ask the topic!
    public bool RequiresTopicInput => SelectedSourceKind == TestSourceKind.Topic;
    public bool HasDownloadedNcertBooks => DownloadedNcertBooks.Count > 0;

    [ObservableProperty]
    private string _newTopic = string.Empty;

    [ObservableProperty]
    private int _newQuestionCount = 10;

    [ObservableProperty]
    private double _newDurationHours = 0.5;

    [ObservableProperty]
    private string _newDifficulty = "Intermediate";

    [ObservableProperty]
    private string _newTargetExam = "General Study";

    [ObservableProperty]
    private bool _useOpenPdfContext;

    // Active Test Taking State
    [ObservableProperty]
    private AiTestSession? _activeSession;

    [ObservableProperty]
    private TestQuestion? _currentQuestion;

    [ObservableProperty]
    private int _currentQuestionIndex;

    [ObservableProperty]
    private string _timeRemainingDisplay = "00:00:00";

    [ObservableProperty]
    private bool _isTimeCritical;

    [ObservableProperty]
    private bool _isTimeWarning;

    [ObservableProperty]
    private double _timeElapsedPercentage;

    [ObservableProperty]
    private bool _isConfirmSubmitDialogOpen;

    // Review & Report Filter
    [ObservableProperty]
    private string _reportQuestionFilter = "All";

    [ObservableProperty]
    private ObservableCollection<TestQuestion> _filteredReportQuestions = [];

    // Dashboard Aggregate Statistics
    [ObservableProperty]
    private int _totalTestsCompleted;

    [ObservableProperty]
    private double _averageScorePercentage;

    [ObservableProperty]
    private int _totalQuestionsAnswered;

    [ObservableProperty]
    private string _topPerformingTopic = "None yet";

    public bool HasSavedTests => SavedTests.Count > 0;
    public bool HasNoSavedTests => SavedTests.Count == 0;
    public bool CanGoPrevious => CurrentQuestionIndex > 0;
    public bool CanGoNext => ActiveSession != null && CurrentQuestionIndex < ActiveSession.TotalQuestions - 1;
    public bool ShowAiTest => true;

    public string CurrentQuestionNumberDisplay => ActiveSession != null && ActiveSession.TotalQuestions > 0
        ? $"Question {CurrentQuestionIndex + 1} of {ActiveSession.TotalQuestions}"
        : "Question 0 of 0";

    public AiTestViewModel(
        IAiTestService testService,
        Func<Task<string?>>? getActivePdfContextFunc = null,
        Func<string, Task<string?>>? extractPdfFromFileFunc = null,
        Func<IReadOnlyList<NcertBookItem>>? getDownloadedNcertBooksFunc = null,
        Func<(bool HasOpenPdf, string? FileName)>? getOpenPdfInfoFunc = null)
    {
        _testService = testService;
        _getActivePdfContextFunc = getActivePdfContextFunc;
        _extractPdfFromFileFunc = extractPdfFromFileFunc;
        _getDownloadedNcertBooksFunc = getDownloadedNcertBooksFunc;
        _getOpenPdfInfoFunc = getOpenPdfInfoFunc;

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnCountdownTimerTick;

        _ = LoadSavedTestsAsync();
    }

    partial void OnSelectedSourceKindChanged(TestSourceKind value)
    {
        OnPropertyChanged(nameof(IsSourceTopic));
        OnPropertyChanged(nameof(IsSourceOpenPdf));
        OnPropertyChanged(nameof(IsSourceLocalFile));
        OnPropertyChanged(nameof(IsSourceDownloadedNcert));
        OnPropertyChanged(nameof(RequiresTopicInput));

        if (value == TestSourceKind.OpenPdf && !string.IsNullOrWhiteSpace(OpenPdfFileName))
        {
            NewTopic = Path.GetFileNameWithoutExtension(OpenPdfFileName);
        }
        else if (value == TestSourceKind.LocalFile && !string.IsNullOrWhiteSpace(PickedPdfFileName))
        {
            NewTopic = Path.GetFileNameWithoutExtension(PickedPdfFileName);
        }
        else if (value == TestSourceKind.DownloadedNcert && SelectedNcertBook != null)
        {
            NewTopic = $"{SelectedNcertBook.ClassDisplay} {SelectedNcertBook.Subject}: {SelectedNcertBook.Title}";
        }
    }

    partial void OnSelectedNcertBookChanged(NcertBookItem? value)
    {
        if (value != null && SelectedSourceKind == TestSourceKind.DownloadedNcert)
        {
            NewTopic = $"{value.ClassDisplay} {value.Subject}: {value.Title}";
        }
    }

    public void RefreshDownloadedNcertBooks()
    {
        DownloadedNcertBooks.Clear();
        if (_getDownloadedNcertBooksFunc != null)
        {
            var books = _getDownloadedNcertBooksFunc();
            foreach (var b in books)
            {
                DownloadedNcertBooks.Add(b);
            }
        }
        if (SelectedNcertBook == null && DownloadedNcertBooks.Count > 0)
        {
            SelectedNcertBook = DownloadedNcertBooks[0];
        }
        OnPropertyChanged(nameof(HasDownloadedNcertBooks));
    }

    public async Task LoadSavedTestsAsync()
    {
        try
        {
            var list = await _testService.LoadAllTestsAsync().ConfigureAwait(false);
            RunOnUi(() =>
            {
                SavedTests.Clear();
                foreach (var t in list)
                {
                    SavedTests.Add(t);
                }
                ApplyFilter();
                UpdateAggregateStatistics();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiTestViewModel] LoadSavedTests error: {ex.Message}");
        }
    }

    partial void OnSearchFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredSavedTests.Clear();
        string q = SearchFilterText.Trim();
        var matches = string.IsNullOrWhiteSpace(q)
            ? SavedTests
            : SavedTests.Where(t => t.Topic.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   t.Title.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var m in matches)
        {
            FilteredSavedTests.Add(m);
        }

        OnPropertyChanged(nameof(HasSavedTests));
        OnPropertyChanged(nameof(HasNoSavedTests));
    }

    private void UpdateAggregateStatistics()
    {
        var completed = SavedTests.Where(t => t.IsCompleted).ToList();
        TotalTestsCompleted = completed.Count;

        if (completed.Count > 0)
        {
            AverageScorePercentage = Math.Round(completed.Average(t => t.ScorePercentage), 1);
            TotalQuestionsAnswered = completed.Sum(t => t.AnsweredCount);

            var best = completed.OrderByDescending(t => t.ScorePercentage).FirstOrDefault();
            TopPerformingTopic = best != null ? best.Topic : "None yet";
        }
        else
        {
            AverageScorePercentage = 0;
            TotalQuestionsAnswered = 0;
            TopPerformingTopic = "None yet";
        }
    }

    [RelayCommand]
    public void OpenCreateDialog()
    {
        if (_getOpenPdfInfoFunc != null)
        {
            var info = _getOpenPdfInfoFunc();
            HasOpenPdf = info.HasOpenPdf;
            OpenPdfFileName = info.FileName;
        }

        RefreshDownloadedNcertBooks();

        // If a PDF is currently open and no source explicitly chosen, default to open PDF or topic
        if (HasOpenPdf && SelectedSourceKind == TestSourceKind.OpenPdf)
        {
            NewTopic = !string.IsNullOrWhiteSpace(OpenPdfFileName) ? Path.GetFileNameWithoutExtension(OpenPdfFileName) : string.Empty;
        }
        else if (SelectedSourceKind == TestSourceKind.OpenPdf && !HasOpenPdf)
        {
            SelectedSourceKind = TestSourceKind.Topic;
        }

        StatusBanner = string.Empty;
        IsCreateDialogVisible = true;
    }

    [RelayCommand]
    public void CloseCreateDialog()
    {
        IsCreateDialogVisible = false;
    }

    [RelayCommand]
    public void SelectSourceKind(object? parameter)
    {
        if (parameter is string s && Enum.TryParse<TestSourceKind>(s, true, out var kind))
        {
            SelectedSourceKind = kind;
        }
        else if (parameter is TestSourceKind k)
        {
            SelectedSourceKind = k;
        }
    }

    [RelayCommand]
    public void PickLocalPdf()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select PDF Document for AI Test",
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                PickedPdfPath = dlg.FileName;
                PickedPdfFileName = Path.GetFileName(dlg.FileName);
                SelectedSourceKind = TestSourceKind.LocalFile;
                NewTopic = Path.GetFileNameWithoutExtension(dlg.FileName);
                StatusBanner = string.Empty;
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Failed to pick PDF: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SetQuickTopic(string topic)
    {
        if (!string.IsNullOrWhiteSpace(topic))
        {
            NewTopic = topic.Trim();
        }
    }

    [RelayCommand]
    public void SetQuickDuration(object? parameter)
    {
        double hours = 0.5;
        if (parameter is double d) hours = d;
        else if (parameter is float f) hours = f;
        else if (parameter is string s && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) hours = parsed;

        if (hours > 0)
        {
            NewDurationHours = hours;
        }
    }

    [RelayCommand]
    public async Task GenerateAndStartTestAsync()
    {
        string topic = NewTopic.Trim();
        string? pdfContext = null;
        string? sourceDocName = null;

        if (SelectedSourceKind == TestSourceKind.Topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                StatusBanner = "Please enter a topic for the test.";
                return;
            }
        }
        else if (SelectedSourceKind == TestSourceKind.OpenPdf)
        {
            if (!HasOpenPdf)
            {
                StatusBanner = "No PDF is currently open in the reader tabs.";
                return;
            }
            sourceDocName = OpenPdfFileName ?? "Open PDF Document";
            if (string.IsNullOrWhiteSpace(topic))
            {
                topic = Path.GetFileNameWithoutExtension(sourceDocName);
            }
        }
        else if (SelectedSourceKind == TestSourceKind.LocalFile)
        {
            if (string.IsNullOrWhiteSpace(PickedPdfPath) || !File.Exists(PickedPdfPath))
            {
                StatusBanner = "Please browse and select a valid PDF file from your computer.";
                return;
            }
            sourceDocName = PickedPdfFileName ?? Path.GetFileName(PickedPdfPath);
            if (string.IsNullOrWhiteSpace(topic))
            {
                topic = Path.GetFileNameWithoutExtension(sourceDocName);
            }
        }
        else if (SelectedSourceKind == TestSourceKind.DownloadedNcert)
        {
            if (SelectedNcertBook == null)
            {
                StatusBanner = "Please select a downloaded NCERT textbook from the list.";
                return;
            }
            sourceDocName = $"{SelectedNcertBook.ClassDisplay} {SelectedNcertBook.Subject} - {SelectedNcertBook.Title}";
            if (string.IsNullOrWhiteSpace(topic))
            {
                topic = sourceDocName;
            }
        }

        IsGeneratingTest = true;
        GenerationStatusText = $"Preparing interactive reasoning test for \"{topic}\"...";
        try
        {
            if (SelectedSourceKind == TestSourceKind.OpenPdf && _getActivePdfContextFunc != null)
            {
                GenerationStatusText = $"Extracting content from open document: {sourceDocName}...";
                pdfContext = await _getActivePdfContextFunc().ConfigureAwait(false);
            }
            else if (SelectedSourceKind == TestSourceKind.LocalFile && _extractPdfFromFileFunc != null && !string.IsNullOrWhiteSpace(PickedPdfPath))
            {
                GenerationStatusText = $"Reading and analyzing {sourceDocName}...";
                pdfContext = await _extractPdfFromFileFunc(PickedPdfPath).ConfigureAwait(false);
            }
            else if (SelectedSourceKind == TestSourceKind.DownloadedNcert && _extractPdfFromFileFunc != null && SelectedNcertBook != null)
            {
                string? ncertPdfPath = SelectedNcertBook.PrimaryPdfPath;
                if (!string.IsNullOrWhiteSpace(ncertPdfPath) && File.Exists(ncertPdfPath))
                {
                    GenerationStatusText = $"Reading NCERT textbook: {sourceDocName}...";
                    pdfContext = await _extractPdfFromFileFunc(ncertPdfPath).ConfigureAwait(false);
                }
            }

            var session = await _testService.GenerateTestAsync(
                topic: topic,
                questionCount: NewQuestionCount,
                durationHours: NewDurationHours,
                difficulty: NewDifficulty,
                targetExam: NewTargetExam,
                pdfContext: pdfContext,
                sourceKind: SelectedSourceKind,
                sourceDocumentName: sourceDocName,
                onStatusUpdate: status => RunOnUi(() => GenerationStatusText = status)).ConfigureAwait(false);

            RunOnUi(() =>
            {
                SavedTests.Insert(0, session);
                ApplyFilter();
                UpdateAggregateStatistics();
                IsCreateDialogVisible = false;
                StartTest(session);
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                StatusBanner = $"Failed to create test: {ex.Message}";
            });
        }
        finally
        {
            RunOnUi(() => IsGeneratingTest = false);
        }
    }

    [RelayCommand]
    public void StartTest(AiTestSession? session)
    {
        if (session is null || session.Questions.Count == 0) return;

        ActiveSession = session;
        if (session.Status == TestStatus.NotStarted)
        {
            session.Status = TestStatus.InProgress;
            session.StartedAt = DateTime.UtcNow;
            session.TimeRemaining = session.TotalDuration;
        }

        CurrentQuestionIndex = 0;
        CurrentQuestion = session.Questions.Count > 0 ? session.Questions[0] : null;

        UpdateTimerDisplay();
        _countdownTimer.Start();

        IsDashboardVisible = false;
        IsCreateDialogVisible = false;
        IsReportVisible = false;
        IsTakingTestVisible = true;

        UpdateNavigationProperties();
    }

    [RelayCommand]
    public void JumpToQuestion(TestQuestion? question)
    {
        if (question is null || ActiveSession is null) return;
        int idx = ActiveSession.Questions.IndexOf(question);
        if (idx >= 0)
        {
            CurrentQuestionIndex = idx;
            CurrentQuestion = question;
            UpdateNavigationProperties();
        }
    }

    [RelayCommand]
    public void NextQuestion()
    {
        if (ActiveSession is null) return;
        if (CurrentQuestionIndex < ActiveSession.Questions.Count - 1)
        {
            CurrentQuestionIndex++;
            CurrentQuestion = ActiveSession.Questions[CurrentQuestionIndex];
            UpdateNavigationProperties();
        }
    }

    [RelayCommand]
    public void PreviousQuestion()
    {
        if (ActiveSession is null) return;
        if (CurrentQuestionIndex > 0)
        {
            CurrentQuestionIndex--;
            CurrentQuestion = ActiveSession.Questions[CurrentQuestionIndex];
            UpdateNavigationProperties();
        }
    }

    [RelayCommand]
    public void SelectOption(object? parameter)
    {
        if (CurrentQuestion is null || ActiveSession is null) return;
        if (ActiveSession.IsCompleted) return;

        int index = 0;
        if (parameter is int i) index = i;
        else if (parameter is string s && int.TryParse(s, out int parsed)) index = parsed;

        CurrentQuestion.SelectOption(index);
        ActiveSession.NotifyScoresChanged();
    }

    [RelayCommand]
    public void ClearCurrentOption()
    {
        if (CurrentQuestion is null || ActiveSession is null) return;
        if (ActiveSession.IsCompleted) return;

        CurrentQuestion.ClearSelection();
        ActiveSession.NotifyScoresChanged();
    }

    [RelayCommand]
    public void ToggleFlagCurrentQuestion()
    {
        if (CurrentQuestion is null) return;
        CurrentQuestion.ToggleFlag();
        ActiveSession?.NotifyScoresChanged();
    }

    [RelayCommand]
    public void PromptSubmitTest()
    {
        IsConfirmSubmitDialogOpen = true;
    }

    [RelayCommand]
    public void CancelSubmitTest()
    {
        IsConfirmSubmitDialogOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmSubmitTestAsync()
    {
        IsConfirmSubmitDialogOpen = false;
        await SubmitTestAsync(isTimeUp: false);
    }

    private async Task SubmitTestAsync(bool isTimeUp)
    {
        _countdownTimer.Stop();
        if (ActiveSession is null) return;

        ActiveSession.Status = isTimeUp ? TestStatus.TimedOut : TestStatus.Completed;
        ActiveSession.CompletedAt = DateTime.UtcNow;
        ActiveSession.NotifyScoresChanged();

        IsTakingTestVisible = false;
        IsEvaluating = true;
        EvaluationStatusText = isTimeUp
            ? "⏰ Time completed! Evaluating your answers with AI..."
            : "Formulating your comprehensive AI evaluation and feedback report...";

        try
        {
            await _testService.EvaluateTestAsync(
                ActiveSession,
                status => RunOnUi(() => EvaluationStatusText = status)).ConfigureAwait(false);

            RunOnUi(() =>
            {
                UpdateAggregateStatistics();
                ViewReport(ActiveSession);
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                StatusBanner = $"Evaluation notice: {ex.Message}";
                ViewReport(ActiveSession);
            });
        }
        finally
        {
            RunOnUi(() => IsEvaluating = false);
        }
    }

    [RelayCommand]
    public void ViewReport(AiTestSession? session)
    {
        if (session is null) return;

        _countdownTimer.Stop();
        ActiveSession = session;
        ReportQuestionFilter = "All";
        ApplyReportQuestionFilter();

        IsDashboardVisible = false;
        IsCreateDialogVisible = false;
        IsTakingTestVisible = false;
        IsReportVisible = true;
    }

    partial void OnReportQuestionFilterChanged(string value) => ApplyReportQuestionFilter();

    [RelayCommand]
    public void SetReportFilter(string filter)
    {
        ReportQuestionFilter = filter;
    }

    private void ApplyReportQuestionFilter()
    {
        FilteredReportQuestions.Clear();
        if (ActiveSession is null) return;

        var items = ReportQuestionFilter switch
        {
            "Correct" => ActiveSession.Questions.Where(q => q.IsCorrect),
            "Incorrect" => ActiveSession.Questions.Where(q => q.IsAnswered && !q.IsCorrect),
            "Unanswered" => ActiveSession.Questions.Where(q => !q.IsAnswered),
            _ => ActiveSession.Questions
        };

        foreach (var q in items)
        {
            FilteredReportQuestions.Add(q);
        }
    }

    [RelayCommand]
    public void RetakeTest(AiTestSession? session)
    {
        if (session is null) return;

        foreach (var q in session.Questions)
        {
            q.UserSelectedOptionIndex = null;
            q.IsFlagged = false;
        }

        session.Status = TestStatus.NotStarted;
        session.StartedAt = null;
        session.CompletedAt = null;
        session.TimeRemaining = session.TotalDuration;
        session.EvaluationReport = null;
        session.NotifyScoresChanged();

        _ = _testService.SaveTestSessionAsync(session);
        StartTest(session);
    }

    [RelayCommand]
    public async Task DeleteTestAsync(AiTestSession? session)
    {
        if (session is null) return;

        try
        {
            await _testService.DeleteTestSessionAsync(session.Id).ConfigureAwait(false);
            RunOnUi(() =>
            {
                SavedTests.Remove(session);
                ApplyFilter();
                UpdateAggregateStatistics();
                if (ActiveSession == session)
                {
                    BackToDashboard();
                }
            });
        }
        catch (Exception ex)
        {
            StatusBanner = $"Failed to delete test: {ex.Message}";
        }
    }

    [RelayCommand]
    public void BackToDashboard()
    {
        _countdownTimer.Stop();
        IsTakingTestVisible = false;
        IsReportVisible = false;
        IsCreateDialogVisible = false;
        IsDashboardVisible = true;
        UpdateAggregateStatistics();
    }

    private void OnCountdownTimerTick(object? sender, EventArgs e)
    {
        if (ActiveSession is null || ActiveSession.IsCompleted)
        {
            _countdownTimer.Stop();
            return;
        }

        if (ActiveSession.TimeRemaining > TimeSpan.Zero)
        {
            ActiveSession.TimeRemaining = ActiveSession.TimeRemaining.Subtract(TimeSpan.FromSeconds(1));
            UpdateTimerDisplay();

            if (ActiveSession.TimeRemaining <= TimeSpan.Zero)
            {
                _countdownTimer.Stop();
                ActiveSession.TimeRemaining = TimeSpan.Zero;
                UpdateTimerDisplay();
                _ = SubmitTestAsync(isTimeUp: true);
            }
        }
        else
        {
            _countdownTimer.Stop();
            _ = SubmitTestAsync(isTimeUp: true);
        }
    }

    private void UpdateTimerDisplay()
    {
        if (ActiveSession is null) return;

        var tr = ActiveSession.TimeRemaining;
        if (tr < TimeSpan.Zero) tr = TimeSpan.Zero;

        TimeRemainingDisplay = tr.TotalHours >= 1
            ? $"{(int)tr.TotalHours:D2}:{tr.Minutes:D2}:{tr.Seconds:D2}"
            : $"{tr.Minutes:D2}:{tr.Seconds:D2}";

        IsTimeCritical = tr.TotalMinutes < 5;
        IsTimeWarning = tr.TotalMinutes is >= 5 and < 10;

        if (ActiveSession.TotalDuration.TotalSeconds > 0)
        {
            double spentSec = (ActiveSession.TotalDuration - tr).TotalSeconds;
            TimeElapsedPercentage = Math.Min(100.0, Math.Max(0.0, (spentSec / ActiveSession.TotalDuration.TotalSeconds) * 100.0));
        }

        ActiveSession.NotifyScoresChanged();
    }

    private void UpdateNavigationProperties()
    {
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CurrentQuestionNumberDisplay));
    }

    private static void RunOnUi(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }
}
