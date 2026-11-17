using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<TodoPriority> TodoPriorities { get; } =
        [TodoPriority.Low, TodoPriority.Medium, TodoPriority.High];

    public static IReadOnlyList<EventCategory> EventCategories { get; } =
        [EventCategory.StudySession, EventCategory.Exam, EventCategory.Assignment, EventCategory.Lecture, EventCategory.Other];

    public static IReadOnlyList<int> ReminderMinuteOptions { get; } = [0, 5, 15, 30, 60];

    private readonly IPdfService _pdfService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IAnnotationService _annotationService;
    private readonly IStudyPlannerService _studyPlannerService;
    private readonly INewsFeedService _newsFeedService;
    private readonly IVoiceReadoutService _voiceReadoutService;
    private readonly ILlamaCppAutoSetupService _llamaAutoSetupService;
    private readonly AppUserState _userState;
    private readonly StudyPlannerState _plannerState;

    private readonly DispatcherTimer _stopwatchTimer;
    private readonly DispatcherTimer _reminderTimer;
    private DateTime? _stopwatchSegmentStartedUtc;
    private TimeSpan _stopwatchAccumulated = TimeSpan.Zero;
    private CancellationTokenSource? _pdfSpeechCts;
    private readonly HashSet<string> _notifiedReminderIds = [];

    public IPdfService PdfService => _pdfService;
    public IAnnotationService AnnotationService => _annotationService;
    public IAiChatService AiChatService { get; }
    public NotesViewModel Notes { get; }
    public NewsFeedViewModel NewsFeed { get; }
    public AiAssistantViewModel AiAssistant { get; }
    public NcertLibraryViewModel Library { get; }

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = [];
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = [];
    public ObservableCollection<RecentFileItem> FilteredRecentFiles { get; } = [];
    public ObservableCollection<RecentFileItem> PinnedFiles { get; } = [];
    public ObservableCollection<RecentAnnotationSummary> RecentAnnotations { get; } = [];

    // Study Planner Collections
    public ObservableCollection<StudyTodoItem> FilteredTodos { get; } = [];
    public ObservableCollection<StudyEvent> SelectedDateEvents { get; } = [];
    public ObservableCollection<StudyEvent> UpcomingEvents { get; } = [];
    public ObservableCollection<StudyEvent> ActiveReminders { get; } = [];

    [ObservableProperty]
    private DocumentTabViewModel? _selectedTab;

    [ObservableProperty]
    private bool _isHomeActive = true;

    [ObservableProperty]
    private bool _isNotesActive;

    [ObservableProperty]
    private bool _isFeedActive;

    [ObservableProperty]
    private bool _isLibraryActive;

    [ObservableProperty]
    private bool _isSpeakingCurrentPage;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private bool _isNightLightEnabled;

    [ObservableProperty]
    private double _nightLightLevel = 45.0;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private bool _isAiSidebarOpen;

    [ObservableProperty]
    private int _sidebarTabIndex;

    [ObservableProperty]
    private bool _isSearchBarVisible;

    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    private bool _isPropertiesOpen;

    [ObservableProperty]
    private bool _isOpeningDocument;

    [ObservableProperty]
    private string _dashboardSearchText = string.Empty;

    [ObservableProperty]
    private string _globalStatusText = "Home Dashboard • Open a PDF or track your study session";

    // Stopwatch Properties
    [ObservableProperty]
    private bool _isStopwatchRunning;

    [ObservableProperty]
    private string _stopwatchDisplay = "00:00:00";

    [ObservableProperty]
    private string _stopwatchSessionLabel = "Focused Study";

    // Calendar & Event Creation Properties
    [ObservableProperty]
    private DateTime _selectedCalendarDate = DateTime.Today;

    [ObservableProperty]
    private string _newEventTitle = string.Empty;

    [ObservableProperty]
    private string _newEventTimeText = DateTime.Now.AddHours(1).ToString("HH:00");

    [ObservableProperty]
    private EventCategory _newEventCategory = EventCategory.StudySession;

    [ObservableProperty]
    private int _newEventReminderMinutes = 15;

    // To-Do Properties
    [ObservableProperty]
    private string _newTodoTitle = string.Empty;

    [ObservableProperty]
    private TodoPriority _newTodoPriority = TodoPriority.Medium;

    [ObservableProperty]
    private string _todoFilterMode = "All";

    public bool HasOpenDocument => SelectedTab is not null;
    public bool HasAnyTabs => Tabs.Count > 0;
    public bool ShowHomeDashboard => !IsNotesActive && !IsFeedActive && !IsLibraryActive && (IsHomeActive || SelectedTab is null);
    public bool ShowNotesStudio => IsNotesActive;
    public bool ShowNewsFeed => IsFeedActive;
    public bool ShowNcertLibrary => IsLibraryActive;
    public bool ShowDocumentReader => !IsHomeActive && !IsNotesActive && !IsFeedActive && !IsLibraryActive && SelectedTab is not null;
    public double NightLightOverlayOpacity => IsNightLightEnabled ? Math.Clamp(NightLightLevel / 100.0, 0.0, 1.0) * 0.52 : 0.0;
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public bool HasPinnedFiles => PinnedFiles.Count > 0;
    public bool HasRecentAnnotations => RecentAnnotations.Count > 0;
    public bool HasActiveReminders => ActiveReminders.Count > 0;
    public bool HasSelectedDateEvents => SelectedDateEvents.Count > 0;
    public bool HasTodos => FilteredTodos.Count > 0;
    public bool CanResetOrLogStopwatch => IsStopwatchRunning || _stopwatchAccumulated > TimeSpan.Zero;
    public string StopwatchActionLabel => IsStopwatchRunning ? "Pause" : "Start";

    public RecentFileItem? ContinueReadingItem => RecentFiles.FirstOrDefault();
    public int TotalDocumentsCount => RecentFiles.Count;
    public int PinnedDocumentsCount => PinnedFiles.Count;
    public int TotalAnnotationsCount { get; private set; }
    public int UpcomingEventsCount => UpcomingEvents.Count;
    public int CompletedTodoCount => _plannerState.Todos.Count(t => t.IsCompleted);
    public int TotalTodoCount => _plannerState.Todos.Count;
    public string TodoProgressLabel => $"{CompletedTodoCount} / {TotalTodoCount} done";
    public string SelectedCalendarDateLabel => SelectedCalendarDate.ToString("ddd, MMM dd yyyy");

    public string TodayStudyTimeDisplay
    {
        get
        {
            TimeSpan today = _studyPlannerService.GetTodayStudyTime(_plannerState, DateTime.Today) + GetElapsedStopwatch();
            return FormatDurationFriendly(today);
        }
    }

    public string TotalStudyTimeDisplay
    {
        get
        {
            TimeSpan total = _studyPlannerService.GetTotalStudyTime(_plannerState) + GetElapsedStopwatch();
            return FormatDurationFriendly(total);
        }
    }

    public Func<string, bool, Task<string?>>? PromptForPasswordAsync { get; set; }
    public Action<bool>? ApplyThemeAction { get; set; }
    public Action<string, string>? ShowMessageAction { get; set; }
    public Action? FocusSearchInputAction { get; set; }
    public event Action? ReminderTriggered;
    public event Action? CalendarEventsUpdated;
    public event Action? OpenAiSettingsRequested;

    public bool ShowAiSidebar => IsAiSidebarOpen && !IsFullScreen;
    public IVoiceReadoutService VoiceReadoutService => _voiceReadoutService;
    public ILlamaCppAutoSetupService LlamaAutoSetupService => _llamaAutoSetupService;

    public MainViewModel(
        IPdfService pdfService,
        IRecentFilesService recentFilesService,
        IAnnotationService? annotationService = null,
        IStudyPlannerService? studyPlannerService = null,
        INotesService? notesService = null,
        IAiSettingsService? aiSettingsService = null,
        IAiChatService? aiChatService = null,
        IWebResearchService? webResearchService = null,
        INewsFeedService? newsFeedService = null,
        IVoiceReadoutService? voiceReadoutService = null,
        ILlamaCppAutoSetupService? llamaAutoSetupService = null)
    {
        _pdfService = pdfService;
        _recentFilesService = recentFilesService;
        _annotationService = annotationService ?? new AnnotationService();
        _studyPlannerService = studyPlannerService ?? new StudyPlannerService();
        _newsFeedService = newsFeedService ?? new NewsFeedService();
        _voiceReadoutService = voiceReadoutService ?? new VoiceReadoutService();
        _llamaAutoSetupService = llamaAutoSetupService ?? new LlamaCppAutoSetupService();

        Notes = new NotesViewModel(notesService);
        AiChatService = aiChatService ?? new AiChatService();
        AiAssistant = new AiAssistantViewModel(
            aiSettingsService ?? new AiSettingsService(),
            AiChatService,
            webResearchService ?? new WebResearchService(),
            new WebpageFetchService(),
            new Sketchfab3DService(),
            null,
            _llamaAutoSetupService);
        AiAssistant.GetActivePdfContextAsync = GetActivePdfContextForAiAsync;
        AiAssistant.HasOpenPdfProvider = () => SelectedTab is not null;
        AiAssistant.SearchPdfToolAsync = SearchActivePdfForAiAsync;
        AiAssistant.ReadPdfPageToolAsync = ReadActivePdfPageForAiAsync;
        AiAssistant.OpenSettingsRequested += () => OpenAiSettingsRequested?.Invoke();

        NewsFeed = new NewsFeedViewModel(_newsFeedService, _voiceReadoutService)
        {
            VoiceModeProvider = () => AiAssistant.Settings.VoiceMode,
            VoiceSettingsProvider = () => (AiAssistant.Settings.VoiceSpeed, AiAssistant.Settings.VoicePitch, AiAssistant.Settings.VoiceModel)
        };
        NewsFeed.DiscussWithAiRequested += item =>
        {
            IsAiSidebarOpen = true;
            string details = !string.IsNullOrWhiteSpace(item.FullContent) ? item.FullContent : item.Summary;
            AiAssistant.SendQuickPromptCommand.Execute(
                $"Explain and analyze this news topic with background context and competitive exam relevance:\n\n**{item.Title}**\n{details}\nSource: {item.Source} ({item.Url})");
        };
        NewsFeed.DiscussCustomPromptRequested += (_, prompt) =>
        {
            IsAiSidebarOpen = true;
            AiAssistant.SendQuickPromptCommand.Execute(prompt);
        };
        NewsFeed.StopOtherSpeechRequested += () =>
        {
            StopPdfSpeech();
            AiAssistant.StopSpeaking();
        };

        Library = new NcertLibraryViewModel();
        Library.BookOpenRequested += async path =>
        {
            IsLibraryActive = false;
            await OpenFileAsync(path);
        };

        _userState = _recentFilesService.Load();
        _plannerState = _studyPlannerService.Load();
        _isDarkTheme = _userState.IsDarkTheme;
        _isNightLightEnabled = _userState.IsNightLightEnabled;
        _nightLightLevel = Math.Clamp(_userState.NightLightLevel, 0.0, 100.0);

        _stopwatchTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _stopwatchTimer.Tick += (_, _) => UpdateStopwatchTick();

        _reminderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _reminderTimer.Tick += (_, _) => CheckDueReminders();
        _reminderTimer.Start();

        RefreshDashboard();
        RefreshStudyPlannerCollections();
        CheckDueReminders();
    }

    public bool DateHasEvents(DateTime date) =>
        _plannerState.Events.Any(e => e.EventDateTime.Date == date.Date);

    partial void OnSelectedTabChanged(DocumentTabViewModel? value)
    {
        IsSearchBarVisible = false;
        if (value is not null)
        {
            IsHomeActive = false;
            IsNotesActive = false;
            IsFeedActive = false;
            IsLibraryActive = false;
            GlobalStatusText = $"{value.DocumentInfo.FileName} • {value.PageCount} pages • {value.FileSizeDisplay}";
            StopwatchSessionLabel = $"Studying: {value.DocumentInfo.FileName}";
            _ = value.RenderAroundCurrentPageAsync();
        }
        else
        {
            if (!IsNotesActive && !IsFeedActive && !IsLibraryActive)
            {
                IsHomeActive = true;
            }
            IsPropertiesOpen = false;
            GlobalStatusText = "Home Dashboard • Open a PDF or track your study session";
        }

        NotifyWorkspaceVisibilityProperties();
    }

    partial void OnIsHomeActiveChanged(bool value)
    {
        if (value)
        {
            SetActiveWorkspace(home: true);
        }
        else
        {
            NotifyWorkspaceVisibilityProperties();
        }
    }

    partial void OnIsNotesActiveChanged(bool value)
    {
        if (value)
        {
            SetActiveWorkspace(notes: true);
        }
        else
        {
            NotifyWorkspaceVisibilityProperties();
        }
    }

    partial void OnIsFeedActiveChanged(bool value)
    {
        if (value)
        {
            SetActiveWorkspace(feed: true);
        }
        else
        {
            NotifyWorkspaceVisibilityProperties();
        }
    }

    partial void OnIsLibraryActiveChanged(bool value)
    {
        if (value)
        {
            SetActiveWorkspace(library: true);
        }
        else
        {
            NotifyWorkspaceVisibilityProperties();
        }
    }

    partial void OnDashboardSearchTextChanged(string value) => ApplyDashboardFilter();

    partial void OnSelectedCalendarDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(SelectedCalendarDateLabel));
        RefreshCalendarCollections();
    }

    partial void OnTodoFilterModeChanged(string value) => RefreshTodoCollections();

    partial void OnIsDarkThemeChanged(bool value)
    {
        _userState.IsDarkTheme = value;
        _recentFilesService.Save(_userState);
        ApplyThemeAction?.Invoke(value);
    }

    partial void OnIsNightLightEnabledChanged(bool value)
    {
        if (value && NightLightLevel <= 0.5)
        {
            _nightLightLevel = 45.0;
            _userState.NightLightLevel = 45.0;
            OnPropertyChanged(nameof(NightLightLevel));
        }

        _userState.IsNightLightEnabled = value;
        _recentFilesService.Save(_userState);
        OnPropertyChanged(nameof(NightLightOverlayOpacity));
    }

    partial void OnNightLightLevelChanged(double value)
    {
        double clamped = Math.Clamp(value, 0.0, 100.0);
        if (Math.Abs(clamped - value) > 0.01)
        {
            NightLightLevel = clamped;
            return;
        }

        _userState.NightLightLevel = clamped;
        _recentFilesService.Save(_userState);
        OnPropertyChanged(nameof(NightLightOverlayOpacity));
    }

    [RelayCommand]
    public void ToggleNightLight() => IsNightLightEnabled = !IsNightLightEnabled;

    // Stopwatch Commands
    [RelayCommand]
    public void ToggleStopwatch()
    {
        if (IsStopwatchRunning)
        {
            if (_stopwatchSegmentStartedUtc.HasValue)
            {
                _stopwatchAccumulated += DateTime.UtcNow - _stopwatchSegmentStartedUtc.Value;
                _stopwatchSegmentStartedUtc = null;
            }

            IsStopwatchRunning = false;
            _stopwatchTimer.Stop();
        }
        else
        {
            _stopwatchSegmentStartedUtc = DateTime.UtcNow;
            IsStopwatchRunning = true;
            _stopwatchTimer.Start();
        }

        UpdateStopwatchTick();
        OnPropertyChanged(nameof(StopwatchActionLabel));
        OnPropertyChanged(nameof(CanResetOrLogStopwatch));
    }

    [RelayCommand]
    public void LogStopwatchSession()
    {
        TimeSpan elapsed = GetElapsedStopwatch();
        if (elapsed.TotalSeconds < 1)
        {
            return;
        }

        _plannerState.StudySessions.Add(new StudySessionLog
        {
            DateLocal = DateTime.Today,
            DurationSeconds = (long)Math.Round(elapsed.TotalSeconds),
            Label = string.IsNullOrWhiteSpace(StopwatchSessionLabel) ? "Focused Study" : StopwatchSessionLabel.Trim(),
            LoggedUtc = DateTime.UtcNow
        });

        _studyPlannerService.Save(_plannerState);
        ResetStopwatch();
    }

    [RelayCommand]
    public void ResetStopwatch()
    {
        _stopwatchTimer.Stop();
        _stopwatchSegmentStartedUtc = null;
        _stopwatchAccumulated = TimeSpan.Zero;
        IsStopwatchRunning = false;
        StopwatchDisplay = "00:00:00";
        OnPropertyChanged(nameof(StopwatchActionLabel));
        OnPropertyChanged(nameof(CanResetOrLogStopwatch));
        OnPropertyChanged(nameof(TodayStudyTimeDisplay));
        OnPropertyChanged(nameof(TotalStudyTimeDisplay));
    }

    private TimeSpan GetElapsedStopwatch()
    {
        TimeSpan current = _stopwatchAccumulated;
        if (IsStopwatchRunning && _stopwatchSegmentStartedUtc.HasValue)
        {
            current += DateTime.UtcNow - _stopwatchSegmentStartedUtc.Value;
        }

        return current;
    }

    private void UpdateStopwatchTick()
    {
        TimeSpan elapsed = GetElapsedStopwatch();
        StopwatchDisplay = elapsed.ToString(@"hh\:mm\:ss");
        OnPropertyChanged(nameof(TodayStudyTimeDisplay));
        OnPropertyChanged(nameof(TotalStudyTimeDisplay));
    }

    // Calendar & Event Commands
    [RelayCommand]
    public void AddStudyEvent()
    {
        if (string.IsNullOrWhiteSpace(NewEventTitle))
        {
            return;
        }

        if (!TimeSpan.TryParseExact(NewEventTimeText?.Trim(), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out TimeSpan timeOfDay))
        {
            timeOfDay = new TimeSpan(DateTime.Now.Hour, DateTime.Now.Minute, 0);
        }

        DateTime eventDt = SelectedCalendarDate.Date.Add(timeOfDay);
        var ev = new StudyEvent
        {
            Title = NewEventTitle.Trim(),
            EventDateTime = eventDt,
            Category = NewEventCategory,
            ReminderMinutesBefore = Math.Max(0, NewEventReminderMinutes),
            ReminderEnabled = NewEventReminderMinutes >= 0
        };

        _plannerState.Events.Add(ev);
        _studyPlannerService.Save(_plannerState);
        NewEventTitle = string.Empty;

        RefreshCalendarCollections();
        CheckDueReminders();
        CalendarEventsUpdated?.Invoke();
    }

    [RelayCommand]
    public void DeleteStudyEvent(StudyEvent? ev)
    {
        if (ev is null)
        {
            return;
        }

        _plannerState.Events.RemoveAll(e => e.Id == ev.Id);
        ActiveReminders.Remove(ev);
        _studyPlannerService.Save(_plannerState);
        RefreshCalendarCollections();
        OnPropertyChanged(nameof(HasActiveReminders));
        CalendarEventsUpdated?.Invoke();
    }

    [RelayCommand]
    public void DismissReminder(StudyEvent? ev)
    {
        if (ev is null)
        {
            return;
        }

        ev.ReminderDismissed = true;
        ActiveReminders.Remove(ev);
        _studyPlannerService.Save(_plannerState);
        OnPropertyChanged(nameof(HasActiveReminders));
    }

    [RelayCommand]
    public void SnoozeReminder(StudyEvent? ev)
    {
        if (ev is null)
        {
            return;
        }

        ev.SnoozedUntilUtc = DateTime.UtcNow.AddMinutes(5);
        _notifiedReminderIds.Remove(ev.Id);
        ActiveReminders.Remove(ev);
        _studyPlannerService.Save(_plannerState);
        OnPropertyChanged(nameof(HasActiveReminders));
    }

    public void CheckDueReminders(DateTime? nowLocalOverride = null, DateTime? nowUtcOverride = null)
    {
        DateTime nowLocal = nowLocalOverride ?? DateTime.Now;
        DateTime nowUtc = nowUtcOverride ?? DateTime.UtcNow;

        var due = _studyPlannerService.GetDueReminders(_plannerState, nowLocal, nowUtc);
        bool anyNew = false;

        ActiveReminders.Clear();
        foreach (var ev in due)
        {
            ActiveReminders.Add(ev);
            if (_notifiedReminderIds.Add(ev.Id))
            {
                anyNew = true;
            }
        }

        OnPropertyChanged(nameof(HasActiveReminders));
        if (anyNew)
        {
            ReminderTriggered?.Invoke();
        }
    }

    // To-Do Commands
    [RelayCommand]
    public void AddTodo()
    {
        if (string.IsNullOrWhiteSpace(NewTodoTitle))
        {
            return;
        }

        _plannerState.Todos.Insert(0, new StudyTodoItem
        {
            Title = NewTodoTitle.Trim(),
            Priority = NewTodoPriority,
            DueDate = SelectedCalendarDate.Date
        });

        NewTodoTitle = string.Empty;
        _studyPlannerService.Save(_plannerState);
        RefreshTodoCollections();
    }

    [RelayCommand]
    public void ToggleTodo(StudyTodoItem? item)
    {
        if (item is null)
        {
            return;
        }

        _studyPlannerService.Save(_plannerState);
        RefreshTodoCollections();
    }

    [RelayCommand]
    public void DeleteTodo(StudyTodoItem? item)
    {
        if (item is null)
        {
            return;
        }

        _plannerState.Todos.RemoveAll(t => t.Id == item.Id);
        _studyPlannerService.Save(_plannerState);
        RefreshTodoCollections();
    }

    [RelayCommand]
    public void ClearCompletedTodos()
    {
        int removed = _plannerState.Todos.RemoveAll(t => t.IsCompleted);
        if (removed > 0)
        {
            _studyPlannerService.Save(_plannerState);
            RefreshTodoCollections();
        }
    }

    [RelayCommand]
    public void SetTodoFilter(string mode)
    {
        if (!string.IsNullOrWhiteSpace(mode))
        {
            TodoFilterMode = mode;
        }
    }

    // Navigation & PDF Commands
    public void SetActiveWorkspace(bool home = false, bool notes = false, bool feed = false, bool library = false)
    {
#pragma warning disable MVVMTK0034
        _isHomeActive = home;
        _isNotesActive = notes;
        _isFeedActive = feed;
        _isLibraryActive = library;
#pragma warning restore MVVMTK0034

        if (home)
        {
            IsPropertiesOpen = false;
            IsSearchBarVisible = false;
            RefreshDashboard();
            RefreshStudyPlannerCollections();
            GlobalStatusText = "Home Dashboard • Study timer, calendar, tasks & PDFs";
        }
        else if (notes)
        {
            IsPropertiesOpen = false;
            IsSearchBarVisible = false;
            GlobalStatusText = "Notes Studio • Notebooks, formulas, sketches & attachments";
        }
        else if (feed)
        {
            IsPropertiesOpen = false;
            IsSearchBarVisible = false;
            GlobalStatusText = "News Feed • Educational updates & announcements";
            _ = NewsFeed.RefreshFeedAsync();
        }
        else if (library)
        {
            IsPropertiesOpen = false;
            IsSearchBarVisible = false;
            GlobalStatusText = "NCERT Library • Browse, download and read official Class 6–12 textbooks";
            _ = Library.RefreshStatesAsync();
        }
        else if (SelectedTab is not null)
        {
            GlobalStatusText = $"{SelectedTab.DocumentInfo.FileName} • {SelectedTab.PageCount} pages • {SelectedTab.FileSizeDisplay}";
        }

        OnPropertyChanged(nameof(IsHomeActive));
        OnPropertyChanged(nameof(IsNotesActive));
        OnPropertyChanged(nameof(IsFeedActive));
        OnPropertyChanged(nameof(IsLibraryActive));

        NotifyWorkspaceVisibilityProperties();
    }

    [RelayCommand]
    public void ShowHome()
    {
        SetActiveWorkspace(home: true);
    }

    [RelayCommand]
    public void ShowNotes()
    {
        SetActiveWorkspace(notes: true);
    }

    [RelayCommand]
    public void ShowFeed()
    {
        SetActiveWorkspace(feed: true);
    }

    [RelayCommand]
    public void ShowLibrary()
    {
        SetActiveWorkspace(library: true);
    }

    [RelayCommand]
    public void SelectTab(DocumentTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        SelectedTab = tab;
        SetActiveWorkspace();
    }

    [RelayCommand]
    public async Task OpenFileDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open PDF Document",
            Filter = "PDF Documents (*.pdf)|*.pdf|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (string file in dialog.FileNames)
            {
                await OpenFileAsync(file);
            }
        }
    }

    [RelayCommand]
    public async Task OpenFolderPdfsAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder Containing PDFs"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        var pdfs = Directory.EnumerateFiles(dialog.FolderName, "*.pdf", SearchOption.TopDirectoryOnly)
            .Take(6)
            .ToList();

        if (pdfs.Count == 0)
        {
            ShowMessageAction?.Invoke("No PDFs Found", $"No .pdf files were found in \"{dialog.FolderName}\".");
            return;
        }

        foreach (string pdf in pdfs)
        {
            await OpenFileAsync(pdf);
        }
    }

    [RelayCommand]
    public async Task OpenRecentFileAsync(RecentFileItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!File.Exists(item.FilePath))
        {
            _recentFilesService.RemoveFile(_userState, item.FilePath);
            RefreshDashboard();
            ShowMessageAction?.Invoke("File Not Found", $"Could not find \"{item.FilePath}\". It has been removed from recent files.");
            return;
        }

        await OpenFileAsync(item.FilePath, item.LastPage);
    }

    [RelayCommand]
    public async Task OpenRecentAnnotationAsync(RecentAnnotationSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        if (!File.Exists(summary.FilePath))
        {
            ShowMessageAction?.Invoke("File Not Found", $"Could not find \"{summary.FilePath}\".");
            return;
        }

        await OpenFileAsync(summary.FilePath, summary.PageNumber);
        if (SelectedTab is not null)
        {
            IsSidebarOpen = true;
            SidebarTabIndex = 3;
            SelectedTab.GoToPage(summary.PageNumber);
        }
    }

    [RelayCommand]
    public void TogglePinRecentFile(RecentFileItem? item)
    {
        if (item is null)
        {
            return;
        }

        _recentFilesService.TogglePin(_userState, item.FilePath);
        RefreshDashboard();
    }

    [RelayCommand]
    public void RemoveRecentFile(RecentFileItem? item)
    {
        if (item is null)
        {
            return;
        }

        _recentFilesService.RemoveFile(_userState, item.FilePath);
        RefreshDashboard();
    }

    public async Task OpenFileAsync(string filePath, int? preferredStartPage = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        var existingTab = Tabs.FirstOrDefault(
            t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (existingTab is not null)
        {
            SelectedTab = existingTab;
            IsHomeActive = false;
            IsNotesActive = false;
            IsFeedActive = false;
            IsLibraryActive = false;
            if (preferredStartPage is int targetPage)
            {
                existingTab.GoToPage(targetPage);
            }
            return;
        }

        IsOpeningDocument = true;
        GlobalStatusText = $"Opening {Path.GetFileName(filePath)}...";

        try
        {
            string? password = null;
            PdfDocumentInfo? info = null;
            bool wrongPassword = false;

            while (info is null)
            {
                try
                {
                    info = await _pdfService.OpenDocumentAsync(filePath, password);
                }
                catch (PdfPasswordRequiredException)
                {
                    if (PromptForPasswordAsync is null)
                    {
                        throw;
                    }

                    password = await PromptForPasswordAsync(Path.GetFileName(filePath), wrongPassword);
                    if (password is null)
                    {
                        GlobalStatusText = "Cancelled opening encrypted PDF";
                        return;
                    }

                    wrongPassword = true;
                }
            }

            int startPage = preferredStartPage
                ?? _userState.RecentFiles
                    .FirstOrDefault(r => string.Equals(r.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    ?.LastPage
                ?? 1;

            var tab = new DocumentTabViewModel(
                info,
                _pdfService,
                password,
                startPage,
                onPageChanged: OnTabPageChanged,
                annotationService: _annotationService,
                onAnnotationsChanged: OnTabAnnotationsChanged);

            Tabs.Add(tab);
            SelectedTab = tab;
            IsHomeActive = false;
            IsNotesActive = false;
            IsFeedActive = false;
            IsLibraryActive = false;

            _recentFilesService.RecordOpenedFile(
                _userState,
                filePath,
                info.PageCount,
                tab.CurrentPageNumber,
                tab.Annotations.Count);
            RefreshDashboard();
        }
        catch (Exception ex)
        {
            GlobalStatusText = "Failed to open PDF";
            ShowMessageAction?.Invoke("Unable to Open PDF", ex.Message);
        }
        finally
        {
            IsOpeningDocument = false;
        }
    }

    [RelayCommand]
    public void CloseTab(DocumentTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null)
        {
            return;
        }

        int idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)];
        }
        else
        {
            SelectedTab = null;
            IsHomeActive = true;
        }

        NotifyWorkspaceVisibilityProperties();
    }

    [RelayCommand]
    public void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    [RelayCommand]
    public void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    public void ToggleAiSidebar() => IsAiSidebarOpen = !IsAiSidebarOpen;

    [RelayCommand]
    public void OpenAiSettings() => OpenAiSettingsRequested?.Invoke();

    partial void OnIsAiSidebarOpenChanged(bool value) => OnPropertyChanged(nameof(ShowAiSidebar));
    partial void OnIsFullScreenChanged(bool value) => OnPropertyChanged(nameof(ShowAiSidebar));

    private async Task<string?> GetActivePdfContextForAiAsync()
    {
        if (SelectedTab is null)
        {
            return null;
        }

        try
        {
            string pageText = await SelectedTab.GetCurrentPageTextAsync();
            if (pageText.Length > 6000)
            {
                pageText = pageText[..6000] + "…";
            }

            return $"File: {SelectedTab.DocumentInfo.FileName}\n" +
                   $"Current Page: {SelectedTab.CurrentPageNumber} of {SelectedTab.PageCount}\n" +
                   $"Page Text:\n{pageText}";
        }
        catch
        {
            return $"File: {SelectedTab.DocumentInfo.FileName} (Page {SelectedTab.CurrentPageNumber} of {SelectedTab.PageCount})";
        }
    }

    private async Task<string> SearchActivePdfForAiAsync(string query, CancellationToken ct)
    {
        if (SelectedTab is null)
        {
            return "No PDF document is currently open.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "Search query was empty.";
        }

        var matches = await SelectedTab.SearchAllPagesForAiAsync(query, ct);
        if (matches.Count == 0)
        {
            return $"No matches found for \"{query}\" in {SelectedTab.DocumentInfo.FileName}.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {matches.Count} match(es) for \"{query}\" in {SelectedTab.DocumentInfo.FileName}:");
        foreach (var m in matches.Take(10))
        {
            sb.AppendLine($"- Page {m.PageNumber}: {m.Snippet}");
        }

        return sb.ToString();
    }

    private async Task<string> ReadActivePdfPageForAiAsync(int pageNumber, CancellationToken ct)
    {
        if (SelectedTab is null)
        {
            return "No PDF document is currently open.";
        }

        int clamped = Math.Clamp(pageNumber, 1, Math.Max(1, SelectedTab.PageCount));
        string text = await SelectedTab.GetPageTextForAiAsync(clamped, ct);
        return $"File: {SelectedTab.DocumentInfo.FileName}, Page {clamped} of {SelectedTab.PageCount}:\n{text}";
    }

    [RelayCommand]
    public void ToggleSearch()
    {
        if (!ShowDocumentReader)
        {
            return;
        }

        IsSearchBarVisible = !IsSearchBarVisible;
        if (IsSearchBarVisible)
        {
            FocusSearchInputAction?.Invoke();
        }
        else
        {
            SelectedTab?.ClearSearchCommand.Execute(null);
        }
    }

    [RelayCommand]
    public void ToggleFullScreen()
    {
        if (ShowDocumentReader)
        {
            IsFullScreen = !IsFullScreen;
        }
    }

    [RelayCommand]
    public void ToggleProperties()
    {
        if (ShowDocumentReader)
        {
            IsPropertiesOpen = !IsPropertiesOpen;
        }
    }

    [RelayCommand]
    public async Task ReadCurrentPdfPageAloudAsync()
    {
        if (SelectedTab is null)
        {
            return;
        }

        if (IsSpeakingCurrentPage)
        {
            StopPdfSpeech();
            return;
        }

        AiAssistant.StopSpeaking();
        NewsFeed.StopArticleReadout();
        StopPdfSpeech();

        try
        {
            string text = await SelectedTab.GetCurrentPageTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                SelectedTab.StatusMessage = $"No readable text found on page {SelectedTab.CurrentPageNumber}";
                return;
            }

            IsSpeakingCurrentPage = true;
            _pdfSpeechCts = new CancellationTokenSource();
            var ct = _pdfSpeechCts.Token;
            var mode = AiAssistant.SelectedVoiceMode;
            SelectedTab.StatusMessage = $"Reading page {SelectedTab.CurrentPageNumber} aloud ({AiPersonalisationDefaults.GetVoiceOption(mode).ShortLabel})...";

            await _voiceReadoutService.SpeakAsync(
                text,
                mode,
                AiAssistant.Settings.VoiceSpeed,
                AiAssistant.Settings.VoicePitch,
                AiAssistant.Settings.VoiceModel,
                ct);
            if (!ct.IsCancellationRequested && SelectedTab is not null)
            {
                SelectedTab.StatusMessage = $"Finished reading page {SelectedTab.CurrentPageNumber}";
            }
        }
        catch (OperationCanceledException)
        {
            if (SelectedTab is not null)
            {
                SelectedTab.StatusMessage = "PDF voice readout stopped";
            }
        }
        catch (Exception ex)
        {
            if (SelectedTab is not null)
            {
                SelectedTab.StatusMessage = $"Voice error: {ex.Message}";
            }
        }
        finally
        {
            IsSpeakingCurrentPage = false;
        }
    }

    [RelayCommand]
    public void StopPdfSpeech()
    {
        try
        {
            _pdfSpeechCts?.Cancel();
            _pdfSpeechCts?.Dispose();
            _pdfSpeechCts = null;
        }
        catch
        {
        }

        _voiceReadoutService.Stop();
        IsSpeakingCurrentPage = false;
        if (SelectedTab is not null)
        {
            SelectedTab.StatusMessage = "PDF voice readout stopped";
        }
    }

    [RelayCommand]
    public async Task CopyCurrentPageTextAsync()
    {
        if (SelectedTab is null)
        {
            return;
        }

        try
        {
            string text = await SelectedTab.GetCurrentPageTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                SelectedTab.StatusMessage = $"No selectable text found on page {SelectedTab.CurrentPageNumber}";
                return;
            }

            Clipboard.SetText(text);
            SelectedTab.StatusMessage = $"Copied page {SelectedTab.CurrentPageNumber} text to clipboard";
        }
        catch (Exception ex)
        {
            ShowMessageAction?.Invoke("Copy Failed", ex.Message);
        }
    }

    [RelayCommand]
    public async Task ExportCurrentPagePngAsync()
    {
        if (SelectedTab is null)
        {
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(SelectedTab.FilePath);
        var dialog = new SaveFileDialog
        {
            Title = "Export Current Page as PNG",
            Filter = "PNG Image (*.png)|*.png",
            FileName = $"{baseName}_page_{SelectedTab.CurrentPageNumber}.png"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await SelectedTab.ExportCurrentPagePngAsync(dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowMessageAction?.Invoke("Export Failed", ex.Message);
            }
        }
    }

    [RelayCommand]
    public void ExportAnnotationsMarkdown()
    {
        if (SelectedTab is null || !SelectedTab.HasAnnotations)
        {
            ShowMessageAction?.Invoke("No Annotations", "Add highlights, notes, or markups before exporting study notes.");
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(SelectedTab.FilePath);
        var dialog = new SaveFileDialog
        {
            Title = "Export Annotations & Study Notes",
            Filter = "Markdown Document (*.md)|*.md",
            FileName = $"{baseName}_notes.md"
        };

        if (dialog.ShowDialog() == true)
        {
            string markdown = SelectedTab.ExportAnnotationsAsMarkdown();
            File.WriteAllText(dialog.FileName, markdown);
            SelectedTab.StatusMessage = $"Exported {SelectedTab.Annotations.Count} annotation(s) to {Path.GetFileName(dialog.FileName)}";
        }
    }

    private void OnTabPageChanged(string filePath, int pageNumber)
    {
        _recentFilesService.UpdateLastPage(_userState, filePath, pageNumber);
        RefreshDashboard();
    }

    private void OnTabAnnotationsChanged(string filePath, int count)
    {
        _recentFilesService.UpdateAnnotationCount(_userState, filePath, count);
        RefreshDashboard();
    }

    public void RefreshDashboard()
    {
        RecentFiles.Clear();
        PinnedFiles.Clear();
        foreach (var item in _userState.RecentFiles)
        {
            RecentFiles.Add(item);
            if (item.IsPinned)
            {
                PinnedFiles.Add(item);
            }
        }

        RecentAnnotations.Clear();
        foreach (var ann in _annotationService.GetRecentAnnotations(8))
        {
            RecentAnnotations.Add(ann);
        }

        TotalAnnotationsCount = _annotationService.GetTotalAnnotationCount();

        ApplyDashboardFilter();
        OnPropertyChanged(nameof(HasRecentFiles));
        OnPropertyChanged(nameof(HasPinnedFiles));
        OnPropertyChanged(nameof(HasRecentAnnotations));
        OnPropertyChanged(nameof(ContinueReadingItem));
        OnPropertyChanged(nameof(TotalDocumentsCount));
        OnPropertyChanged(nameof(PinnedDocumentsCount));
        OnPropertyChanged(nameof(TotalAnnotationsCount));
        OnPropertyChanged(nameof(TodayStudyTimeDisplay));
        OnPropertyChanged(nameof(TotalStudyTimeDisplay));
    }

    private void RefreshStudyPlannerCollections()
    {
        RefreshTodoCollections();
        RefreshCalendarCollections();
    }

    private void RefreshTodoCollections()
    {
        FilteredTodos.Clear();
        var items = _plannerState.Todos.Where(t => TodoFilterMode switch
        {
            "Active" => !t.IsCompleted,
            "Completed" => t.IsCompleted,
            _ => true
        });

        foreach (var item in items)
        {
            FilteredTodos.Add(item);
        }

        OnPropertyChanged(nameof(HasTodos));
        OnPropertyChanged(nameof(CompletedTodoCount));
        OnPropertyChanged(nameof(TotalTodoCount));
        OnPropertyChanged(nameof(TodoProgressLabel));
    }

    private void RefreshCalendarCollections()
    {
        SelectedDateEvents.Clear();
        foreach (var ev in _plannerState.Events
            .Where(e => e.EventDateTime.Date == SelectedCalendarDate.Date)
            .OrderBy(e => e.EventDateTime))
        {
            SelectedDateEvents.Add(ev);
        }

        UpcomingEvents.Clear();
        DateTime now = DateTime.Now;
        foreach (var ev in _plannerState.Events
            .Where(e => e.EventDateTime >= now.AddHours(-1))
            .OrderBy(e => e.EventDateTime)
            .Take(8))
        {
            UpcomingEvents.Add(ev);
        }

        OnPropertyChanged(nameof(HasSelectedDateEvents));
        OnPropertyChanged(nameof(UpcomingEventsCount));
    }

    private void ApplyDashboardFilter()
    {
        FilteredRecentFiles.Clear();
        string query = DashboardSearchText?.Trim() ?? string.Empty;
        foreach (var item in RecentFiles)
        {
            if (string.IsNullOrEmpty(query) ||
                item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRecentFiles.Add(item);
            }
        }
    }

    private void NotifyWorkspaceVisibilityProperties()
    {
        OnPropertyChanged(nameof(HasOpenDocument));
        OnPropertyChanged(nameof(HasAnyTabs));
        OnPropertyChanged(nameof(ShowHomeDashboard));
        OnPropertyChanged(nameof(ShowNotesStudio));
        OnPropertyChanged(nameof(ShowNewsFeed));
        OnPropertyChanged(nameof(ShowNcertLibrary));
        OnPropertyChanged(nameof(ShowDocumentReader));
        AiAssistant?.UpdatePdfContextStatus(ShowDocumentReader && SelectedTab is not null);
    }

    private static string FormatDurationFriendly(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.Minutes}m {ts.Seconds:D2}s";
        }

        return $"{ts.Seconds}s";
    }

    public void Dispose()
    {
        StopPdfSpeech();
        _stopwatchTimer.Stop();
        _reminderTimer.Stop();
        NewsFeed.Dispose();
        AiAssistant.Dispose();
        _voiceReadoutService.Dispose();
        _llamaAutoSetupService.Dispose();
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }

        Tabs.Clear();
    }
}
