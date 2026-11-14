using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed partial class AiAssistantViewModel : ObservableObject, IDisposable
{
    private readonly IAiSettingsService _settingsService;
    private readonly IAiChatService _chatService;
    private readonly IWebResearchService _webResearchService;
    private readonly IVoiceReadoutService _voiceReadoutService;
    private readonly IAiChatHistoryService _chatHistoryService;
    private CancellationTokenSource? _activeRequestCts;
    private CancellationTokenSource? _speechCts;
    private AiChatMessage? _speakingMessage;
    private bool _isSyncingSettings;

    public ObservableCollection<AiChatMessage> Messages { get; } = [];

    [ObservableProperty]
    private bool _isHistoryOpen;

    [ObservableProperty]
    private string _historySearchQuery = string.Empty;

    [ObservableProperty]
    private AiChatSession? _currentSession;

    public ObservableCollection<AiChatSession> AllSessions { get; } = [];
    public ObservableCollection<AiChatSession> FilteredSessions { get; } = [];
    public bool HasNoHistorySessions => FilteredSessions.Count == 0;
    public bool HasHistorySessions => FilteredSessions.Count > 0;

    [ObservableProperty]
    private AiAssistantSettings _settings;

    [ObservableProperty]
    private string _promptText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _usePdfContext = true;

    [ObservableProperty]
    private bool _useWebResearch;

    [ObservableProperty]
    private AiPersonaPresetKind _selectedPersonaPreset = AiPersonaPresetKind.General;

    [ObservableProperty]
    private AiAnswerLength _selectedAnswerLength = AiAnswerLength.Balanced;

    [ObservableProperty]
    private VoiceReadoutMode _selectedVoiceMode = VoiceReadoutMode.HindiDirect;

    [ObservableProperty]
    private string _statusText = "Ready";

    public string ActiveProviderLabel =>
        $"{AiProviderDefaults.GetPreset(Settings.Provider).DisplayName} • {Settings.ModelName}";

    public Func<Task<string?>>? GetActivePdfContextAsync { get; set; }
    public Func<bool>? HasOpenPdfProvider { get; set; }
    public Func<string, CancellationToken, Task<string>>? SearchPdfToolAsync { get; set; }
    public Func<int, CancellationToken, Task<string>>? ReadPdfPageToolAsync { get; set; }
    public event Action<string>? InsertIntoNotesRequested;
    public event Action? OpenSettingsRequested;
    public event Action? MessageAdded;
    public event Action? StopOtherSpeechRequested;

    public AiAssistantViewModel(
        IAiSettingsService settingsService,
        IAiChatService chatService,
        IWebResearchService webResearchService,
        IVoiceReadoutService? voiceReadoutService = null,
        IAiChatHistoryService? chatHistoryService = null)
    {
        _settingsService = settingsService;
        _chatService = chatService;
        _webResearchService = webResearchService;
        _voiceReadoutService = voiceReadoutService ?? new VoiceReadoutService();
        _chatHistoryService = chatHistoryService ?? new AiChatHistoryService();

        _settings = _settingsService.Load();
        _useWebResearch = _settings.UseInternetByDefault;
        SyncPersonalisationFromSettings();
        CurrentSession = new AiChatSession { Title = "New Study Chat" };
        AddWelcomeMessage();
        _ = LoadSessionsListAsync();
    }

    public void UpdateSettings(AiAssistantSettings updated)
    {
        Settings = updated.Clone();
        _settingsService.Save(Settings);
        UseWebResearch = Settings.UseInternetByDefault;
        SyncPersonalisationFromSettings();
        OnPropertyChanged(nameof(ActiveProviderLabel));
        StatusText = $"Configured: {ActiveProviderLabel}";
    }

    private void SyncPersonalisationFromSettings()
    {
        _isSyncingSettings = true;
        try
        {
            SelectedPersonaPreset = Settings.PersonaPreset;
            SelectedAnswerLength = Settings.AnswerLength;
            SelectedVoiceMode = Settings.VoiceMode;
        }
        finally
        {
            _isSyncingSettings = false;
        }
    }

    partial void OnSelectedPersonaPresetChanged(AiPersonaPresetKind value)
    {
        if (_isSyncingSettings)
        {
            return;
        }

        Settings.PersonaPreset = value;
        if (value != AiPersonaPresetKind.Custom)
        {
            Settings.SystemPrompt = AiPersonalisationDefaults.GetPersonaPreset(value).PromptText;
        }

        _settingsService.Save(Settings);
        var preset = AiPersonalisationDefaults.GetPersonaPreset(value);
        var length = AiPersonalisationDefaults.GetLengthOption(SelectedAnswerLength);
        StatusText = $"Preset: {preset.ShortLabel} • {length.DisplayName}";
    }

    partial void OnSelectedAnswerLengthChanged(AiAnswerLength value)
    {
        if (_isSyncingSettings)
        {
            return;
        }

        Settings.AnswerLength = value;
        _settingsService.Save(Settings);
        var preset = AiPersonalisationDefaults.GetPersonaPreset(SelectedPersonaPreset);
        var length = AiPersonalisationDefaults.GetLengthOption(value);
        StatusText = $"Preset: {preset.ShortLabel} • {length.DisplayName}";
    }

    partial void OnSelectedVoiceModeChanged(VoiceReadoutMode value)
    {
        if (_isSyncingSettings)
        {
            return;
        }

        Settings.VoiceMode = value;
        _settingsService.Save(Settings);
        StatusText = $"Voice: {AiPersonalisationDefaults.GetVoiceOption(value).ShortLabel}";
    }

    partial void OnHistorySearchQueryChanged(string value)
    {
        ApplyHistoryFilter();
    }

    private void ApplyHistoryFilter()
    {
        FilteredSessions.Clear();
        string query = HistorySearchQuery?.Trim() ?? string.Empty;
        var matches = string.IsNullOrWhiteSpace(query)
            ? AllSessions
            : AllSessions.Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var s in matches)
        {
            FilteredSessions.Add(s);
        }

        OnPropertyChanged(nameof(HasNoHistorySessions));
        OnPropertyChanged(nameof(HasHistorySessions));
    }

    public async Task LoadSessionsListAsync()
    {
        try
        {
            var sessions = await _chatHistoryService.LoadSessionsAsync().ConfigureAwait(false);
            RunOnUi(() =>
            {
                AllSessions.Clear();
                foreach (var s in sessions)
                {
                    AllSessions.Add(s);
                }
                ApplyHistoryFilter();
            });
        }
        catch
        {
            // Ignore
        }
    }

    [RelayCommand]
    public async Task ToggleHistoryAsync()
    {
        IsHistoryOpen = !IsHistoryOpen;
        if (IsHistoryOpen)
        {
            await LoadSessionsListAsync();
        }
    }

    [RelayCommand]
    public async Task NewChatAsync()
    {
        await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);
        CurrentSession = new AiChatSession { Title = "New Study Chat" };
        RunOnUi(() =>
        {
            Messages.Clear();
            AddWelcomeMessage();
            IsHistoryOpen = false;
            StatusText = "New study chat started";
            MessageAdded?.Invoke();
        });
    }

    [RelayCommand]
    public async Task SelectSessionAsync(AiChatSession? session)
    {
        if (session is null) return;
        await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);

        CurrentSession = session;
        RunOnUi(() =>
        {
            Messages.Clear();
            foreach (var m in session.Messages)
            {
                Messages.Add(m);
            }

            if (Messages.Count == 0)
            {
                AddWelcomeMessage();
            }

            IsHistoryOpen = false;
            StatusText = $"Loaded chat: {session.Title}";
            MessageAdded?.Invoke();
        });
    }

    [RelayCommand]
    public async Task DeleteSessionAsync(AiChatSession? session)
    {
        if (session is null) return;
        await _chatHistoryService.DeleteSessionAsync(session.Id).ConfigureAwait(false);
        RunOnUi(() =>
        {
            AllSessions.Remove(session);
            FilteredSessions.Remove(session);
            OnPropertyChanged(nameof(HasNoHistorySessions));
            OnPropertyChanged(nameof(HasHistorySessions));
        });

        if (CurrentSession?.Id == session.Id)
        {
            await NewChatAsync();
        }
    }

    [RelayCommand]
    public async Task ClearAllHistoryAsync()
    {
        await _chatHistoryService.ClearAllSessionsAsync().ConfigureAwait(false);
        RunOnUi(() =>
        {
            AllSessions.Clear();
            FilteredSessions.Clear();
            OnPropertyChanged(nameof(HasNoHistorySessions));
            OnPropertyChanged(nameof(HasHistorySessions));
        });
        await NewChatAsync();
    }

    private async Task SaveCurrentSessionToHistoryAsync()
    {
        try
        {
            if (CurrentSession is null)
            {
                CurrentSession = new AiChatSession { Title = "New Study Chat" };
            }

            var nonWelcome = Messages.Where(m => !m.IsWelcome && !m.IsError).ToList();
            if (nonWelcome.Count == 0) return;

            if (CurrentSession.Title == "New Study Chat" || string.IsNullOrWhiteSpace(CurrentSession.Title))
            {
                var firstUser = nonWelcome.FirstOrDefault(m => m.IsUser);
                if (firstUser is not null && !string.IsNullOrWhiteSpace(firstUser.Content))
                {
                    string clean = firstUser.Content.Trim().Replace('\r', ' ').Replace('\n', ' ');
                    CurrentSession.Title = clean.Length > 48 ? $"{clean[..45]}…" : clean;
                }
            }

            CurrentSession.Messages.Clear();
            foreach (var m in Messages)
            {
                CurrentSession.Messages.Add(m);
            }
            CurrentSession.MessageCount = nonWelcome.Count;
            CurrentSession.LastUpdatedAtUtc = DateTime.UtcNow;

            await _chatHistoryService.SaveSessionAsync(CurrentSession).ConfigureAwait(false);

            RunOnUi(() =>
            {
                var existing = AllSessions.FirstOrDefault(s => s.Id == CurrentSession.Id);
                if (existing is null)
                {
                    AllSessions.Insert(0, CurrentSession);
                }
                else
                {
                    existing.Title = CurrentSession.Title;
                    existing.LastUpdatedAtUtc = CurrentSession.LastUpdatedAtUtc;
                    existing.MessageCount = CurrentSession.MessageCount;
                }
                ApplyHistoryFilter();
            });
        }
        catch
        {
            // Safeguard against I/O interruption
        }
    }

    [RelayCommand]
    public async Task ToggleSpeakMessageAsync(AiChatMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        if (message.IsSpeaking)
        {
            StopSpeaking();
            StatusText = "Voice readout stopped";
            return;
        }

        StopOtherSpeechRequested?.Invoke();
        StopSpeaking();
        _speakingMessage = message;
        message.IsSpeaking = true;
        _speechCts = new CancellationTokenSource();
        var ct = _speechCts.Token;

        var voiceOption = AiPersonalisationDefaults.GetVoiceOption(SelectedVoiceMode);
        StatusText = $"Reading aloud ({voiceOption.ShortLabel})...";

        try
        {
            await _voiceReadoutService.SpeakAsync(
                message.Content,
                SelectedVoiceMode,
                Settings.VoiceSpeed,
                Settings.VoicePitch,
                Settings.VoiceModel,
                ct);
            if (!ct.IsCancellationRequested)
            {
                StatusText = "Ready";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Voice readout stopped";
        }
        catch (Exception ex)
        {
            StatusText = $"Voice error: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_speakingMessage, message))
            {
                _speakingMessage = null;
            }

            message.IsSpeaking = false;
        }
    }

    [RelayCommand]
    public void StopSpeaking()
    {
        try
        {
            _speechCts?.Cancel();
            _speechCts?.Dispose();
            _speechCts = null;
        }
        catch
        {
        }

        _voiceReadoutService.Stop();
        if (_speakingMessage is not null)
        {
            _speakingMessage.IsSpeaking = false;
            _speakingMessage = null;
        }

        StatusText = "Voice readout stopped";
    }

    [RelayCommand]
    public void OpenSettings() => OpenSettingsRequested?.Invoke();

    [RelayCommand]
    public async Task SendQuickPromptAsync(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || IsBusy)
        {
            return;
        }

        PromptText = prompt;
        await SendMessageAsync();
    }

    [RelayCommand]
    public async Task SendMessageAsync()
    {
        string question = PromptText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(question) || IsBusy)
        {
            return;
        }

        PromptText = string.Empty;
        _activeRequestCts?.Cancel();
        _activeRequestCts?.Dispose();
        _activeRequestCts = new CancellationTokenSource();
        var ct = _activeRequestCts.Token;

        IsBusy = true;
        bool casual = AiChatService.IsCasualMessage(question);
        bool hasPdf = HasOpenPdfProvider?.Invoke() ?? (GetActivePdfContextAsync is not null);

        var userMessage = new AiChatMessage
        {
            Role = "user",
            Content = question
        };

        var historySnapshot = Messages.ToList();
        Messages.Add(userMessage);

        var streamingMessage = new AiChatMessage
        {
            Role = "assistant",
            Content = string.Empty
        };
        Messages.Add(streamingMessage);
        MessageAdded?.Invoke();

        try
        {
            string? pdfContext = null;
            if (!casual && UsePdfContext && GetActivePdfContextAsync is not null)
            {
                StatusText = "Reading active PDF context...";
                pdfContext = await GetActivePdfContextAsync();
                if (!string.IsNullOrWhiteSpace(pdfContext))
                {
                    streamingMessage.UsedPdfContext = true;
                }
            }

            var collectedSources = new List<WebSourceReference>();
            object sourcesLock = new();
            var toolHost = new AiToolHost
            {
                HasOpenPdf = !casual && UsePdfContext && hasPdf,
                AllowWebSearch = !casual && UseWebResearch,
                AllowTemporalTools = !casual,
                ExecutePdfSearchAsync = SearchPdfToolAsync,
                ExecuteReadPdfPageAsync = ReadPdfPageToolAsync,
                ExecuteGetCurrentTimeAsync = (tz, token) => Task.FromResult(AiChatService.FormatCurrentTimeResponse(tzOrFormat: tz)),
                ExecuteGetTodayOverviewAsync = token => Task.FromResult(AiChatService.FormatTodayOverviewResponse()),
                ExecuteGetCalendarInfoAsync = (target, offset, month, year, token) => Task.FromResult(AiChatService.FormatCalendarInfoResponse(target, offset, month, year)),
                ExecuteWebSearchAsync = async (q, token) =>
                {
                    string effectiveQ = WebResearchService.SanitizeAndGroundTemporalQuery(q);
                    if (!string.Equals(q, effectiveQ, StringComparison.OrdinalIgnoreCase))
                    {
                        RunOnUi(() => StatusText = $"Searching web for \"{effectiveQ}\" (grounded to {DateTime.Now.Year})...");
                    }

                    var found = await _webResearchService.SearchAsync(effectiveQ, Settings, token).ConfigureAwait(false);
                    if (found.Count == 0)
                    {
                        return ("No web search results found.", found);
                    }

                    var sb = new StringBuilder();
                    foreach (var s in found)
                    {
                        sb.AppendLine($"[{s.Index}] {s.Title} ({s.Url})");
                        if (!string.IsNullOrWhiteSpace(s.Snippet))
                        {
                            sb.AppendLine($"    {s.Snippet}");
                        }
                    }

                    return (sb.ToString(), found);
                },
                OnToolStatus = status => RunOnUi(() => StatusText = status),
                OnToolUsageRecorded = (usedPdf, usedWeb, usedTime) => RunOnUi(() =>
                {
                    if (usedPdf) streamingMessage.UsedPdfContext = true;
                    if (usedWeb) streamingMessage.UsedWebResearch = true;
                    if (usedTime) streamingMessage.UsedTimeContext = true;
                }),
                OnWebSourcesCollected = sources => RunOnUi(() =>
                {
                    lock (sourcesLock)
                    {
                        foreach (var src in sources)
                        {
                            if (!collectedSources.Any(c => string.Equals(c.Url, src.Url, StringComparison.OrdinalIgnoreCase)))
                            {
                                collectedSources.Add(src);
                                streamingMessage.Sources.Add(new WebSourceReference
                                {
                                    Index = collectedSources.Count,
                                    Title = src.Title,
                                    Url = src.Url,
                                    Snippet = src.Snippet
                                });
                            }
                        }
                    }
                })
            };

            StatusText = $"Thinking with {Settings.ModelName}...";
            var streamBuffer = new StringBuilder();
            object streamLock = new();
            long lastUiPushTick = 0;

            string finalReply = await Task.Run(
                () => _chatService.SendChatWithToolsAsync(
                    Settings,
                    historySnapshot,
                    question,
                    pdfContext,
                    toolHost,
                    onStreamDelta: delta =>
                    {
                        string? snapshotToPush = null;
                        lock (streamLock)
                        {
                            streamBuffer.Append(delta);
                            long now = Environment.TickCount64;
                            if (now - lastUiPushTick >= 90)
                            {
                                lastUiPushTick = now;
                                snapshotToPush = streamBuffer.ToString();
                            }
                        }

                        if (snapshotToPush is not null)
                        {
                            RunOnUi(() =>
                            {
                                streamingMessage.Content = snapshotToPush;
                                MessageAdded?.Invoke();
                            });
                        }
                    },
                    ct),
                ct);

            if (!string.IsNullOrWhiteSpace(finalReply))
            {
                streamingMessage.Content = finalReply;
            }
            else if (string.IsNullOrWhiteSpace(streamingMessage.Content))
            {
                streamingMessage.Content = "(No response text returned by model.)";
            }

            MessageAdded?.Invoke();
            StatusText = streamingMessage.Sources.Count > 0
                ? $"Answered with {streamingMessage.Sources.Count} web source(s)"
                : "Ready";
            await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (string.IsNullOrWhiteSpace(streamingMessage.Content))
            {
                Messages.Remove(streamingMessage);
            }

            StatusText = "Request cancelled";
        }
        catch (Exception ex)
        {
            Messages.Remove(streamingMessage);
            Messages.Add(new AiChatMessage
            {
                Role = "assistant",
                Content = $"Could not reach **{ActiveProviderLabel}**.\n\n`{ex.Message}`",
                IsError = true
            });
            MessageAdded?.Invoke();
            StatusText = "Request failed";
            await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void StopRequest()
    {
        _activeRequestCts?.Cancel();
        StatusText = "Cancelling...";
    }

    [RelayCommand]
    public async Task ClearChatAsync()
    {
        _activeRequestCts?.Cancel();
        StopSpeaking();
        await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);
        CurrentSession = new AiChatSession { Title = "New Study Chat" };
        RunOnUi(() =>
        {
            Messages.Clear();
            AddWelcomeMessage();
            StatusText = "Cleared and archived to history";
            MessageAdded?.Invoke();
        });
    }

    [RelayCommand]
    public void CopyMessage(AiChatMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        Clipboard.SetText(message.Content);
        StatusText = "Copied message to clipboard";
    }

    [RelayCommand]
    public void SendToNotes(AiChatMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        InsertIntoNotesRequested?.Invoke(message.Content);
        StatusText = "Inserted AI response into active note";
    }

    private void AddWelcomeMessage()
    {
        Messages.Add(new AiChatMessage
        {
            Role = "assistant",
            IsWelcome = true,
            Content =
                "Hi! I'm **StudySuit AI**.\n\n" +
                "- Ask questions about your open **PDF** (`pdf_search` & `read_pdf_page` tools)\n" +
                "- Toggle **Web Research** when you want live online citations (`web_search` tool)\n" +
                "- Supports **Markdown**, tables, code, **LaTeX equations**, **charts/graphs**, and **pictures**"
        });
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        StopSpeaking();
        _voiceReadoutService.Dispose();
        _activeRequestCts?.Cancel();
        _activeRequestCts?.Dispose();
    }
}
