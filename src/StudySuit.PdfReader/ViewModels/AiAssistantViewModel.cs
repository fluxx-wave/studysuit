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
    private readonly IWebpageFetchService _webpageFetchService;
    private readonly ISketchfab3DService _sketchfab3DService;
    private readonly IAiChatHistoryService _chatHistoryService;
    private readonly ILlamaCppAutoSetupService? _llamaService;
    private readonly object _messagesLock = new();
    private readonly object _sessionsLock = new();
    private CancellationTokenSource? _activeRequestCts;
    private bool _isSyncingSettings;

    public IWebResearchService WebResearchService => _webResearchService;

    public ObservableCollection<AiChatMessage> Messages { get; } = [];
    public bool HasMessages => Messages.Count > 0;
    public bool HasNoMessages => Messages.Count == 0;

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
    private bool _usePdfContext = false;

    [ObservableProperty]
    private bool _useWebResearch = true;

    [ObservableProperty]
    private bool _useSketchfab3D = true;

    [ObservableProperty]
    private bool _useDiagramsAndCharts = true;

    [ObservableProperty]
    private AiPersonaPresetKind _selectedPersonaPreset = AiPersonaPresetKind.General;

    [ObservableProperty]
    private AiAnswerLength _selectedAnswerLength = AiAnswerLength.Balanced;

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

    public AiAssistantViewModel(
        IAiSettingsService settingsService,
        IAiChatService chatService,
        IWebResearchService webResearchService,
        IWebpageFetchService? webpageFetchService = null,
        ISketchfab3DService? sketchfab3DService = null,
        IAiChatHistoryService? chatHistoryService = null,
        ILlamaCppAutoSetupService? llamaService = null)
    {
        _settingsService = settingsService;
        _chatService = chatService;
        _webResearchService = webResearchService;
        _webpageFetchService = webpageFetchService ?? new WebpageFetchService();
        _sketchfab3DService = sketchfab3DService ?? new Sketchfab3DService();
        _chatHistoryService = chatHistoryService ?? new AiChatHistoryService();
        _llamaService = llamaService;

        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(Messages, _messagesLock);
        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(AllSessions, _sessionsLock);
        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(FilteredSessions, _sessionsLock);

        _settings = _settingsService.Load();
        _useWebResearch = _settings.UseInternetByDefault && _settings.EnableWebSearch;
        _useSketchfab3D = _settings.EnableSketchfab3D;
        _useDiagramsAndCharts = _settings.EnableMermaidDiagrams;
        SyncPersonalisationFromSettings();
        CurrentSession = new AiChatSession { Title = "New Study Chat" };
        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(HasNoMessages));
        };
        _ = LoadSessionsListAsync();
    }

    public void UpdateSettings(AiAssistantSettings updated)
    {
        Settings = updated.Clone();
        _settingsService.Save(Settings);
        UseWebResearch = Settings.UseInternetByDefault && Settings.EnableWebSearch;
        UseSketchfab3D = Settings.EnableSketchfab3D;
        UseDiagramsAndCharts = Settings.EnableMermaidDiagrams;
        if (Settings.AutoReferenceOpenPdf && (HasOpenPdfProvider?.Invoke() == true))
        {
            UsePdfContext = true;
        }
        else if (!Settings.AutoReferenceOpenPdf)
        {
            UsePdfContext = false;
        }
        SyncPersonalisationFromSettings();
        OnPropertyChanged(nameof(ActiveProviderLabel));
        StatusText = $"Configured: {ActiveProviderLabel}";
    }

    public void UpdatePdfContextStatus(bool isPdfViewed)
    {
        if (Settings.AutoReferenceOpenPdf)
        {
            UsePdfContext = isPdfViewed;
        }
    }

    private void SyncPersonalisationFromSettings()
    {
        _isSyncingSettings = true;
        try
        {
            SelectedPersonaPreset = Settings.PersonaPreset;
            SelectedAnswerLength = Settings.AnswerLength;
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
        StatusText = $"Persona: {preset.DisplayName} ({length.DisplayName})";
    }

    partial void OnSelectedAnswerLengthChanged(AiAnswerLength value)
    {
        if (_isSyncingSettings)
        {
            return;
        }

        Settings.AnswerLength = value;
        _settingsService.Save(Settings);
        var length = AiPersonalisationDefaults.GetLengthOption(value);
        StatusText = $"Answer length: {length.DisplayName}";
    }

    [RelayCommand]
    public void OpenSettings() => OpenSettingsRequested?.Invoke();

    public void StopSpeaking() { }
    public VoiceReadoutMode SelectedVoiceMode => VoiceReadoutMode.English;

    [RelayCommand]
    public async Task SendMessageAsync() => await SendPromptAsync();

    public async Task StartCircleToSearchSessionAsync(string sessionTitle, string fullPrompt)
    {
        if (CurrentSession is not null)
        {
            CurrentSession.Title = sessionTitle;
        }
        await SendExternalPromptAsync(fullPrompt);
    }

    [RelayCommand]
    public async Task SendPromptAsync()
    {
        string prompt = PromptText.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || IsBusy)
        {
            return;
        }

        PromptText = string.Empty;
        await SendPromptCoreAsync(prompt);
    }

    [RelayCommand]
    public async Task SendQuickPromptAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || IsBusy)
        {
            return;
        }

        await SendPromptCoreAsync(prompt.Trim());
    }

    public async Task SendExternalPromptAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        await SendPromptCoreAsync(prompt.Trim());
    }

    public async Task SendExplainConceptPromptAsync(string concept)
    {
        if (string.IsNullOrWhiteSpace(concept)) return;
        string prompt = $"Explain the concept '{concept}' thoroughly with intuitive examples, core formulas/principles, and real-world applications.";
        await SendPromptCoreAsync(prompt);
    }

    public async Task SendQuizPromptAsync(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;
        string prompt = $"Generate 3 conceptual practice questions with detailed solutions about '{topic}'.";
        await SendPromptCoreAsync(prompt);
    }

    public async Task SendNewsArticleDiscussionAsync(NewsFeedItem article)
    {
        if (article is null) return;
        string prompt = $"Analyze this current affairs news story:\n\n**Headline:** {article.Title}\n**Source:** {article.Source}\n**Summary:** {article.Summary}\n\nProvide key background context, relevance to competitive exams/general knowledge, and critical takeaways.";
        await SendPromptCoreAsync(prompt);
    }

    public async Task SendAnnotationDiscussionAsync(string annotationText, string noteText)
    {
        string prompt = string.IsNullOrWhiteSpace(noteText)
            ? $"I highlighted this passage in the document:\n\"{annotationText}\"\n\nPlease analyze, clarify the concepts, and explain why it is significant."
            : $"Regarding this passage:\n\"{annotationText}\"\n\nMy note was: \"{noteText}\"\n\nPlease answer my question or expand on this thought.";
        await SendPromptCoreAsync(prompt);
    }

    public async Task SendFormulaEvaluationPromptAsync(string formula, double? evaluationResult)
    {
        string prompt = evaluationResult.HasValue
            ? $"Explain the mathematical formula:\n`{formula}`\n\nComputed value: `{evaluationResult.Value:G6}`. Explain its derivation, meaning of variables, and practical usage."
            : $"Explain the mathematical formula:\n`{formula}`\n\nExplain its derivation, units, variables, and how to apply it.";
        await SendPromptCoreAsync(prompt);
    }

    private async Task SendPromptCoreAsync(string question)
    {
        IsBusy = true;
        StatusText = "Thinking...";
        _activeRequestCts?.Cancel();
        _activeRequestCts = new CancellationTokenSource();
        var ct = _activeRequestCts.Token;

        var userMessage = new AiChatMessage
        {
            Role = "user",
            Content = question,
            TimestampUtc = DateTime.UtcNow
        };
        Messages.Add(userMessage);

        var streamingMessage = new AiChatMessage
        {
            Role = "assistant",
            Content = "...",
            IsStreaming = true,
            TimestampUtc = DateTime.UtcNow
        };
        Messages.Add(streamingMessage);
        MessageAdded?.Invoke();

        try
        {
            string? pdfContext = null;
            if (UsePdfContext && GetActivePdfContextAsync is not null)
            {
                StatusText = "Reading open document context...";
                pdfContext = await GetActivePdfContextAsync().ConfigureAwait(false);
                streamingMessage.UsedPdfContext = !string.IsNullOrWhiteSpace(pdfContext);
            }

            var historySnapshot = Messages
                .Where(m => m != streamingMessage && !m.IsWelcome && !m.IsError)
                .ToList();

            var collectedSources = new List<WebSourceReference>();
            var sourcesLock = new object();

            var toolHost = new AiToolHost
            {
                AllowWebSearch = Settings.EnableWebSearch && UseWebResearch,
                AllowFetchWebpage = Settings.EnableFetchWebpage,
                AllowSketchfab3D = Settings.EnableSketchfab3D && UseSketchfab3D,
                AllowImageSearch = Settings.EnableImageSearch,
                HasOpenPdf = HasOpenPdfProvider?.Invoke() == true,
                AllowTemporalTools = true,
                ExecuteWebSearchAsync = async (q, token) =>
                {
                    RunOnUi(() => StatusText = $"Searching web for \"{q}\"...");
                    var found = await _webResearchService.SearchAsync(q, Settings, token).ConfigureAwait(false);
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
                ExecuteFetchWebpageAsync = async (url, token) =>
                {
                    RunOnUi(() => StatusText = $"Fetching webpage {url}...");
                    return await _webpageFetchService.FetchPageContentAsync(url, token).ConfigureAwait(false);
                },
                ExecuteSketchfab3DSearchAsync = async (q, token) =>
                {
                    RunOnUi(() => StatusText = $"Searching Sketchfab 3D for \"{q}\"...");
                    return await _sketchfab3DService.SearchModelAsync(q, token).ConfigureAwait(false);
                },
                ExecuteImageSearchAsync = async (q, token) =>
                {
                    RunOnUi(() => StatusText = $"Searching images for \"{q}\"...");
                    var foundImages = await _webResearchService.SearchImagesAsync(q, Settings, token).ConfigureAwait(false);
                    if (foundImages.Count == 0)
                    {
                        return ($"No image search results found for \"{q}\".", foundImages);
                    }

                    var sb = new StringBuilder();
                    sb.AppendLine($"Found {foundImages.Count} relevant image(s) for \"{q}\":\n");
                    foreach (var img in foundImages)
                    {
                        sb.AppendLine($"[Image {img.Index}]: {img.Title}");
                        sb.AppendLine($"    Direct Image URL: {img.ImageUrl}");
                        sb.AppendLine($"    Embed in Markdown: ![{img.Title}]({img.ImageUrl})\n");
                    }
                    sb.AppendLine("[MANDATORY INSTRUCTION]: Select the best image(s) above and embed into your response using: `![Descriptive Caption](ImageURL)`.");
                    return (sb.ToString(), foundImages);
                },
                ExecutePdfSearchAsync = SearchPdfToolAsync,
                ExecuteReadPdfPageAsync = ReadPdfPageToolAsync,
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
                }),
                OnImagesCollected = images => RunOnUi(() =>
                {
                    foreach (var img in images)
                    {
                        if (!streamingMessage.Images.Any(i => string.Equals(i.ImageUrl, img.ImageUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            streamingMessage.Images.Add(img);
                            streamingMessage.UsedWebResearch = true;
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
                            });
                        }
                    },
                    cancellationToken: ct),
                ct);

            if (!string.IsNullOrWhiteSpace(finalReply))
            {
                streamingMessage.Content = finalReply;
            }
            else if (string.IsNullOrWhiteSpace(streamingMessage.Content))
            {
                streamingMessage.Content = "(No response text returned by model.)";
            }
            streamingMessage.IsStreaming = false;

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
            bool isLocal = Settings.Provider is AiProviderKind.OllamaLocal or AiProviderKind.LmStudioLocal or AiProviderKind.LlamaCppLocal;
            string errContent;
            if (isLocal)
            {
                string configuredModel = string.IsNullOrWhiteSpace(Settings.ModelName) ? "(no model specified)" : Settings.ModelName;
                errContent =
                    $"### ⚠️ Could not reach Local LLM ({ActiveProviderLabel})\n\n" +
                    $"**Endpoint:** `{Settings.BaseUrl}` • **Model:** `{configuredModel}`\n\n" +
                    $"**Details:** `{ex.Message}`\n\n" +
                    "**To start your local model:**\n" +
                    "- **Ollama:** In terminal, run `ollama run <model>` (e.g. `ollama run llama3.2`)\n" +
                    "- **LM Studio:** Start local server at port `1234`\n" +
                    "- **llama.cpp:** Run `llama-server.exe` at port `8080`\n" +
                    "- Or switch provider in ⚙️ Settings (top-right of AI panel).";
            }
            else
            {
                errContent =
                    $"### ⚠️ AI Request Failed\n\n" +
                    $"**Provider:** {Settings.Provider} • **Model:** `{Settings.ModelName}`\n\n" +
                    $"**Error:** `{ex.Message}`\n\n" +
                    "Check your internet connection, API Key in Settings ⚙️, or try another provider.";
            }

            var errMsg = new AiChatMessage
            {
                Role = "assistant",
                Content = errContent,
                IsError = true,
                TimestampUtc = DateTime.UtcNow
            };
            Messages.Add(errMsg);
            StatusText = "Error during request";
            MessageAdded?.Invoke();
        }
        finally
        {
            streamingMessage.IsStreaming = false;
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
        await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);
        CurrentSession = new AiChatSession { Title = "New Study Chat" };
        Active3DViewPool.ClearAndDisposeAll();
        RunOnUi(() =>
        {
            Messages.Clear();
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

    [RelayCommand]
    public void ToggleHistory()
    {
        IsHistoryOpen = !IsHistoryOpen;
        if (IsHistoryOpen)
        {
            _ = LoadSessionsListAsync();
        }
    }

    [RelayCommand]
    public void CloseHistory() => IsHistoryOpen = false;

    [RelayCommand]
    public async Task SelectSessionAsync(AiChatSession? session) => await LoadSessionAsync(session);

    [RelayCommand]
    public async Task LoadSessionAsync(AiChatSession? session)
    {
        if (session is null) return;
        IsBusy = true;
        StatusText = "Loading chat session...";
        try
        {
            if (Messages.Count > 0 && CurrentSession is not null && CurrentSession.Id != session.Id)
            {
                await SaveCurrentSessionToHistoryAsync().ConfigureAwait(false);
            }

            var loaded = await _chatHistoryService.LoadSessionAsync(session.Id).ConfigureAwait(false);
            var target = loaded ?? session;
            if (target is not null)
            {
                Active3DViewPool.ClearAndDisposeAll();
                RunOnUi(() =>
                {
                    CurrentSession = target;
                    Messages.Clear();
                    foreach (var m in target.Messages)
                    {
                        m.IsStreaming = false;
                        Messages.Add(m);
                    }
                    IsHistoryOpen = false;
                    StatusText = $"Loaded \"{target.Title}\" ({target.Messages.Count} messages)";
                    MessageAdded?.Invoke();
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load session: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSessionAsync(AiChatSession? session)
    {
        if (session is null) return;
        try
        {
            await _chatHistoryService.DeleteSessionAsync(session.Id).ConfigureAwait(false);
            RunOnUi(() =>
            {
                AllSessions.Remove(session);
                FilteredSessions.Remove(session);
                if (CurrentSession?.Id == session.Id)
                {
                    CurrentSession = new AiChatSession { Title = "New Study Chat" };
                    Messages.Clear();
                }
                ApplyHistoryFilter();
                StatusText = "Deleted session";
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Delete error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task NewChatAsync()
    {
        await ClearChatAsync();
        IsHistoryOpen = false;
    }

    private async Task LoadSessionsListAsync()
    {
        try
        {
            var list = await _chatHistoryService.LoadSessionsAsync().ConfigureAwait(false);
            RunOnUi(() =>
            {
                AllSessions.Clear();
                foreach (var s in list)
                {
                    AllSessions.Add(s);
                }
                ApplyHistoryFilter();
            });
        }
        catch
        {
        }
    }

    partial void OnHistorySearchQueryChanged(string value) => ApplyHistoryFilter();

    private void ApplyHistoryFilter()
    {
        string q = HistorySearchQuery?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(q)
            ? AllSessions.ToList()
            : AllSessions.Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     s.PreviewText.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        FilteredSessions.Clear();
        foreach (var s in filtered)
        {
            FilteredSessions.Add(s);
        }
        OnPropertyChanged(nameof(HasNoHistorySessions));
        OnPropertyChanged(nameof(HasHistorySessions));
    }

    private async Task SaveCurrentSessionToHistoryAsync()
    {
        try
        {
            if (CurrentSession is null) return;
            if (Messages.Count == 0) return;

            if (CurrentSession.Title is "New Study Chat" or "New Chat" || string.IsNullOrWhiteSpace(CurrentSession.Title))
            {
                var firstUser = Messages.FirstOrDefault(m => m.IsUser && !string.IsNullOrWhiteSpace(m.Content));
                if (firstUser is not null)
                {
                    string preview = firstUser.Content.Trim();
                    if (preview.Length > 45) preview = preview[..45] + "...";
                    CurrentSession.Title = preview;
                }
            }

            CurrentSession.Messages.Clear();
            foreach (var m in Messages)
            {
                CurrentSession.Messages.Add(m);
            }
            CurrentSession.MessageCount = Messages.Count(m => !m.IsWelcome && !m.IsError);
            CurrentSession.LastUpdatedAtUtc = DateTime.UtcNow;

            await _chatHistoryService.SaveSessionAsync(CurrentSession).ConfigureAwait(false);

            RunOnUi(() =>
            {
                var existing = AllSessions.FirstOrDefault(s => s.Id == CurrentSession.Id);
                if (existing is not null)
                {
                    AllSessions.Remove(existing);
                }
                AllSessions.Insert(0, CurrentSession);
                ApplyHistoryFilter();
            });
        }
        catch
        {
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public void Dispose()
    {
        _activeRequestCts?.Cancel();
        _activeRequestCts?.Dispose();
    }
}
