using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed partial class NewsFeedViewModel : ObservableObject, IDisposable
{
    private readonly INewsFeedService _feedService;
    private readonly IVoiceReadoutService _voiceService;
    private List<NewsFeedItem> _allLoadedItems = [];
    private CancellationTokenSource? _fetchCts;
    private CancellationTokenSource? _articleContentCts;
    private CancellationTokenSource? _speechCts;
    private NewsFeedItem? _speakingItem;

    public ObservableCollection<NewsFeedItem> FilteredItems { get; } = [];

    [ObservableProperty]
    private NewsTimeframe _selectedTimeframe = NewsTimeframe.Daily;

    [ObservableProperty]
    private NewsCategory _selectedCategory = NewsCategory.TopStories;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Daily, Weekly & Monthly news feed • Click Refresh or select a timeframe";

    [ObservableProperty]
    private bool _hasLoadedOnce;

    [ObservableProperty]
    private NewsFeedItem? _selectedArticle;

    [ObservableProperty]
    private bool _isReaderOpen;

    [ObservableProperty]
    private bool _isWebMode = true;

    [ObservableProperty]
    private bool _isLoadingArticleContent;

    public bool HasItems => FilteredItems.Count > 0;
    public bool IsDailySelected => SelectedTimeframe == NewsTimeframe.Daily;
    public bool IsWeeklySelected => SelectedTimeframe == NewsTimeframe.Weekly;
    public bool IsMonthlySelected => SelectedTimeframe == NewsTimeframe.Monthly;

    public string ActiveArticleContent
    {
        get
        {
            if (SelectedArticle is null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(SelectedArticle.FullContent))
            {
                return SelectedArticle.FullContent;
            }

            if (!string.IsNullOrWhiteSpace(SelectedArticle.Summary))
            {
                return SelectedArticle.Summary;
            }

            return SelectedArticle.Title;
        }
    }

    public bool IsActiveArticleSpeaking => SelectedArticle?.IsSpeaking ?? false;

    public Func<VoiceReadoutMode>? VoiceModeProvider { get; set; }
    public Func<(double speed, int pitch, string? model)>? VoiceSettingsProvider { get; set; }
    public event Action<NewsFeedItem>? DiscussWithAiRequested;
    public event Action<NewsFeedItem, string>? DiscussCustomPromptRequested;
    public event Action? StopOtherSpeechRequested;
    public event Action<string>? OpenArticleWebRequested;
    public event Action? CloseReaderRequested;

    public NewsFeedViewModel(INewsFeedService? feedService = null, IVoiceReadoutService? voiceService = null)
    {
        _feedService = feedService ?? new NewsFeedService();
        _voiceService = voiceService ?? new VoiceReadoutService();
    }

    public async Task EnsureLoadedAsync()
    {
        if (!HasLoadedOnce && !IsLoading)
        {
            await LoadFeedAsync(forceRefresh: false);
        }
    }

    partial void OnSelectedTimeframeChanged(NewsTimeframe value)
    {
        OnPropertyChanged(nameof(IsDailySelected));
        OnPropertyChanged(nameof(IsWeeklySelected));
        OnPropertyChanged(nameof(IsMonthlySelected));
        _ = LoadFeedAsync(forceRefresh: false);
    }

    partial void OnSelectedCategoryChanged(NewsCategory value)
    {
        _ = LoadFeedAsync(forceRefresh: false);
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedArticleChanged(NewsFeedItem? value)
    {
        OnPropertyChanged(nameof(ActiveArticleContent));
        OnPropertyChanged(nameof(IsActiveArticleSpeaking));
    }

    [RelayCommand]
    public async Task SelectTimeframeAsync(NewsTimeframe timeframe)
    {
        if (SelectedTimeframe != timeframe)
        {
            SelectedTimeframe = timeframe;
        }
        else
        {
            await LoadFeedAsync(forceRefresh: false);
        }
    }

    [RelayCommand]
    public Task RefreshFeedAsync() => LoadFeedAsync(forceRefresh: true);

    public async Task LoadFeedAsync(bool forceRefresh)
    {
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;

        IsLoading = true;
        string timeframeTitle = SelectedTimeframe switch
        {
            NewsTimeframe.Daily => "Daily News",
            NewsTimeframe.Weekly => "Weekly News",
            _ => "Monthly News"
        };
        string catTitle = NewsFeedCatalog.GetCategory(SelectedCategory).Label;
        StatusText = $"Loading {timeframeTitle} ({catTitle})...";

        try
        {
            var items = await _feedService.FetchNewsAsync(SelectedTimeframe, SelectedCategory, forceRefresh, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _allLoadedItems = items.ToList();
            HasLoadedOnce = true;
            ApplyFilter();

            StatusText = FilteredItems.Count > 0
                ? $"{timeframeTitle} • {catTitle} • {FilteredItems.Count} stories (Updated {DateTime.Now:HH:mm})"
                : $"{timeframeTitle} • {catTitle} • No stories matched filter";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load {timeframeTitle}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        string q = SearchQuery?.Trim() ?? string.Empty;
        FilteredItems.Clear();

        IEnumerable<NewsFeedItem> source = string.IsNullOrWhiteSpace(q)
            ? _allLoadedItems
            : _allLoadedItems.Where(i =>
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Source.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var item in source)
        {
            FilteredItems.Add(item);
        }

        OnPropertyChanged(nameof(HasItems));
    }

    [RelayCommand]
    public async Task OpenArticleAsync(NewsFeedItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedArticle = item;
        IsReaderOpen = true;
        IsWebMode = true;
        OnPropertyChanged(nameof(ActiveArticleContent));
        OnPropertyChanged(nameof(IsActiveArticleSpeaking));

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            OpenArticleWebRequested?.Invoke(item.Url);
        }

        if (string.IsNullOrWhiteSpace(item.FullContent) && !string.IsNullOrWhiteSpace(item.Url))
        {
            _articleContentCts?.Cancel();
            _articleContentCts?.Dispose();
            _articleContentCts = new CancellationTokenSource();
            var ct = _articleContentCts.Token;

            IsLoadingArticleContent = true;
            try
            {
                string? fetched = await _feedService.FetchArticleContentAsync(item.Url, ct);
                if (!string.IsNullOrWhiteSpace(fetched))
                {
                    item.FullContent = fetched;
                }
                else
                {
                    item.FullContent = BuildFallbackArticleText(item);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                item.FullContent = BuildFallbackArticleText(item);
            }
            finally
            {
                IsLoadingArticleContent = false;
                OnPropertyChanged(nameof(ActiveArticleContent));
            }
        }
    }

    [RelayCommand]
    public void CloseReader()
    {
        StopArticleReadout();
        _articleContentCts?.Cancel();
        IsReaderOpen = false;
        SelectedArticle = null;
        OnPropertyChanged(nameof(ActiveArticleContent));
        OnPropertyChanged(nameof(IsActiveArticleSpeaking));
        CloseReaderRequested?.Invoke();
    }

    [RelayCommand]
    public async Task ReadArticleAloudAsync(NewsFeedItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsSpeaking)
        {
            StopArticleReadout();
            return;
        }

        StopOtherSpeechRequested?.Invoke();
        StopArticleReadout();

        _speakingItem = item;
        item.IsSpeaking = true;
        OnPropertyChanged(nameof(IsActiveArticleSpeaking));
        _speechCts = new CancellationTokenSource();
        var ct = _speechCts.Token;

        var mode = VoiceModeProvider?.Invoke() ?? VoiceReadoutMode.HindiDirect;
        string speech = string.IsNullOrWhiteSpace(item.Summary)
            ? item.Title
            : $"{item.Title}. {item.Summary}";
        StatusText = $"Reading article aloud ({AiPersonalisationDefaults.GetVoiceOption(mode).ShortLabel})...";

        try
        {
            var (speed, pitch, voiceModel) = VoiceSettingsProvider?.Invoke() ?? (1.0, 0, null);
            await _voiceService.SpeakAsync(speech, mode, speed, pitch, voiceModel, ct);
            if (!ct.IsCancellationRequested)
            {
                StatusText = "Finished reading article";
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
            if (ReferenceEquals(_speakingItem, item))
            {
                _speakingItem = null;
            }

            item.IsSpeaking = false;
            OnPropertyChanged(nameof(IsActiveArticleSpeaking));
        }
    }

    [RelayCommand]
    public async Task ReadActiveArticleAloudAsync()
    {
        if (SelectedArticle is null)
        {
            return;
        }

        if (SelectedArticle.IsSpeaking)
        {
            StopArticleReadout();
            return;
        }

        StopOtherSpeechRequested?.Invoke();
        StopArticleReadout();

        _speakingItem = SelectedArticle;
        SelectedArticle.IsSpeaking = true;
        OnPropertyChanged(nameof(IsActiveArticleSpeaking));
        _speechCts = new CancellationTokenSource();
        var ct = _speechCts.Token;

        var mode = VoiceModeProvider?.Invoke() ?? VoiceReadoutMode.HindiDirect;
        string speech = string.IsNullOrWhiteSpace(SelectedArticle.FullContent)
            ? (!string.IsNullOrWhiteSpace(SelectedArticle.Summary) ? $"{SelectedArticle.Title}. {SelectedArticle.Summary}" : SelectedArticle.Title)
            : $"{SelectedArticle.Title}. {SelectedArticle.FullContent}";

        StatusText = $"Reading in-app article aloud ({AiPersonalisationDefaults.GetVoiceOption(mode).ShortLabel})...";

        try
        {
            var (speed, pitch, voiceModel) = VoiceSettingsProvider?.Invoke() ?? (1.0, 0, null);
            await _voiceService.SpeakAsync(speech, mode, speed, pitch, voiceModel, ct);
            if (!ct.IsCancellationRequested)
            {
                StatusText = "Finished reading article";
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
            if (ReferenceEquals(_speakingItem, SelectedArticle))
            {
                _speakingItem = null;
            }

            SelectedArticle.IsSpeaking = false;
            OnPropertyChanged(nameof(IsActiveArticleSpeaking));
        }
    }

    [RelayCommand]
    public void StopArticleReadout()
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

        _voiceService.Stop();
        if (_speakingItem is not null)
        {
            _speakingItem.IsSpeaking = false;
            _speakingItem = null;
        }

        if (SelectedArticle is not null)
        {
            SelectedArticle.IsSpeaking = false;
        }

        OnPropertyChanged(nameof(IsActiveArticleSpeaking));
    }

    [RelayCommand]
    public void DiscussWithAi(NewsFeedItem? item)
    {
        if (item is not null)
        {
            DiscussWithAiRequested?.Invoke(item);
        }
    }

    [RelayCommand]
    public void DiscussActiveArticleWithAi()
    {
        if (SelectedArticle is not null)
        {
            DiscussWithAiRequested?.Invoke(SelectedArticle);
        }
    }

    [RelayCommand]
    public void QuickAiAnalysis(string? promptType)
    {
        if (SelectedArticle is null)
        {
            return;
        }

        string details = !string.IsNullOrWhiteSpace(SelectedArticle.FullContent)
            ? SelectedArticle.FullContent
            : SelectedArticle.Summary;

        string prompt = promptType switch
        {
            "Summarize Key Takeaways" =>
                $"Provide an executive summary and key takeaways of this news article for study notes:\n\n**{SelectedArticle.Title}**\n{details}\nSource: {SelectedArticle.Source} ({SelectedArticle.Url})",
            "Explain Exam Relevance" or "Explain Exam Relevance (UPSC/SSC)" =>
                $"Analyze this current affairs topic from a competitive examination perspective (UPSC / SSC / State PCS):\n1. Key constitutional, policy, or economic concepts involved\n2. Probable Prelims and Mains questions\n3. Key facts and dates to remember\n\n**{SelectedArticle.Title}**\n{details}\nSource: {SelectedArticle.Source}",
            "Generate 3 Practice Questions" =>
                $"Generate 3 high-quality competitive exam multiple-choice questions with answer keys and brief explanations based on this story:\n\n**{SelectedArticle.Title}**\n{details}",
            _ =>
                $"Explain and analyze this news topic with background context and competitive exam relevance:\n\n**{SelectedArticle.Title}**\n{details}\nSource: {SelectedArticle.Source}"
        };

        if (DiscussCustomPromptRequested != null)
        {
            DiscussCustomPromptRequested.Invoke(SelectedArticle, prompt);
        }
        else
        {
            DiscussWithAiRequested?.Invoke(SelectedArticle);
        }
    }

    private static string BuildFallbackArticleText(NewsFeedItem item)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(item.Summary))
        {
            sb.AppendLine(item.Summary);
            sb.AppendLine();
        }

        sb.AppendLine("────────────────────────────────────────────");
        sb.AppendLine($"Publication Source: {item.Source}");
        sb.AppendLine($"Category: {item.CategoryLabel} • Published: {item.PublishedDisplay}");
        sb.AppendLine();
        sb.AppendLine("💡 Study Insights: Use the 'Discuss with AI' or quick analysis buttons below to explore historical background, policy implications, and exam relevance for this story.");
        return sb.ToString();
    }

    public void Dispose()
    {
        StopArticleReadout();
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _articleContentCts?.Cancel();
        _articleContentCts?.Dispose();
    }
}
