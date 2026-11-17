using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader.Views;

public partial class AiSettingsDialog : FluentWindow
{
    private readonly IAiChatService _chatService;
    private readonly ILlamaCppAutoSetupService _llamaService;
    private bool _isInitializing = true;

    public AiAssistantSettings UpdatedSettings { get; private set; }

    public AiSettingsDialog(
        AiAssistantSettings currentSettings,
        IAiChatService chatService,
        IVoiceReadoutService? voiceService = null,
        ILlamaCppAutoSetupService? llamaService = null)
    {
        InitializeComponent();
        _chatService = chatService;
        _llamaService = llamaService ?? new LlamaCppAutoSetupService();
        UpdatedSettings = currentSettings.Clone();

        LoadFromSettings(UpdatedSettings);
        _isInitializing = false;
    }

    private void LoadFromSettings(AiAssistantSettings s)
    {
        ProviderBox.SelectedItem = AiProviderDefaults.GetPreset(s.Provider);
        BaseUrlBox.Text = s.BaseUrl;
        ModelBox.Text = s.ModelName;
        AutoReferenceOpenPdfCheck.IsChecked = s.AutoReferenceOpenPdf;
        ApiKeyBox.Password = s.ApiKey;
        TemperatureSlider.Value = Math.Clamp(s.Temperature, 0.0, 1.5);
        TempLabel.Text = TemperatureSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        MaxTokensBox.Text = s.MaxTokens.ToString(CultureInfo.InvariantCulture);
        PersonaPresetBox.SelectedValue = s.PersonaPreset;
        AnswerLengthBox.SelectedValue = s.AnswerLength;
        SystemPromptBox.Text = s.SystemPrompt;
        IncludeResponseFormatCheck.IsChecked = s.IncludeResponseFormatSpec;
        ResponseFormatBox.Text = s.ResponseFormatPrompt;
        ResponseFormatBox.IsEnabled = s.IncludeResponseFormatSpec;
        UseInternetCheck.IsChecked = s.UseInternetByDefault;

        // Tool toggles
        ToolWebSearchCheck.IsChecked = s.EnableWebSearch;
        ToolFetchWebpageCheck.IsChecked = s.EnableFetchWebpage;
        ToolSketchfab3DCheck.IsChecked = s.EnableSketchfab3D;
        ToolMermaidCheck.IsChecked = s.EnableMermaidDiagrams;
        ToolChartsCheck.IsChecked = s.EnableCharts;
        ToolPdfToolsCheck.IsChecked = s.EnablePdfTools;
        ToolImageSearchCheck.IsChecked = s.EnableImageSearch;

        foreach (ComboBoxItem item in SearchProviderBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), s.WebSearchProvider.ToString(), StringComparison.Ordinal))
            {
                SearchProviderBox.SelectedItem = item;
                break;
            }
        }

        foreach (ComboBoxItem item in MaxResultsBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), s.MaxWebResults.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                MaxResultsBox.SelectedItem = item;
                break;
            }
        }

        SearchApiKeyBox.Password = s.WebSearchApiKey;
        UpdateProviderHints();
        UpdateSearchKeyVisibility();
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || ProviderBox.SelectedItem is not AiProviderPreset preset)
        {
            return;
        }

        BaseUrlBox.Text = preset.DefaultBaseUrl;
        ModelBox.Text = preset.DefaultModel;
        ModelBox.ItemsSource = null;
        UpdateProviderHints();
    }

    private void UpdateProviderHints()
    {
        if (ProviderBox.SelectedItem is AiProviderPreset preset)
        {
            ApiKeyHint.Text = preset.RequiresApiKey
                ? "Required for this cloud provider"
                : "Optional for local models";
        }
    }

    private void SearchProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing)
        {
            UpdateSearchKeyVisibility();
        }
    }

    private void UpdateSearchKeyVisibility()
    {
        if (SearchKeyPanel is null || SearchProviderBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        SearchKeyPanel.Visibility = string.Equals(item.Tag?.ToString(), nameof(WebSearchProviderKind.Tavily), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TempLabel is not null)
        {
            TempLabel.Text = e.NewValue.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    private void PresetOllamaButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = AiProviderDefaults.Presets.FirstOrDefault(p => p.Kind == AiProviderKind.OllamaLocal);
        if (preset is not null) ProviderBox.SelectedItem = preset;
        BaseUrlBox.Text = "http://localhost:11434";
        if (string.IsNullOrWhiteSpace(ModelBox.Text)) ModelBox.Text = "llama3.2";
        TestStatusText.Foreground = Brushes.Gray;
        TestStatusText.Text = "Switched to Ollama preset (default port 11434). Click 'Fetch Models' to load installed models.";
    }

    private void PresetLmStudioButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = AiProviderDefaults.Presets.FirstOrDefault(p => p.Kind == AiProviderKind.LmStudioLocal);
        if (preset is not null) ProviderBox.SelectedItem = preset;
        BaseUrlBox.Text = "http://localhost:1234/v1";
        if (string.IsNullOrWhiteSpace(ModelBox.Text)) ModelBox.Text = "local-model";
        TestStatusText.Foreground = Brushes.Gray;
        TestStatusText.Text = "Switched to LM Studio preset (default port 1234). Click 'Fetch Models' to load loaded models.";
    }

    private void PresetLlamaCppButton_Click(object sender, RoutedEventArgs e)
    {
        var preset = AiProviderDefaults.Presets.FirstOrDefault(p => p.Kind == AiProviderKind.LlamaCppLocal);
        if (preset is not null) ProviderBox.SelectedItem = preset;
        BaseUrlBox.Text = "http://localhost:9931/v1";
        if (string.IsNullOrWhiteSpace(ModelBox.Text)) ModelBox.Text = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
        TestStatusText.Foreground = Brushes.Gray;
        TestStatusText.Text = "Switched to llama.cpp preset (default port 9931).";
    }

    private AiAssistantSettings BuildSettingsFromForm()
    {
        var preset = ProviderBox.SelectedItem as AiProviderPreset ?? AiProviderDefaults.Presets[0];
        if (!int.TryParse(MaxTokensBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxTokens))
        {
            maxTokens = 2048;
        }

        var searchProvider = SearchProviderBox.SelectedItem is ComboBoxItem sp &&
                             Enum.TryParse<WebSearchProviderKind>(sp.Tag?.ToString(), out var parsedSp)
            ? parsedSp
            : WebSearchProviderKind.BuiltInWeb;

        int maxWeb = MaxResultsBox.SelectedItem is ComboBoxItem mr &&
                     int.TryParse(mr.Tag?.ToString(), out int parsedMax)
            ? parsedMax
            : 4;

        string baseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text)
            ? preset.DefaultBaseUrl
            : BaseUrlBox.Text.Trim();
        string modelName = string.IsNullOrWhiteSpace(ModelBox.Text)
            ? preset.DefaultModel
            : ModelBox.Text.Trim();

        return new AiAssistantSettings
        {
            Provider = preset.Kind,
            BaseUrl = baseUrl,
            ModelName = modelName,
            ApiKey = ApiKeyBox.Password.Trim(),
            Temperature = Math.Round(TemperatureSlider.Value, 2),
            MaxTokens = Math.Clamp(maxTokens, 64, 32768),
            PersonaPreset = PersonaPresetBox.SelectedValue is AiPersonaPresetKind pk ? pk : AiPersonaPresetKind.General,
            AnswerLength = AnswerLengthBox.SelectedValue is AiAnswerLength al ? al : AiAnswerLength.Balanced,
            SystemPrompt = SystemPromptBox.Text.Trim(),
            IncludeResponseFormatSpec = IncludeResponseFormatCheck.IsChecked == true,
            ResponseFormatPrompt = ResponseFormatBox.Text.Trim(),
            UseInternetByDefault = UseInternetCheck.IsChecked == true,
            WebSearchProvider = searchProvider,
            MaxWebResults = maxWeb,
            WebSearchApiKey = SearchApiKeyBox.Password.Trim(),
            AutoReferenceOpenPdf = AutoReferenceOpenPdfCheck.IsChecked == true,

            // Tools Section
            EnableWebSearch = ToolWebSearchCheck.IsChecked == true,
            EnableFetchWebpage = ToolFetchWebpageCheck.IsChecked == true,
            EnableSketchfab3D = ToolSketchfab3DCheck.IsChecked == true,
            EnableMermaidDiagrams = ToolMermaidCheck.IsChecked == true,
            EnableCharts = ToolChartsCheck.IsChecked == true,
            EnablePdfTools = ToolPdfToolsCheck.IsChecked == true,
            EnableImageSearch = ToolImageSearchCheck.IsChecked == true
        };
    }

    private void PersonaPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || PersonaPresetBox.SelectedItem is not AiPersonaPresetInfo preset)
        {
            return;
        }

        if (preset.Kind != AiPersonaPresetKind.Custom)
        {
            SystemPromptBox.Text = preset.PromptText;
        }
    }

    private void ResetPersonality_Click(object sender, RoutedEventArgs e)
    {
        PersonaPresetBox.SelectedValue = AiPersonaPresetKind.General;
        SystemPromptBox.Text = AiAssistantSettings.DefaultPersonalityPrompt;
    }

    private void ResetResponseFormat_Click(object sender, RoutedEventArgs e)
    {
        IncludeResponseFormatCheck.IsChecked = true;
        ResponseFormatBox.IsEnabled = true;
        ResponseFormatBox.Text = AiAssistantSettings.DefaultResponseFormatPrompt;
    }

    private void IncludeResponseFormatCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (ResponseFormatBox is not null)
        {
            ResponseFormatBox.IsEnabled = IncludeResponseFormatCheck.IsChecked == true;
        }
    }

    private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
    {
        FetchModelsButton.IsEnabled = false;
        TestStatusText.Foreground = Brushes.Gray;
        TestStatusText.Text = "Fetching available models...";

        try
        {
            var candidate = BuildSettingsFromForm();
            var models = await _chatService.FetchAvailableModelsAsync(candidate);
            if (models.Count > 0)
            {
                string currentText = ModelBox.Text;
                ModelBox.ItemsSource = models;
                ModelBox.Text = string.IsNullOrWhiteSpace(currentText) ? models[0] : currentText;

                ModelBox.IsDropDownOpen = true;
                TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                TestStatusText.Text = $"Found {models.Count} model(s).";
            }
            else
            {
                TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
                TestStatusText.Text = "Endpoint responded, but returned an empty model list.";
            }
        }
        catch (Exception ex)
        {
            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            TestStatusText.Text = $"Could not fetch models: {ex.Message}";
        }
        finally
        {
            FetchModelsButton.IsEnabled = true;
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestStatusText.Foreground = Brushes.Gray;
        TestStatusText.Text = "Testing connection...";

        try
        {
            var candidate = BuildSettingsFromForm();
            var (success, message) = await _chatService.TestConnectionAsync(candidate);
            TestStatusText.Foreground = new SolidColorBrush(
                success ? Color.FromRgb(74, 222, 128) : Color.FromRgb(248, 113, 113));
            TestStatusText.Text = message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdatedSettings = BuildSettingsFromForm();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

