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
    private readonly IVoiceReadoutService _voiceService;
    private readonly ILlamaCppAutoSetupService _llamaService;
    private bool _isInitializing = true;
    private bool _isPreviewSpeaking;
    private CancellationTokenSource? _autoSetupCts;

    public AiAssistantSettings UpdatedSettings { get; private set; }

    public AiSettingsDialog(
        AiAssistantSettings currentSettings,
        IAiChatService chatService,
        IVoiceReadoutService? voiceService = null,
        ILlamaCppAutoSetupService? llamaService = null)
    {
        InitializeComponent();
        _chatService = chatService;
        _voiceService = voiceService ?? new VoiceReadoutService();
        _llamaService = llamaService ?? new LlamaCppAutoSetupService();
        UpdatedSettings = currentSettings.Clone();

        LoadFromSettings(UpdatedSettings);
        _isInitializing = false;
        Closing += (_, _) =>
        {
            _voiceService.Stop();
            _autoSetupCts?.Cancel();
        };
    }

    private void LoadFromSettings(AiAssistantSettings s)
    {
        ProviderBox.SelectedItem = AiProviderDefaults.GetPreset(s.Provider);
        BaseUrlBox.Text = s.BaseUrl;
        ModelBox.Text = s.ModelName;
        ApiKeyBox.Password = s.ApiKey;
        TemperatureSlider.Value = Math.Clamp(s.Temperature, 0.0, 1.5);
        TempLabel.Text = TemperatureSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
        MaxTokensBox.Text = s.MaxTokens.ToString(CultureInfo.InvariantCulture);
        PersonaPresetBox.SelectedValue = s.PersonaPreset;
        AnswerLengthBox.SelectedValue = s.AnswerLength;
        VoiceModeBox.SelectedValue = s.VoiceMode;
        SystemPromptBox.Text = s.SystemPrompt;
        IncludeResponseFormatCheck.IsChecked = s.IncludeResponseFormatSpec;
        ResponseFormatBox.Text = s.ResponseFormatPrompt;
        ResponseFormatBox.IsEnabled = s.IncludeResponseFormatSpec;
        UseInternetCheck.IsChecked = s.UseInternetByDefault;

        // Auto setup & system specs
        var specs = _llamaService.DetectSystemSpecs();
        SystemSpecsLabel.Text = specs.SummaryText;

        var packages = _llamaService.GetAvailablePackages();
        AutoSetupModelBox.ItemsSource = packages;
        var recommended = _llamaService.GetRecommendedPackage(specs);
        AutoSetupModelBox.SelectedItem = recommended;
        AutoSetupModelNote.Text = recommended.Description;
        UpdateLlamaServerButton();

        // Voice Model, Speed & Pitch
        var voiceList = new List<string>(AiPersonalisationDefaults.StandardVoiceModels);
        try
        {
            var windowsVoices = _voiceService.GetInstalledWindowsVoices();
            foreach (var wv in windowsVoices)
            {
                if (!voiceList.Contains(wv, StringComparer.OrdinalIgnoreCase))
                {
                    voiceList.Add(wv);
                }
            }
        }
        catch
        {
        }
        VoiceModelBox.ItemsSource = voiceList;
        VoiceModelBox.Text = string.IsNullOrWhiteSpace(s.VoiceModel) ? "Google Neural (Auto)" : s.VoiceModel;

        VoiceSpeedSlider.Value = Math.Clamp(s.VoiceSpeed, 0.5, 2.0);
        VoiceSpeedLabel.Text = $"{VoiceSpeedSlider.Value:0.0}x";

        VoicePitchSlider.Value = Math.Clamp(s.VoicePitch, -10, 10);
        int pitchVal = (int)Math.Round(VoicePitchSlider.Value);
        VoicePitchLabel.Text = pitchVal == 0 ? "0 (Neutral)" : (pitchVal > 0 ? $"+{pitchVal}" : $"{pitchVal}");

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

    private void VoiceSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VoiceSpeedLabel is not null)
        {
            VoiceSpeedLabel.Text = $"{e.NewValue:0.0}x";
        }
    }

    private void VoicePitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VoicePitchLabel is not null)
        {
            int val = (int)Math.Round(e.NewValue);
            VoicePitchLabel.Text = val == 0 ? "0 (Neutral)" : (val > 0 ? $"+{val}" : $"{val}");
        }
    }

    private void VoiceModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        string selected = VoiceModelBox.SelectedItem?.ToString() ?? VoiceModelBox.Text ?? string.Empty;
        if (selected.Contains("Reels Male Narrator", StringComparison.OrdinalIgnoreCase) ||
            selected.Contains("Viral Baritone", StringComparison.OrdinalIgnoreCase))
        {
            if (VoicePitchSlider.Value == 0)
            {
                VoicePitchSlider.Value = -4;
            }
            if (Math.Abs(VoiceSpeedSlider.Value - 1.0) < 0.05)
            {
                VoiceSpeedSlider.Value = 1.05;
            }
        }
    }

    private async void PreviewVoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPreviewSpeaking)
        {
            _voiceService.Stop();
            _isPreviewSpeaking = false;
            PreviewVoiceLabel.Text = "Preview Voice";
            PreviewVoiceIcon.Symbol = SymbolRegular.Speaker224;
            return;
        }

        _isPreviewSpeaking = true;
        PreviewVoiceLabel.Text = "Stop";
        PreviewVoiceIcon.Symbol = SymbolRegular.Stop24;

        var mode = VoiceModeBox.SelectedValue is VoiceReadoutMode vm ? vm : VoiceReadoutMode.HindiDirect;
        string selectedVoice = VoiceModelBox.Text?.Trim() ?? string.Empty;
        bool isReels = selectedVoice.Contains("Reels", StringComparison.OrdinalIgnoreCase) ||
                       selectedVoice.Contains("Instagram", StringComparison.OrdinalIgnoreCase);

        string sampleText;
        if (isReels)
        {
            sampleText = mode == VoiceReadoutMode.English
                ? "Did you know? In the heart of ancient Rome, something astonishing was discovered. Welcome to StudySuit's viral Reels voice."
                : "क्या आप जानते हैं? यह है स्टडीसूट का वायरल रील्स नैरेटर वॉइस। तैयार हैं आज की पढ़ाई के लिए?";
        }
        else
        {
            sampleText = mode == VoiceReadoutMode.English
                ? "Hello! This is StudySuit AI voice preview at your chosen speed and pitch."
                : "नमस्ते! यह आपकी चुनी हुई गति और पिच पर स्टडीसूट एआई आवाज़ का प्रिव्यू है।";
        }

        try
        {
            await _voiceService.SpeakAsync(
                sampleText,
                mode,
                VoiceSpeedSlider.Value,
                (int)Math.Round(VoicePitchSlider.Value),
                VoiceModelBox.Text?.Trim());
        }
        catch
        {
        }
        finally
        {
            _isPreviewSpeaking = false;
            PreviewVoiceLabel.Text = "Preview Voice";
            PreviewVoiceIcon.Symbol = SymbolRegular.Speaker224;
        }
    }

    private void AutoSetupModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoSetupModelBox.SelectedItem is LlamaModelPackage pkg)
        {
            AutoSetupModelNote.Text = $"{pkg.Description} ({pkg.RamRecommendation})";
        }
    }

    private async void AutoSetupButton_Click(object sender, RoutedEventArgs e)
    {
        if (AutoSetupModelBox.SelectedItem is not LlamaModelPackage package)
        {
            return;
        }

        AutoSetupButton.IsEnabled = false;
        AutoSetupProgressPanel.Visibility = Visibility.Visible;
        AutoSetupProgressBar.Value = 0;
        AutoSetupStatusText.Text = "Starting auto-setup...";
        AutoSetupDetailText.Text = "";

        _autoSetupCts = new CancellationTokenSource();
        var ct = _autoSetupCts.Token;

        var progress = new Progress<(string Phase, double Percent, string Detail)>(p =>
        {
            AutoSetupStatusText.Text = p.Phase;
            AutoSetupProgressBar.Value = p.Percent;
            AutoSetupDetailText.Text = p.Detail;
        });

        try
        {
            // Step 1: Install llama.cpp server binaries
            string serverExe = await _llamaService.SetupLlamaServerAsync(progress, ct);

            // Step 2: Download the selected GGUF model
            string modelFile = await _llamaService.DownloadModelAsync(package, progress, ct);

            // Step 3: Configure settings to use llama.cpp
            var llamaPreset = AiProviderDefaults.Presets.FirstOrDefault(p => p.Kind == AiProviderKind.LlamaCppLocal)
                              ?? AiProviderDefaults.Presets[0];
            ProviderBox.SelectedItem = llamaPreset;
            BaseUrlBox.Text = "http://localhost:8080/v1";
            ModelBox.Text = package.FileName;

            AutoSetupStatusText.Text = "Auto-setup completed successfully!";
            AutoSetupDetailText.Text = "Ready to start local server";
            AutoSetupProgressBar.Value = 100;

            // Automatically attempt starting the server
            bool started = await _llamaService.StartServerAsync(package.FileName, port: 8080, ct);
            UpdateLlamaServerButton();

            TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
            TestStatusText.Text = started
                ? $"Local AI Server is running at http://localhost:8080/v1 with {package.DisplayName}!"
                : $"Auto-setup finished. Click 'Start Local Server' to run offline.";
        }
        catch (OperationCanceledException)
        {
            AutoSetupStatusText.Text = "Setup cancelled";
        }
        catch (Exception ex)
        {
            AutoSetupStatusText.Text = $"Setup error: {ex.Message}";
        }
        finally
        {
            AutoSetupButton.IsEnabled = true;
        }
    }

    private async void StartLlamaServerButton_Click(object sender, RoutedEventArgs e)
    {
        StartLlamaServerButton.IsEnabled = false;
        try
        {
            if (_llamaService.IsServerRunning())
            {
                _llamaService.StopServer();
                UpdateLlamaServerButton();
                TestStatusText.Foreground = Brushes.Gray;
                TestStatusText.Text = "Local llama.cpp server stopped.";
            }
            else
            {
                string modelName = AutoSetupModelBox.SelectedItem is LlamaModelPackage pkg
                    ? pkg.FileName
                    : (string.IsNullOrWhiteSpace(ModelBox.Text) ? "qwen2.5-1.5b-instruct-q4_k_m.gguf" : ModelBox.Text.Trim());

                TestStatusText.Foreground = Brushes.Gray;
                TestStatusText.Text = $"Starting local server with {modelName}...";

                bool success = await _llamaService.StartServerAsync(modelName);
                UpdateLlamaServerButton();

                TestStatusText.Foreground = new SolidColorBrush(
                    success ? Color.FromRgb(74, 222, 128) : Color.FromRgb(248, 113, 113));
                TestStatusText.Text = success
                    ? "Local llama.cpp server is running at http://localhost:8080/v1."
                    : "Could not start server. Ensure llama-server.exe and the model file are installed via Auto Setup.";
            }
        }
        finally
        {
            StartLlamaServerButton.IsEnabled = true;
        }
    }

    private void UpdateLlamaServerButton()
    {
        bool running = _llamaService.IsServerRunning();
        LlamaServerStatusLabel.Text = running ? "Stop Server" : "Start Local Server";
        LlamaServerIcon.Symbol = running ? SymbolRegular.Stop24 : SymbolRegular.Play24;
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
        string voiceModel = string.IsNullOrWhiteSpace(VoiceModelBox.Text)
            ? "Google Neural (Auto)"
            : VoiceModelBox.Text.Trim();

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
            VoiceMode = VoiceModeBox.SelectedValue is VoiceReadoutMode vm ? vm : VoiceReadoutMode.HindiDirect,
            VoiceModel = voiceModel,
            VoiceSpeed = Math.Round(VoiceSpeedSlider.Value, 1),
            VoicePitch = (int)Math.Round(VoicePitchSlider.Value),
            SystemPrompt = SystemPromptBox.Text.Trim(),
            IncludeResponseFormatSpec = IncludeResponseFormatCheck.IsChecked == true,
            ResponseFormatPrompt = ResponseFormatBox.Text.Trim(),
            UseInternetByDefault = UseInternetCheck.IsChecked == true,
            WebSearchProvider = searchProvider,
            MaxWebResults = maxWeb,
            WebSearchApiKey = SearchApiKeyBox.Password.Trim()
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

