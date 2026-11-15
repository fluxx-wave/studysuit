using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public sealed class AiToolHost
{
    public bool AllowWebSearch { get; init; }
    public bool HasOpenPdf { get; init; }
    public bool AllowTemporalTools { get; init; } = true;
    public Func<string, CancellationToken, Task<(string ResultText, IReadOnlyList<WebSourceReference> Sources)>>? ExecuteWebSearchAsync { get; init; }
    public Func<string, CancellationToken, Task<string>>? ExecutePdfSearchAsync { get; init; }
    public Func<int, CancellationToken, Task<string>>? ExecuteReadPdfPageAsync { get; init; }
    public Func<string?, CancellationToken, Task<string>>? ExecuteGetCurrentTimeAsync { get; init; }
    public Func<CancellationToken, Task<string>>? ExecuteGetTodayOverviewAsync { get; init; }
    public Func<string?, int?, int?, int?, CancellationToken, Task<string>>? ExecuteGetCalendarInfoAsync { get; init; }
    public Action<string>? OnToolStatus { get; init; }
    public Action<bool, bool, bool>? OnToolUsageRecorded { get; init; }
    public Action<IReadOnlyList<WebSourceReference>>? OnWebSourcesCollected { get; init; }
}

public interface IAiChatService
{
    Task<string> SendChatAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        string? pdfContext,
        IReadOnlyList<WebSourceReference> webSources,
        Action<string>? onStreamDelta = null,
        CancellationToken cancellationToken = default);

    Task<string> SendChatWithToolsAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        string? pdfContext,
        AiToolHost? toolHost,
        Action<string>? onStreamDelta = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> FetchAvailableModelsAsync(
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> TestConnectionAsync(
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class AiChatService : IAiChatService
{
    private const int MaxToolRounds = 4;
    private static readonly HashSet<string> CasualGreetings = new(StringComparer.OrdinalIgnoreCase)
    {
        "hi", "hey", "hello", "hola", "yo", "sup", "howdy", "greetings",
        "good morning", "good afternoon", "good evening", "good night",
        "how are you", "whats up", "what's up", "thanks", "thank you", "thx", "ty", "ok", "okay", "cool", "nice", "bye"
    };

    private readonly HttpClient _http;

    public AiChatService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
    }

    public static bool IsCasualMessage(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return true;
        }

        string normalized = prompt.Trim().TrimEnd('!', '.', '?', ',');
        return CasualGreetings.Contains(normalized);
    }

    public Task<string> SendChatAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        string? pdfContext,
        IReadOnlyList<WebSourceReference> webSources,
        Action<string>? onStreamDelta = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        string enrichedUserPrompt = BuildEnrichedUserPrompt(userPrompt, pdfContext, webSources);
        return DispatchSinglePassAsync(settings, history, enrichedUserPrompt, onStreamDelta, cancellationToken);
    }

    public async Task<string> SendChatWithToolsAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        string? pdfContext,
        AiToolHost? toolHost,
        Action<string>? onStreamDelta = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);

        bool casual = IsCasualMessage(userPrompt);
        bool enableTools = toolHost is not null && !casual && (toolHost.HasOpenPdf || toolHost.AllowWebSearch || toolHost.AllowTemporalTools);
        string effectivePdfContext = casual ? string.Empty : (pdfContext ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(effectivePdfContext))
        {
            toolHost?.OnToolUsageRecorded?.Invoke(true, false, false);
        }

        string initialUserPrompt = string.IsNullOrWhiteSpace(effectivePdfContext)
            ? userPrompt.Trim()
            : BuildEnrichedUserPrompt(userPrompt, effectivePdfContext, []);

        if (!enableTools || toolHost is null)
        {
            return await DispatchSinglePassAsync(settings, history, initialUserPrompt, onStreamDelta, cancellationToken);
        }

        return settings.Provider switch
        {
            AiProviderKind.OllamaLocal => await SendOllamaWithToolsAsync(settings, history, initialUserPrompt, toolHost, onStreamDelta, cancellationToken),
            AiProviderKind.Gemini => await SendGeminiWithToolsAsync(settings, history, initialUserPrompt, toolHost, onStreamDelta, cancellationToken),
            AiProviderKind.Anthropic => await SendAnthropicWithToolsAsync(settings, history, initialUserPrompt, toolHost, onStreamDelta, cancellationToken),
            _ => await SendOpenAiWithToolsAsync(settings, history, initialUserPrompt, toolHost, onStreamDelta, cancellationToken)
        };
    }

    private Task<string> DispatchSinglePassAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string enrichedUserPrompt,
        Action<string>? onStreamDelta,
        CancellationToken ct)
    {
        return settings.Provider switch
        {
            AiProviderKind.OllamaLocal => SendOllamaWithToolsAsync(settings, history, enrichedUserPrompt, toolHost: null, onStreamDelta, ct),
            AiProviderKind.Gemini => SendGeminiWithToolsAsync(settings, history, enrichedUserPrompt, toolHost: null, onStreamDelta, ct),
            AiProviderKind.Anthropic => SendAnthropicWithToolsAsync(settings, history, enrichedUserPrompt, toolHost: null, onStreamDelta, ct),
            _ => SendOpenAiWithToolsAsync(settings, history, enrichedUserPrompt, toolHost: null, onStreamDelta, ct)
        };
    }

    public async Task<IReadOnlyList<string>> FetchAvailableModelsAsync(
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        string baseUrl = NormalizeBaseUrl(settings.BaseUrl, settings.Provider);

        switch (settings.Provider)
        {
            case AiProviderKind.OllamaLocal:
            {
                using var resp = await _http.GetAsync($"{baseUrl}/api/tags", cancellationToken);
                string body = await resp.Content.ReadAsStringAsync(cancellationToken);
                EnsureSuccess(resp, body);

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                return arr.EnumerateArray()
                    .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            case AiProviderKind.Gemini:
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    req.Headers.TryAddWithoutValidation("x-goog-api-key", settings.ApiKey.Trim());
                }

                using var resp = await _http.SendAsync(req, cancellationToken);
                string body = await resp.Content.ReadAsStringAsync(cancellationToken);
                EnsureSuccess(resp, body);

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("models", out var arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                return arr.EnumerateArray()
                    .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? n["models/".Length..] : n)
                    .Where(n => n.Contains("gemini", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            case AiProviderKind.Anthropic:
                return
                [
                    "claude-3-5-sonnet-latest",
                    "claude-3-5-haiku-latest",
                    "claude-3-7-sonnet-latest"
                ];

            default:
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
                }

                using var resp = await _http.SendAsync(req, cancellationToken);
                string body = await resp.Content.ReadAsStringAsync(cancellationToken);
                EnsureSuccess(resp, body);

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                return arr.EnumerateArray()
                    .Select(m => m.TryGetProperty("id", out var id) ? id.GetString() : null)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(
        AiAssistantSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preset = AiProviderDefaults.GetPreset(settings.Provider);
            if (preset.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return (false, $"{preset.DisplayName} requires an API key.");
            }

            if (settings.Provider == AiProviderKind.Anthropic)
            {
                string reply = await SendAnthropicWithToolsAsync(settings, [], "Reply with OK.", toolHost: null, onStreamDelta: null, cancellationToken);
                return !string.IsNullOrWhiteSpace(reply)
                    ? (true, $"Connected to Anthropic ({settings.ModelName}).")
                    : (false, "Received an empty response from Anthropic.");
            }

            var models = await FetchAvailableModelsAsync(settings, cancellationToken);
            if (models.Count > 0)
            {
                return (true, $"Connected! Found {models.Count} available model(s).");
            }

            return (true, "Endpoint responded, but no models were listed.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ==================== OLLAMA (STREAMING + TOOLS) ====================
    private async Task<string> SendOllamaWithToolsAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        AiToolHost? toolHost,
        Action<string>? onStreamDelta,
        CancellationToken ct)
    {
        string baseUrl = NormalizeBaseUrl(settings.BaseUrl, settings.Provider);
        var messages = BuildBaseChatMessages(settings, history, userPrompt);
        var tools = BuildOpenAiToolSchemas(toolHost);
        bool useTools = tools.Count > 0;

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.ModelName.Trim(),
                ["messages"] = messages,
                ["stream"] = onStreamDelta is not null,
                ["options"] = new
                {
                    temperature = settings.Temperature,
                    num_predict = settings.MaxTokens
                }
            };

            if (useTools)
            {
                payload["tools"] = tools;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/chat")
            {
                Content = CreateJsonContent(payload)
            };

            using var resp = await _http.SendAsync(
                req,
                onStreamDelta is not null ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errBody = await resp.Content.ReadAsStringAsync(ct);
                if (useTools && errBody.Contains("tool", StringComparison.OrdinalIgnoreCase))
                {
                    // Model does not support native Ollama tool calling; retry without tools cleanly.
                    useTools = false;
                    continue;
                }

                EnsureSuccess(resp, errBody);
            }

            var contentBuilder = new StringBuilder();
            var toolCalls = new List<(string Name, JsonElement Args)>();

            if (onStreamDelta is not null)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                string? firstLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (firstLine is not null)
                {
                    bool parsedFirst = TryProcessOllamaChunk(firstLine, contentBuilder, toolCalls, onStreamDelta);
                    if (!parsedFirst)
                    {
                        // Pretty-printed single JSON response rather than NDJSON
                        string rest = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                        ProcessOllamaChunk($"{firstLine}\n{rest}", contentBuilder, toolCalls, onStreamDelta);
                    }
                    else
                    {
                        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                ProcessOllamaChunk(line, contentBuilder, toolCalls, onStreamDelta);
                            }
                        }
                    }
                }
            }
            else
            {
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ProcessOllamaChunk(body, contentBuilder, toolCalls, onStreamDelta: null);
            }

            if (toolCalls.Count == 0 || toolHost is null || round == MaxToolRounds)
            {
                return contentBuilder.ToString().Trim();
            }

            // Record assistant tool_calls message and execute tools
            var rawToolCalls = toolCalls.Select(tc => new
            {
                function = new
                {
                    name = tc.Name,
                    arguments = tc.Args
                }
            }).ToList();

            messages.Add(new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = contentBuilder.ToString(),
                ["tool_calls"] = rawToolCalls
            });

            foreach (var (name, args) in toolCalls)
            {
                string result = await ExecuteToolAsync(name, args, toolHost, ct);
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "tool",
                    ["content"] = result
                });
            }

            useTools = false;
        }

        return string.Empty;
    }

    private static bool TryProcessOllamaChunk(
        string jsonLine,
        StringBuilder contentBuilder,
        List<(string Name, JsonElement Args)> toolCalls,
        Action<string>? onStreamDelta)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return true;
        }

        try
        {
            ProcessOllamaChunk(jsonLine, contentBuilder, toolCalls, onStreamDelta);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ProcessOllamaChunk(
        string jsonLine,
        StringBuilder contentBuilder,
        List<(string Name, JsonElement Args)> toolCalls,
        Action<string>? onStreamDelta)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return;
        }

        using var doc = JsonDocument.Parse(jsonLine);
        if (!doc.RootElement.TryGetProperty("message", out var msgObj))
        {
            return;
        }

        if (msgObj.TryGetProperty("content", out var contentProp))
        {
            string chunk = contentProp.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(chunk))
            {
                contentBuilder.Append(chunk);
                onStreamDelta?.Invoke(chunk);
            }
        }

        if (msgObj.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcArr.EnumerateArray())
            {
                if (tc.TryGetProperty("function", out var fn))
                {
                    string name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    JsonElement args = fn.TryGetProperty("arguments", out var a) ? a.Clone() : default;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        toolCalls.Add((name, args));
                    }
                }
            }
        }
    }

    // ==================== OPENAI / LM STUDIO / LLAMA.CPP (TOOLS + STREAMING) ====================
    private sealed class OpenAiToolCallCollector
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    private async Task<string> SendOpenAiWithToolsAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        AiToolHost? toolHost,
        Action<string>? onStreamDelta,
        CancellationToken ct)
    {
        string baseUrl = NormalizeBaseUrl(settings.BaseUrl, settings.Provider);
        var messages = BuildBaseChatMessages(settings, history, userPrompt);
        var tools = BuildOpenAiToolSchemas(toolHost);
        bool useTools = tools.Count > 0;

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            bool streamThisRound = onStreamDelta is not null;
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.ModelName.Trim(),
                ["messages"] = messages,
                ["temperature"] = settings.Temperature,
                ["max_tokens"] = settings.MaxTokens,
                ["stream"] = streamThisRound
            };

            if (useTools)
            {
                payload["tools"] = tools;
                payload["tool_choice"] = "auto";
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
            {
                Content = CreateJsonContent(payload)
            };

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
            }

            using var resp = await _http.SendAsync(
                req,
                streamThisRound ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (useTools && (errBody.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
                                 errBody.Contains("jinja", StringComparison.OrdinalIgnoreCase) ||
                                 resp.StatusCode == System.Net.HttpStatusCode.InternalServerError))
                {
                    useTools = false;
                    continue;
                }

                EnsureSuccess(resp, errBody);
            }

            var sb = new StringBuilder();
            var toolMap = new Dictionary<int, OpenAiToolCallCollector>();
            var toolList = new List<OpenAiToolCallCollector>();

            if (streamThisRound)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        string data = line[5..].Trim();
                        if (data == "[DONE]")
                        {
                            break;
                        }

                        ProcessOpenAiStreamDelta(data, sb, toolMap, toolList, onStreamDelta);
                    }
                    else if (line.TrimStart().StartsWith('{'))
                    {
                        // Non-SSE fallback if endpoint ignores stream:true
                        ProcessOpenAiCompletionMessage(line, sb, toolList, onStreamDelta);
                        break;
                    }
                }
            }
            else
            {
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ProcessOpenAiCompletionMessage(body, sb, toolList, onDelta: null);
            }

            if (toolList.Count == 0 || toolHost is null || round == MaxToolRounds)
            {
                return sb.ToString().Trim();
            }

            var assistantCallList = new List<object>();
            var toolResults = new List<(string CallId, string Name, string Result)>();

            foreach (var tc in toolList)
            {
                string callId = string.IsNullOrWhiteSpace(tc.Id) ? Guid.NewGuid().ToString("N") : tc.Id;
                string name = tc.Name;
                string argsStr = tc.ArgumentsBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(argsStr))
                {
                    argsStr = "{}";
                }

                assistantCallList.Add(new
                {
                    id = callId,
                    type = "function",
                    function = new { name, arguments = argsStr }
                });

                using var argDoc = JsonDocument.Parse(argsStr);
                string res = await ExecuteToolAsync(name, argDoc.RootElement.Clone(), toolHost, ct);
                toolResults.Add((callId, name, res));
            }

            messages.Add(new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = sb.ToString(),
                ["tool_calls"] = assistantCallList
            });

            foreach (var (callId, name, res) in toolResults)
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = callId,
                    ["name"] = name,
                    ["content"] = res
                });
            }

            // After tool executions, disable tool re-invocations to stream synthesized final answer
            useTools = false;
        }

        return string.Empty;
    }

    private static void ProcessOpenAiStreamDelta(
        string json,
        StringBuilder sb,
        Dictionary<int, OpenAiToolCallCollector> toolMap,
        List<OpenAiToolCallCollector> toolList,
        Action<string>? onDelta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
            {
                return;
            }

            if (delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                string chunk = content.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(chunk))
                {
                    sb.Append(chunk);
                    onDelta?.Invoke(chunk);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var tcArr) &&
                tcArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcArr.EnumerateArray())
                {
                    int index = tc.TryGetProperty("index", out var ip) && ip.ValueKind == JsonValueKind.Number ? ip.GetInt32() : toolList.Count;
                    if (!toolMap.TryGetValue(index, out var collector))
                    {
                        collector = new OpenAiToolCallCollector();
                        toolMap[index] = collector;
                        toolList.Add(collector);
                    }

                    if (tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        collector.Id = idProp.GetString() ?? collector.Id;
                    }

                    if (tc.TryGetProperty("function", out var fn))
                    {
                        if (fn.TryGetProperty("name", out var np) && np.ValueKind == JsonValueKind.String)
                        {
                            collector.Name = np.GetString() ?? collector.Name;
                        }

                        if (fn.TryGetProperty("arguments", out var ap) && ap.ValueKind == JsonValueKind.String)
                        {
                            collector.ArgumentsBuilder.Append(ap.GetString());
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void ProcessOpenAiCompletionMessage(
        string json,
        StringBuilder sb,
        List<OpenAiToolCallCollector> toolList,
        Action<string>? onDelta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var msg))
            {
                return;
            }

            if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                string text = content.GetString()?.Trim() ?? string.Empty;
                sb.Append(text);
                if (!string.IsNullOrEmpty(text))
                {
                    onDelta?.Invoke(text);
                }
            }

            if (msg.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcArr.EnumerateArray())
                {
                    var collector = new OpenAiToolCallCollector
                    {
                        Id = tc.TryGetProperty("id", out var idp) ? idp.GetString() ?? string.Empty : string.Empty
                    };

                    if (tc.TryGetProperty("function", out var fn))
                    {
                        collector.Name = fn.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty;
                        if (fn.TryGetProperty("arguments", out var ap))
                        {
                            string argsStr = ap.ValueKind == JsonValueKind.String ? ap.GetString() ?? "{}" : ap.GetRawText();
                            collector.ArgumentsBuilder.Append(argsStr);
                        }
                    }

                    toolList.Add(collector);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    // ==================== GEMINI (TOOLS + STREAMING) ====================
    private async Task<string> SendGeminiWithToolsAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        AiToolHost? toolHost,
        Action<string>? onStreamDelta,
        CancellationToken ct)
    {
        string baseUrl = NormalizeBaseUrl(settings.BaseUrl, settings.Provider);
        string model = settings.ModelName.Trim();
        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            model = model["models/".Length..];
        }

        var contents = new List<object>();
        foreach (var (isUser, content) in NormalizeConversationTurns(history, userPrompt))
        {
            contents.Add(new
            {
                role = isUser ? "user" : "model",
                parts = new[] { new { text = content } }
            });
        }

        var funcDeclarations = BuildGeminiToolDeclarations(toolHost);

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            var payload = new Dictionary<string, object>
            {
                ["contents"] = contents,
                ["generationConfig"] = new
                {
                    temperature = settings.Temperature,
                    maxOutputTokens = settings.MaxTokens
                }
            };

            string effectiveSystem = BuildEffectiveSystemPrompt(settings);
            if (!string.IsNullOrWhiteSpace(effectiveSystem))
            {
                payload["systemInstruction"] = new
                {
                    parts = new[] { new { text = effectiveSystem } }
                };
            }

            if (funcDeclarations.Count > 0)
            {
                payload["tools"] = new[] { new { functionDeclarations = funcDeclarations } };
            }

            bool streamThisRound = onStreamDelta is not null;
            string action = streamThisRound ? ":streamGenerateContent?alt=sse" : ":generateContent";

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/models/{Uri.EscapeDataString(model)}{action}")
            {
                Content = CreateJsonContent(payload)
            };

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                req.Headers.TryAddWithoutValidation("x-goog-api-key", settings.ApiKey.Trim());
            }

            using var resp = await _http.SendAsync(
                req,
                streamThisRound ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                EnsureSuccess(resp, errBody);
            }

            var textSb = new StringBuilder();
            var functionCalls = new List<(string Name, JsonElement Args)>();

            if (streamThisRound)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } rawLine)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        string data = line[5..].Trim();
                        if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
                        {
                            continue;
                        }

                        ProcessGeminiCandidateJson(data, textSb, functionCalls, onStreamDelta);
                    }
                    else if (line.StartsWith('{'))
                    {
                        ProcessGeminiCandidateJson(line, textSb, functionCalls, onStreamDelta);
                    }
                }
            }
            else
            {
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ProcessGeminiCandidateJson(body, textSb, functionCalls, onStreamDelta: null);
            }

            if (functionCalls.Count == 0 || toolHost is null || round == MaxToolRounds)
            {
                return textSb.ToString().Trim();
            }

            var modelParts = new List<object>();
            if (textSb.Length > 0)
            {
                modelParts.Add(new { text = textSb.ToString() });
            }
            foreach (var (name, args) in functionCalls)
            {
                modelParts.Add(new { functionCall = new { name, args } });
            }
            contents.Add(new { role = "model", parts = modelParts });

            var responseParts = new List<object>();
            foreach (var (name, args) in functionCalls)
            {
                string result = await ExecuteToolAsync(name, args, toolHost, ct);
                responseParts.Add(new
                {
                    functionResponse = new
                    {
                        name,
                        response = new { result }
                    }
                });
            }

            contents.Add(new { role = "user", parts = responseParts });
            funcDeclarations.Clear(); // Only 1 tool round needed; subsequent round synthesizes final answer directly
        }

        return string.Empty;
    }

    private static void ProcessGeminiCandidateJson(
        string json,
        StringBuilder textSb,
        List<(string Name, JsonElement Args)> functionCalls,
        Action<string>? onStreamDelta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return;
            }

            var cand = candidates[0];
            if (!cand.TryGetProperty("content", out var contentObj) ||
                !contentObj.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    string chunk = t.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        textSb.Append(chunk);
                        onStreamDelta?.Invoke(chunk);
                    }
                }
                else if (part.TryGetProperty("functionCall", out var fc))
                {
                    string name = fc.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    JsonElement args = fc.TryGetProperty("args", out var a) ? a.Clone() : default;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        functionCalls.Add((name, args));
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    // ==================== ANTHROPIC (TOOLS + STREAMING) ====================
    private sealed class AnthropicToolUseCollector
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder InputBuilder { get; } = new();

        public JsonElement ParseInput()
        {
            string raw = InputBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = "{}";
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                return doc.RootElement.Clone();
            }
            catch
            {
                using var doc = JsonDocument.Parse("{}");
                return doc.RootElement.Clone();
            }
        }
    }

    private async Task<string> SendAnthropicWithToolsAsync(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        AiToolHost? toolHost,
        Action<string>? onStreamDelta,
        CancellationToken ct)
    {
        string baseUrl = NormalizeBaseUrl(settings.BaseUrl, settings.Provider);
        var messages = new List<object>();

        foreach (var (isUser, content) in NormalizeConversationTurns(history, userPrompt))
        {
            messages.Add(new { role = isUser ? "user" : "assistant", content });
        }
        var tools = BuildAnthropicToolSchemas(toolHost);

        for (int round = 0; round <= MaxToolRounds; round++)
        {
            bool streamThisRound = onStreamDelta is not null;
            var payload = new Dictionary<string, object>
            {
                ["model"] = settings.ModelName.Trim(),
                ["max_tokens"] = settings.MaxTokens,
                ["temperature"] = settings.Temperature,
                ["messages"] = messages,
                ["stream"] = streamThisRound
            };

            string effectiveSystem = BuildEffectiveSystemPrompt(settings);
            if (!string.IsNullOrWhiteSpace(effectiveSystem))
            {
                payload["system"] = effectiveSystem;
            }

            if (tools.Count > 0)
            {
                payload["tools"] = tools;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages")
            {
                Content = CreateJsonContent(payload)
            };

            req.Headers.TryAddWithoutValidation("x-api-key", settings.ApiKey.Trim());
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using var resp = await _http.SendAsync(
                req,
                streamThisRound ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                EnsureSuccess(resp, errBody);
            }

            var textSb = new StringBuilder();
            var toolMap = new Dictionary<int, AnthropicToolUseCollector>();
            var toolUses = new List<AnthropicToolUseCollector>();

            if (streamThisRound)
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } rawLine)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        string data = line[5..].Trim();
                        if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
                        {
                            continue;
                        }

                        ProcessAnthropicStreamChunk(data, textSb, toolMap, toolUses, onStreamDelta);
                    }
                    else if (line.StartsWith('{'))
                    {
                        ProcessAnthropicNonStreamMessage(line, textSb, toolUses);
                        break;
                    }
                }
            }
            else
            {
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ProcessAnthropicNonStreamMessage(body, textSb, toolUses);
            }

            if (toolUses.Count == 0 || toolHost is null || round == MaxToolRounds)
            {
                return textSb.ToString().Trim();
            }

            var assistantContentList = new List<object>();
            if (textSb.Length > 0)
            {
                assistantContentList.Add(new { type = "text", text = textSb.ToString() });
            }
            foreach (var tu in toolUses)
            {
                assistantContentList.Add(new
                {
                    type = "tool_use",
                    id = tu.Id,
                    name = tu.Name,
                    input = tu.ParseInput()
                });
            }
            messages.Add(new { role = "assistant", content = assistantContentList });

            var toolResults = new List<object>();
            foreach (var tu in toolUses)
            {
                string res = await ExecuteToolAsync(tu.Name, tu.ParseInput(), toolHost, ct);
                toolResults.Add(new
                {
                    type = "tool_result",
                    tool_use_id = tu.Id,
                    content = res
                });
            }

            messages.Add(new { role = "user", content = toolResults });
            tools.Clear(); // Only 1 tool round needed; subsequent round synthesizes final answer directly
        }

        return string.Empty;
    }

    private static void ProcessAnthropicStreamChunk(
        string json,
        StringBuilder textSb,
        Dictionary<int, AnthropicToolUseCollector> toolMap,
        List<AnthropicToolUseCollector> toolUses,
        Action<string>? onStreamDelta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return;
            }

            string type = typeProp.GetString() ?? string.Empty;

            if (type == "content_block_start" && doc.RootElement.TryGetProperty("content_block", out var cb))
            {
                int index = doc.RootElement.TryGetProperty("index", out var ip) ? ip.GetInt32() : 0;
                string cbType = cb.TryGetProperty("type", out var cbt) ? cbt.GetString() ?? string.Empty : string.Empty;
                if (cbType == "tool_use")
                {
                    var collector = new AnthropicToolUseCollector
                    {
                        Id = cb.TryGetProperty("id", out var idp) ? idp.GetString() ?? string.Empty : string.Empty,
                        Name = cb.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty
                    };
                    toolMap[index] = collector;
                    toolUses.Add(collector);
                }
            }
            else if (type == "content_block_delta" && doc.RootElement.TryGetProperty("delta", out var delta))
            {
                int index = doc.RootElement.TryGetProperty("index", out var ip) ? ip.GetInt32() : 0;
                string deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() ?? string.Empty : string.Empty;
                if (deltaType == "text_delta" && delta.TryGetProperty("text", out var textProp))
                {
                    string chunk = textProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        textSb.Append(chunk);
                        onStreamDelta?.Invoke(chunk);
                    }
                }
                else if (deltaType == "input_json_delta" && delta.TryGetProperty("partial_json", out var jsonProp))
                {
                    string partial = jsonProp.GetString() ?? string.Empty;
                    if (toolMap.TryGetValue(index, out var collector))
                    {
                        collector.InputBuilder.Append(partial);
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void ProcessAnthropicNonStreamMessage(
        string json,
        StringBuilder textSb,
        List<AnthropicToolUseCollector> toolUses)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Unexpected response format from Anthropic API.");
        }

        foreach (var item in arr.EnumerateArray())
        {
            string type = item.TryGetProperty("type", out var tp) ? tp.GetString() ?? string.Empty : string.Empty;
            if (type == "text" && item.TryGetProperty("text", out var t))
            {
                textSb.Append(t.GetString());
            }
            else if (type == "tool_use")
            {
                var collector = new AnthropicToolUseCollector
                {
                    Id = item.TryGetProperty("id", out var idp) ? idp.GetString() ?? string.Empty : string.Empty,
                    Name = item.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty
                };
                if (item.TryGetProperty("input", out var inp))
                {
                    collector.InputBuilder.Append(inp.GetRawText());
                }
                toolUses.Add(collector);
            }
        }
    }

    // ==================== TOOL DEFINITIONS & EXECUTION ====================
    internal static string BuildEffectiveSystemPrompt(AiAssistantSettings settings, DateTime? customNow = null)
    {
        var parts = new List<string>();
        DateTime now = customNow ?? DateTime.Now;
        TimeZoneInfo localTz = TimeZoneInfo.Local;

        parts.Add(
            $"[REAL-TIME TEMPORAL GROUNDING & CALENDAR ANCHOR]\n" +
            $"• Real-world Present Date: {now:dddd, MMMM dd, yyyy}\n" +
            $"• Real-world Present Year: {now.Year}\n" +
            $"• Real-world Local Time: {now:hh:mm:ss tt} ({now:HH:mm:ss} 24h) | Timezone: {localTz.DisplayName}\n" +
            $"• Real-world UTC Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n\n" +
            $"TEMPORAL COGNITION & SEARCH RULES (MANDATORY):\n" +
            $"1. You are actively operating in the real-world year {now.Year}. Any pre-training knowledge cutoff from 2024 or earlier is in the past.\n" +
            $"2. NEVER assume or state that the present year is 2024 or 2023. Never say 'As of my cutoff date in 2024' or treat 2024 as the current year.\n" +
            $"3. For questions about 'today', 'current events', 'recent news', 'this year', or study timelines, use {now.Year} (or {now:MMMM dd, yyyy}).\n" +
            $"4. When generating queries for `web_search` about latest/recent/current topics, ALWAYS ground the search in {now.Year} (or today's date). NEVER search for 2024 or 2023 unless the user explicitly requested historical events from those years.\n" +
            $"5. You have dedicated real-time tools: call `get_current_time` for live clocks/dates, `get_today_overview` for today's summary & annual progress, and `get_calendar_info` for exam countdowns, date differences, day-of-week lookups, and calendar math.\n\n" +
            $"[FACTUAL INTEGRITY, AUTHENTIC SOURCES & ANTI-HALLUCINATION RULES (STRICT & MANDATORY)]:\n" +
            $"1. ZERO TOLERANCE FOR FAKE FACTS: NEVER invent, fabricate, or hallucinate arbitrary statistics, state rankings, fake figures, or non-existent reports. If exact figures are not in your verified knowledge or document, state it honestly or execute `web_search`.\n" +
            $"2. ALWAYS CITE AUTHENTIC OFFICIAL PRIMARY SOURCES:\n" +
            $"   • For crime statistics: National Crime Records Bureau (NCRB) 'Crime in India' annual report.\n" +
            $"   • For economic data: Reserve Bank of India (RBI) Bulletins, Ministry of Finance Economic Survey, Union Budget, MoSPI/NSO.\n" +
            $"   • For health, literacy & population: Census of India, National Family Health Survey (NFHS), NITI Aayog SDG Index.\n" +
            $"   • For government schemes & policy: Press Information Bureau (PIB), Gazette of India, official ministry reports.\n" +
            $"3. STATISTICAL DISTINCTION — TOTAL VOLUME vs PER-CAPITA RATE:\n" +
            $"   • When explaining state comparisons (e.g. crime or incidents), ALWAYS explain the vital academic distinction between Total Incidents (where populous states like Uttar Pradesh with 240M people have high absolute counts) versus Crime Rate per 1 Lakh Population (where Union Territories or high-registration states like Delhi or Kerala lead due to near-100% online FIR/e-FIR systems, strict crime registration policies, and high literacy).\n\n" +
            $"[INTERNET IMAGES & VISUAL REFERENCE INSTRUCTION (MANDATORY)]:\n" +
            $"1. IF THE USER EXPLICITLY ASKS FOR AN IMAGE, PHOTO, PICTURE, OR DIAGRAM FROM THE INTERNET (e.g. 'show me an image/photo of...', 'picture dikhao', 'photo provide karo', 'image do', 'diagram ke sath batao', 'include photo/image'):\n" +
            $"   • Use `web_search` to find authentic high-resolution educational, encyclopedic, or official image links (e.g. from Wikimedia Commons, Wikipedia, NASA, ISRO, NCBI, or official government portals).\n" +
            $"   • Embed the authentic image into your response on its own line using standard Markdown image syntax: `![Descriptive Caption](https://example.com/image.jpg)`.\n" +
            $"   • ONLY embed real, direct HTTPS image URLs (e.g. ending in `.jpg`, `.jpeg`, `.png`, `.webp`, `.svg`, or authentic media URLs from Wikimedia Commons/Wikipedia). NEVER invent or hallucinate fake broken image URLs.\n" +
            $"   • Accompany the image with a brief clear caption explaining the visual concept.\n" +
            $"   • If the user does NOT explicitly ask for an image, do NOT include random internet images; provide clear conceptual explanations and structured charts.");

        string personaPrompt = settings.PersonaPreset == AiPersonaPresetKind.Custom || !string.IsNullOrWhiteSpace(settings.SystemPrompt)
            ? settings.SystemPrompt.Trim()
            : AiPersonalisationDefaults.GetPersonaPreset(settings.PersonaPreset).PromptText;

        if (!string.IsNullOrWhiteSpace(personaPrompt))
        {
            var presetInfo = AiPersonalisationDefaults.GetPersonaPreset(settings.PersonaPreset);
            parts.Add($"[PERSONALITY & EXAM MODE: {presetInfo.DisplayName}]\n{personaPrompt}");
        }

        var lengthOption = AiPersonalisationDefaults.GetLengthOption(settings.AnswerLength);
        parts.Add($"[{lengthOption.DisplayName}]\n{lengthOption.Instruction}");

        if (settings.IncludeResponseFormatSpec && !string.IsNullOrWhiteSpace(settings.ResponseFormatPrompt))
        {
            parts.Add(settings.ResponseFormatPrompt.Trim());
        }

        return string.Join("\n\n", parts);
    }

    internal static List<(bool IsUser, string Content)> NormalizeConversationTurns(
        IReadOnlyList<AiChatMessage> history,
        string userPrompt,
        int maxTurns = 60)
    {
        var turns = new List<(bool IsUser, string Content)>();

        foreach (var msg in history.Where(m => !m.IsError && !m.IsWelcome && !string.IsNullOrWhiteSpace(m.Content)).TakeLast(maxTurns))
        {
            string trimmed = msg.Content.Trim();
            if (turns.Count == 0 && !msg.IsUser)
            {
                // Strict Jinja chat templates (e.g. Ministral/Mistral/Llama in llama.cpp) require user first after system.
                continue;
            }

            if (turns.Count > 0 && turns[^1].IsUser == msg.IsUser)
            {
                turns[^1] = (msg.IsUser, $"{turns[^1].Content}\n\n{trimmed}");
            }
            else
            {
                turns.Add((msg.IsUser, trimmed));
            }
        }

        string cleanPrompt = userPrompt.Trim();
        if (turns.Count > 0 && turns[^1].IsUser)
        {
            turns[^1] = (true, $"{turns[^1].Content}\n\n{cleanPrompt}");
        }
        else
        {
            turns.Add((true, cleanPrompt));
        }

        return turns;
    }

    private static List<Dictionary<string, object>> BuildBaseChatMessages(
        AiAssistantSettings settings,
        IReadOnlyList<AiChatMessage> history,
        string userPrompt)
    {
        var messages = new List<Dictionary<string, object>>();
        string effectiveSystem = BuildEffectiveSystemPrompt(settings);
        if (!string.IsNullOrWhiteSpace(effectiveSystem))
        {
            messages.Add(new Dictionary<string, object>
            {
                ["role"] = "system",
                ["content"] = effectiveSystem
            });
        }

        foreach (var (isUser, content) in NormalizeConversationTurns(history, userPrompt))
        {
            messages.Add(new Dictionary<string, object>
            {
                ["role"] = isUser ? "user" : "assistant",
                ["content"] = content
            });
        }

        return messages;
    }

    // ==================== TEMPORAL TOOLS & FORMATTERS ====================
    public static string FormatCurrentTimeResponse(DateTime? customNow = null, string? tzOrFormat = null)
    {
        DateTime now = customNow ?? DateTime.Now;
        DateTime utc = now.ToUniversalTime();
        TimeZoneInfo localTz = TimeZoneInfo.Local;

        bool preferUtc = string.Equals(tzOrFormat, "utc", StringComparison.OrdinalIgnoreCase);
        DateTime displayTime = preferUtc ? utc : now;

        var info = new
        {
            current_date = displayTime.ToString("dddd, MMMM dd, yyyy"),
            current_time_12h = displayTime.ToString("hh:mm:ss tt"),
            current_time_24h = displayTime.ToString("HH:mm:ss"),
            year = displayTime.Year,
            month = displayTime.ToString("MMMM"),
            month_number = displayTime.Month,
            day = displayTime.Day,
            day_of_week = displayTime.DayOfWeek.ToString(),
            day_of_year = displayTime.DayOfYear,
            is_leap_year = DateTime.IsLeapYear(displayTime.Year),
            timezone = preferUtc ? "UTC (Coordinated Universal Time)" : $"{localTz.DisplayName} (UTC offset: {localTz.GetUtcOffset(now)})",
            utc_timestamp = utc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
            iso_8601 = now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            temporal_anchor = $"The real-world current year is {now.Year}. You are operating in {now.Year}."
        };

        return JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatTodayOverviewResponse(DateTime? customNow = null)
    {
        DateTime now = customNow ?? DateTime.Now;
        int year = now.Year;
        bool isLeap = DateTime.IsLeapYear(year);
        int totalDaysInYear = isLeap ? 366 : 365;
        int dayOfYear = now.DayOfYear;
        int daysRemaining = totalDaysInYear - dayOfYear;
        double progressPercent = Math.Round((double)dayOfYear / totalDaysInYear * 100.0, 1);
        int quarter = (now.Month - 1) / 3 + 1;

        var overview = new
        {
            today = now.ToString("dddd, MMMM dd, yyyy"),
            year = year,
            month = now.ToString("MMMM"),
            day = now.Day,
            day_of_week = now.DayOfWeek.ToString(),
            quarter = $"Q{quarter}",
            day_of_year = dayOfYear,
            days_remaining_in_year = daysRemaining,
            year_progress_percent = $"{progressPercent}%",
            is_leap_year = isLeap,
            current_time = now.ToString("hh:mm:ss tt (HH:mm:ss 24h)"),
            timezone = TimeZoneInfo.Local.DisplayName,
            temporal_anchor = $"The real-world current year is {year}. All answers and search queries must be anchored in {year}."
        };

        return JsonSerializer.Serialize(overview, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatCalendarInfoResponse(
        string? targetDateStr,
        int? daysOffset,
        int? month,
        int? year,
        DateTime? customNow = null)
    {
        DateTime today = (customNow ?? DateTime.Now).Date;
        int targetYear = year ?? today.Year;
        int targetMonth = (month >= 1 && month <= 12) ? month.Value : today.Month;

        var result = new Dictionary<string, object>
        {
            ["reference_today"] = today.ToString("yyyy-MM-dd (dddd, MMMM dd, yyyy)"),
            ["reference_year"] = today.Year,
            ["days_in_current_month"] = DateTime.DaysInMonth(today.Year, today.Month),
            ["is_current_year_leap"] = DateTime.IsLeapYear(today.Year)
        };

        if (daysOffset.HasValue && daysOffset.Value != 0)
        {
            DateTime offsetDate = today.AddDays(daysOffset.Value);
            result["offset_days"] = daysOffset.Value;
            result["offset_result_date"] = offsetDate.ToString("yyyy-MM-dd (dddd, MMMM dd, yyyy)");
            result["offset_day_of_week"] = offsetDate.DayOfWeek.ToString();
        }

        if (!string.IsNullOrWhiteSpace(targetDateStr))
        {
            DateTime? parsedTarget = ParseFlexibleDate(targetDateStr, today);
            if (parsedTarget.HasValue)
            {
                DateTime target = parsedTarget.Value.Date;
                int daysDiff = (target - today).Days;
                result["target_date"] = target.ToString("yyyy-MM-dd (dddd, MMMM dd, yyyy)");
                result["target_day_of_week"] = target.DayOfWeek.ToString();
                result["target_is_leap_year"] = DateTime.IsLeapYear(target.Year);

                if (daysDiff > 0)
                {
                    result["countdown_days_remaining"] = daysDiff;
                    result["status"] = $"{daysDiff} day(s) remaining until target date ({target:MMMM dd, yyyy})";
                }
                else if (daysDiff < 0)
                {
                    result["days_elapsed_since_target"] = Math.Abs(daysDiff);
                    result["status"] = $"{Math.Abs(daysDiff)} day(s) have passed since target date ({target:MMMM dd, yyyy})";
                }
                else
                {
                    result["status"] = "Target date is today!";
                }
            }
            else
            {
                result["target_parse_error"] = $"Could not parse date string: '{targetDateStr}'. Supported formats include YYYY-MM-DD, MM/DD/YYYY, or relative words like 'tomorrow', 'yesterday', 'next week'.";
            }
        }

        if (month.HasValue || year.HasValue)
        {
            int daysInSpecifiedMonth = DateTime.DaysInMonth(targetYear, targetMonth);
            DateTime firstDay = new DateTime(targetYear, targetMonth, 1);
            result["specified_month"] = new DateTime(targetYear, targetMonth, 1).ToString("MMMM yyyy");
            result["specified_month_total_days"] = daysInSpecifiedMonth;
            result["specified_month_starts_on"] = firstDay.DayOfWeek.ToString();
            result["specified_year_is_leap"] = DateTime.IsLeapYear(targetYear);
        }

        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static DateTime? ParseFlexibleDate(string dateStr, DateTime referenceToday)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        string clean = dateStr.Trim().ToLowerInvariant();
        if (clean is "today" or "now") return referenceToday;
        if (clean is "tomorrow") return referenceToday.AddDays(1);
        if (clean is "yesterday") return referenceToday.AddDays(-1);
        if (clean is "day after tomorrow") return referenceToday.AddDays(2);
        if (clean is "day before yesterday") return referenceToday.AddDays(-2);
        if (clean is "next week") return referenceToday.AddDays(7);
        if (clean is "next month") return referenceToday.AddMonths(1);

        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtDirect))
        {
            return dtDirect;
        }

        if (DateTime.TryParse(dateStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dtLocal))
        {
            return dtLocal;
        }

        return null;
    }

    private static List<object> BuildOpenAiToolSchemas(AiToolHost? host)
    {
        var list = new List<object>();
        if (host is null)
        {
            return list;
        }

        if (host.AllowTemporalTools)
        {
            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = "get_current_time",
                    description = $"Get the exact current real-world date, time, year ({DateTime.Now.Year}), day of the week, timezone, and ISO timestamp. Call this whenever answering questions about today, current time, what day it is, or verifying the real-world present date.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            timezone_or_format = new { type = "string", description = "Optional timezone name or format preference ('local' or 'utc')." }
                        }
                    }
                }
            });

            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = "get_today_overview",
                    description = $"Get a comprehensive overview of today in {DateTime.Now.Year}: current date, day of week, quarter, percentage of the year elapsed, and days remaining in {DateTime.Now.Year}.",
                    parameters = new
                    {
                        type = "object",
                        properties = new { }
                    }
                }
            });

            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = "get_calendar_info",
                    description = $"Calculate calendar dates, day offsets, days remaining until an exam/event, days elapsed, days in a month, or day of the week for any past or future date relative to today in {DateTime.Now.Year}.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            target_date = new { type = "string", description = "Target date string (e.g. '2026-12-31', '2026-03-15', 'tomorrow', 'yesterday', 'next week')." },
                            days_offset = new { type = "integer", description = "Number of days to add to or subtract from today (e.g. 7 for next week, -1 for yesterday)." },
                            month = new { type = "integer", description = "1-based month number (1 to 12) to inspect days in month." },
                            year = new { type = "integer", description = $"4-digit year (default is current year {DateTime.Now.Year})." }
                        }
                    }
                }
            });
        }

        if (host.HasOpenPdf)
        {
            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = "pdf_search",
                    description = "Search across all pages of the currently open PDF document for keywords or phrases.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Search keyword or phrase to find in the PDF." }
                        },
                        required = new[] { "query" }
                    }
                }
            });

            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = "read_pdf_page",
                    description = "Read the text content of a specific page number (1-based) from the currently open PDF.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            page_number = new { type = "integer", description = "1-based page number to read." }
                        },
                        required = new[] { "page_number" }
                    }
                }
            });
        }

        if (host.AllowWebSearch)
        {
            list.Add(new
            {
                type = "function",
                function = new
                {
                    name = "web_search",
                    description = $"Search the internet for up-to-date facts, news, definitions, or external information. Always ground recent/current queries in {DateTime.Now.Year}. Do NOT use for casual greetings.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = $"Concise web search query. Ground recent/current topics in {DateTime.Now.Year}." }
                        },
                        required = new[] { "query" }
                    }
                }
            });
        }

        return list;
    }

    private static List<object> BuildGeminiToolDeclarations(AiToolHost? host)
    {
        var list = new List<object>();
        if (host is null) return list;

        if (host.AllowTemporalTools)
        {
            list.Add(new
            {
                name = "get_current_time",
                description = $"Get the exact current real-world date, time, year ({DateTime.Now.Year}), day of week, and timezone.",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        timezone_or_format = new { type = "STRING", description = "Optional timezone or format ('local' or 'utc')" }
                    }
                }
            });

            list.Add(new
            {
                name = "get_today_overview",
                description = $"Get a summary of today, quarter, year progress, and days remaining in {DateTime.Now.Year}.",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new { }
                }
            });

            list.Add(new
            {
                name = "get_calendar_info",
                description = $"Calculate calendar dates, day offsets, days remaining until an exam/event, days elapsed, or day of the week relative to today in {DateTime.Now.Year}.",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        target_date = new { type = "STRING", description = "Target date string (e.g. '2026-12-31', 'tomorrow')" },
                        days_offset = new { type = "INTEGER", description = "Number of days offset from today" },
                        month = new { type = "INTEGER", description = "1-based month number (1 to 12)" },
                        year = new { type = "INTEGER", description = $"4-digit year (default is current year {DateTime.Now.Year})" }
                    }
                }
            });
        }

        if (host.HasOpenPdf)
        {
            list.Add(new
            {
                name = "pdf_search",
                description = "Search across all pages of the open PDF document for keywords.",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new { query = new { type = "STRING", description = "Search query" } },
                    required = new[] { "query" }
                }
            });
            list.Add(new
            {
                name = "read_pdf_page",
                description = "Read text of a 1-based page number in the open PDF.",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new { page_number = new { type = "INTEGER", description = "1-based page number" } },
                    required = new[] { "page_number" }
                }
            });
        }

        if (host.AllowWebSearch)
        {
            list.Add(new
            {
                name = "web_search",
                description = $"Search the internet for external or current information grounded in {DateTime.Now.Year}.",
                parameters = new
                {
                    type = "OBJECT",
                    properties = new { query = new { type = "STRING", description = $"Web search query grounded in {DateTime.Now.Year}" } },
                    required = new[] { "query" }
                }
            });
        }

        return list;
    }

    private static List<object> BuildAnthropicToolSchemas(AiToolHost? host)
    {
        var list = new List<object>();
        if (host is null) return list;

        if (host.AllowTemporalTools)
        {
            list.Add(new
            {
                name = "get_current_time",
                description = $"Get the exact current real-world date, time, year ({DateTime.Now.Year}), day of week, and timezone.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        timezone_or_format = new { type = "string", description = "Optional timezone or format ('local' or 'utc')" }
                    }
                }
            });

            list.Add(new
            {
                name = "get_today_overview",
                description = $"Get a summary of today, quarter, year progress, and days remaining in {DateTime.Now.Year}.",
                input_schema = new
                {
                    type = "object",
                    properties = new { }
                }
            });

            list.Add(new
            {
                name = "get_calendar_info",
                description = $"Calculate calendar dates, day offsets, days remaining until an exam/event, days elapsed, or day of the week relative to today in {DateTime.Now.Year}.",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        target_date = new { type = "string", description = "Target date string (e.g. '2026-12-31', 'tomorrow')" },
                        days_offset = new { type = "integer", description = "Number of days offset from today" },
                        month = new { type = "integer", description = "1-based month number (1 to 12)" },
                        year = new { type = "integer", description = $"4-digit year (default is current year {DateTime.Now.Year})" }
                    }
                }
            });
        }

        if (host.HasOpenPdf)
        {
            list.Add(new
            {
                name = "pdf_search",
                description = "Search across all pages of the open PDF document.",
                input_schema = new
                {
                    type = "object",
                    properties = new { query = new { type = "string", description = "Search query" } },
                    required = new[] { "query" }
                }
            });
            list.Add(new
            {
                name = "read_pdf_page",
                description = "Read the text of a 1-based page number in the open PDF.",
                input_schema = new
                {
                    type = "object",
                    properties = new { page_number = new { type = "integer", description = "1-based page number" } },
                    required = new[] { "page_number" }
                }
            });
        }

        if (host.AllowWebSearch)
        {
            list.Add(new
            {
                name = "web_search",
                description = $"Search the internet for external or current facts grounded in {DateTime.Now.Year}.",
                input_schema = new
                {
                    type = "object",
                    properties = new { query = new { type = "string", description = $"Web search query grounded in {DateTime.Now.Year}" } },
                    required = new[] { "query" }
                }
            });
        }

        return list;
    }

    private static async Task<string> ExecuteToolAsync(string name, JsonElement args, AiToolHost host, CancellationToken ct)
    {
        switch (name)
        {
            case "get_current_time" when host.AllowTemporalTools:
            {
                host.OnToolStatus?.Invoke("Checking current real-world clock & date...");
                host.OnToolUsageRecorded?.Invoke(false, false, true);
                if (host.ExecuteGetCurrentTimeAsync is not null)
                {
                    string tz = GetStringArg(args, "timezone_or_format");
                    return await host.ExecuteGetCurrentTimeAsync(tz, ct);
                }
                return FormatCurrentTimeResponse();
            }

            case "get_today_overview" when host.AllowTemporalTools:
            {
                host.OnToolStatus?.Invoke("Fetching today's temporal overview...");
                host.OnToolUsageRecorded?.Invoke(false, false, true);
                if (host.ExecuteGetTodayOverviewAsync is not null)
                {
                    return await host.ExecuteGetTodayOverviewAsync(ct);
                }
                return FormatTodayOverviewResponse();
            }

            case "get_calendar_info" when host.AllowTemporalTools:
            {
                host.OnToolStatus?.Invoke("Calculating calendar & date metrics...");
                host.OnToolUsageRecorded?.Invoke(false, false, true);
                string target = GetStringArg(args, "target_date");
                int? daysOffset = GetNullableIntArg(args, "days_offset");
                int? month = GetNullableIntArg(args, "month");
                int? year = GetNullableIntArg(args, "year");

                if (host.ExecuteGetCalendarInfoAsync is not null)
                {
                    return await host.ExecuteGetCalendarInfoAsync(target, daysOffset, month, year, ct);
                }
                return FormatCalendarInfoResponse(target, daysOffset, month, year);
            }

            case "pdf_search" when host.ExecutePdfSearchAsync is not null:
            {
                string q = GetStringArg(args, "query");
                host.OnToolStatus?.Invoke($"Searching PDF for \"{q}\"...");
                host.OnToolUsageRecorded?.Invoke(true, false, false);
                return await host.ExecutePdfSearchAsync(q, ct);
            }

            case "read_pdf_page" when host.ExecuteReadPdfPageAsync is not null:
            {
                int page = GetIntArg(args, "page_number", 1);
                host.OnToolStatus?.Invoke($"Reading PDF page {page}...");
                host.OnToolUsageRecorded?.Invoke(true, false, false);
                return await host.ExecuteReadPdfPageAsync(page, ct);
            }

            case "web_search" when host.AllowWebSearch && host.ExecuteWebSearchAsync is not null:
            {
                string q = GetStringArg(args, "query");
                host.OnToolStatus?.Invoke($"Searching web for \"{q}\"...");
                var (resultText, sources) = await host.ExecuteWebSearchAsync(q, ct);
                host.OnToolUsageRecorded?.Invoke(false, true, false);
                if (sources.Count > 0)
                {
                    host.OnWebSourcesCollected?.Invoke(sources);
                }

                return resultText;
            }

            default:
                return $"Tool '{name}' is not available.";
        }
    }

    private static int? GetNullableIntArg(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int num))
            {
                return num;
            }

            if (int.TryParse(val.ToString(), out int parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string GetStringArg(JsonElement args, string key)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var val))
        {
            return val.GetString() ?? val.ToString();
        }

        return string.Empty;
    }

    private static int GetIntArg(JsonElement args, string key, int fallback)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int num))
            {
                return num;
            }

            if (int.TryParse(val.ToString(), out int parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }

    internal static string BuildEnrichedUserPrompt(
        string userPrompt,
        string? pdfContext,
        IReadOnlyList<WebSourceReference> webSources)
    {
        if (string.IsNullOrWhiteSpace(pdfContext) && webSources.Count == 0)
        {
            return userPrompt;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(pdfContext))
        {
            sb.AppendLine("[Active PDF Document Context]");
            sb.AppendLine(pdfContext.Trim());
            sb.AppendLine();
        }

        if (webSources.Count > 0)
        {
            sb.AppendLine("[Internet Research Sources]");
            foreach (var src in webSources)
            {
                sb.AppendLine($"[{src.Index}] {src.Title} ({src.Url})");
                if (!string.IsNullOrWhiteSpace(src.Snippet))
                {
                    sb.AppendLine($"    {src.Snippet}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("[User Question]");
        sb.Append(userPrompt.Trim());
        return sb.ToString();
    }

    private static void ValidateSettings(AiAssistantSettings settings)
    {
        string effectiveUrl = NormalizeBaseUrl(settings.BaseUrl, settings.Provider);
        if (string.IsNullOrWhiteSpace(effectiveUrl))
        {
            throw new InvalidOperationException("AI Base URL is not configured. Open AI Settings to configure it.");
        }

        if (string.IsNullOrWhiteSpace(settings.ModelName))
        {
            throw new InvalidOperationException("AI Model Name is not configured. Open AI Settings to set a model.");
        }

        var preset = AiProviderDefaults.GetPreset(settings.Provider);
        if (preset.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException($"{preset.DisplayName} requires an API key. Add it in AI Settings.");
        }
    }

    internal static string NormalizeBaseUrl(string baseUrl, AiProviderKind provider)
    {
        string raw = string.IsNullOrWhiteSpace(baseUrl)
            ? AiProviderDefaults.GetPreset(provider).DefaultBaseUrl
            : baseUrl.Trim();
        raw = raw.TrimEnd('/');

        if (provider == AiProviderKind.OllamaLocal)
        {
            if (raw.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                raw.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[..raw.LastIndexOf('/')];
            }
        }
        else if (provider is AiProviderKind.LmStudioLocal or AiProviderKind.LlamaCppLocal)
        {
            if (!raw.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                raw += "/v1";
            }
        }

        return raw;
    }

    private static StringContent CreateJsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static void EnsureSuccess(HttpResponseMessage resp, string body)
    {
        if (resp.IsSuccessStatusCode)
        {
            return;
        }

        string detail = body.Length > 320 ? body[..320] + "…" : body;
        throw new HttpRequestException($"HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}): {detail}");
    }
}
