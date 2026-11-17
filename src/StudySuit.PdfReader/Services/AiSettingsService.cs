using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IAiSettingsService
{
    AiAssistantSettings Load();
    void Save(AiAssistantSettings settings);
}

public sealed class AiSettingsService : IAiSettingsService
{
    private const string DpapiPrefix = "dpapi:";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AiSettingsService(string? customSettingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _settingsPath = customSettingsPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsPath = Path.Combine(appData, "StudySuit.PdfReader", "ai_settings.json");
        }
    }

    public AiAssistantSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AiAssistantSettings();
            }

            string json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AiAssistantSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return new AiAssistantSettings();
            }

            loaded.ApiKey = UnprotectSecret(loaded.ApiKey);
            loaded.WebSearchApiKey = UnprotectSecret(loaded.WebSearchApiKey);
            var preset = AiProviderDefaults.GetPreset(loaded.Provider);
            if (string.IsNullOrWhiteSpace(loaded.BaseUrl))
            {
                loaded.BaseUrl = preset.DefaultBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(loaded.ModelName))
            {
                loaded.ModelName = preset.DefaultModel;
            }

            if (string.IsNullOrWhiteSpace(loaded.SystemPrompt))
            {
                loaded.SystemPrompt = AiAssistantSettings.DefaultPersonalityPrompt;
            }

            if (loaded.IncludeResponseFormatSpec && string.IsNullOrWhiteSpace(loaded.ResponseFormatPrompt))
            {
                loaded.ResponseFormatPrompt = AiAssistantSettings.DefaultResponseFormatPrompt;
            }

            loaded.Temperature = Math.Clamp(loaded.Temperature, 0.0, 2.0);
            loaded.MaxTokens = Math.Clamp(loaded.MaxTokens, 64, 32768);
            loaded.MaxWebResults = Math.Clamp(loaded.MaxWebResults, 1, 10);

            if (string.IsNullOrWhiteSpace(loaded.OmniVoice) ||
                (loaded.Provider == AiProviderKind.LlamaCppLocal && string.Equals(loaded.OmniVoice, "alloy", StringComparison.OrdinalIgnoreCase)))
            {
                loaded.OmniVoice = "female";
            }

            return loaded;
        }
        catch
        {
            return new AiAssistantSettings();
        }
    }

    public void Save(AiAssistantSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var toPersist = settings.Clone();
            toPersist.ApiKey = ProtectSecret(settings.ApiKey);
            toPersist.WebSearchApiKey = ProtectSecret(settings.WebSearchApiKey);

            string json = JsonSerializer.Serialize(toPersist, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }

    private static string ProtectSecret(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return string.Empty;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(plain);
            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(encrypted);
        }
        catch
        {
            return plain;
        }
    }

    private static string UnprotectSecret(string stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            return stored;
        }

        try
        {
            byte[] encrypted = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }
}

