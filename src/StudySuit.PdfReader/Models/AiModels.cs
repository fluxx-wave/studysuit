using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudySuit.PdfReader.Models;

public enum AiProviderKind
{
    OllamaLocal,
    LmStudioLocal,
    LlamaCppLocal,
    OpenAi,
    LiquidAi,
    Gemini,
    Anthropic,
    CustomOpenAiCompatible
}

public enum WebSearchProviderKind
{
    BuiltInWeb,
    Tavily
}

public enum AiPersonaPresetKind
{
    General,
    Upsc,
    Ssc,
    JeeNeet,
    AcademicResearch,
    Custom
}

public enum AiAnswerLength
{
    Concise,
    Balanced,
    Detailed
}

public enum VoiceReadoutMode
{
    HindiDirect,
    HindiTranslate,
    English
}

public sealed record VoiceReadoutOption(
    VoiceReadoutMode Mode,
    string DisplayName,
    string ShortLabel);

public sealed record AiPersonaPresetInfo(
    AiPersonaPresetKind Kind,
    string DisplayName,
    string ShortLabel,
    string Description,
    string PromptText);

public sealed record AiAnswerLengthOption(
    AiAnswerLength Length,
    string DisplayName,
    string Instruction);

public static class AiPersonalisationDefaults
{
    public static IReadOnlyList<AiPersonaPresetInfo> PersonaPresets { get; } =
    [
        new(
            AiPersonaPresetKind.General,
            "General Study Tutor",
            "General",
            "Balanced academic tutor with clear examples, intuition, and visual summaries.",
            AiAssistantSettings.DefaultPersonalityPrompt),
        new(
            AiPersonaPresetKind.Upsc,
            "UPSC (CSE Prelims & Mains)",
            "UPSC",
            "GS Prelims traps + structured Mains format (Intro, multidimensional GS body, Way Forward).",
            "You are StudyMateAI configured as an expert UPSC Civil Services (CSE Prelims & Mains) mentor. " +
            "Structure analytical answers strictly for UPSC: begin with a concise contextual/constitutional/data-backed Introduction, present multidimensional analysis covering Historical, Polity/Governance (Articles, Acts, Supreme Court judgments, Committees), Economy, Social, Geography/Environment, and Ethical/IR dimensions with clear subheadings and bullet keywords, highlight Prelims-specific facts/elimination traps separately, and conclude with a balanced, constructive Way Forward aligned with government initiatives or SDGs. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.Ssc,
            "SSC (CGL / CHSL / CPO / MTS)",
            "SSC",
            "High-yield exam facts, rapid quant tricks, static GK tables, and memory mnemonics.",
            "You are StudyMateAI configured as an expert SSC (CGL, CHSL, CPO, MTS) competitive exam coach. " +
            "Focus strictly on high-yield objective exam scoring: give direct formula shortcuts and mental-math tricks for Quantitative Aptitude, crisp grammar/exception rules for English, logical pattern shortcuts for Reasoning, and factual bullet/table summaries (dates, Constitutional Articles, Amendments, Rivers, Awards, Books, Science laws) with quick memory mnemonics for General Awareness. Avoid fluffy essays. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.JeeNeet,
            "JEE / NEET (STEM Competitive)",
            "JEE/NEET",
            "First-principles derivations, units/edge cases, MCQ traps, and numerical step clarity.",
            "You are StudyMateAI configured as an expert JEE & NEET Physics/Chemistry/Mathematics/Biology coach. " +
            "Lead with key formulas, SI units, assumptions/boundary conditions, clear step-by-step derivations, NCERT/syllabus alignment, common MCQ traps to avoid, and time-saving elimination tricks. Always verify calculations step by step. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.AcademicResearch,
            "Deep Academic & Research",
            "Research",
            "University-grade rigor, formal definitions, evidence synthesis, and counterarguments.",
            "You are StudyMateAI configured as a rigorous university-level research assistant. " +
            "Provide precise formal definitions, theoretical grounding, methodology/evidence nuances, edge cases, and well-reasoned comparisons with zero oversimplification. Clearly distinguish verified document/web evidence from general background theory. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.Custom,
            "Custom Persona",
            "Custom",
            "Uses your custom Personality (System Prompt) configured in AI Settings.",
            AiAssistantSettings.DefaultPersonalityPrompt)
    ];

    public static IReadOnlyList<AiAnswerLengthOption> AnswerLengths { get; } =
    [
        new(
            AiAnswerLength.Concise,
            "Short (Quick Revision)",
            "ANSWER LENGTH RULE — SHORT / CRISP (~80–160 words): Be direct and high-density. Use crisp bullets, a compact table/metrics tile if useful, zero filler sentences, and skip lengthy background prose."),
        new(
            AiAnswerLength.Balanced,
            "Balanced (Standard)",
            "ANSWER LENGTH RULE — BALANCED (~200–400 words): Cover core concepts, key points, and 1–2 concrete examples or a visual chart/card block cleanly without unnecessary padding."),
        new(
            AiAnswerLength.Detailed,
            "Detailed (UPSC Mains / Deep)",
            "ANSWER LENGTH RULE — DETAILED / IN-DEPTH (~500–850+ words): Provide thorough, multi-dimensional coverage with clear sections, sub-points, real examples, relevant data/articles/formulas, edge cases, and visual cards, charts, or tables where helpful.")
    ];

    public static AiPersonaPresetInfo GetPersonaPreset(AiPersonaPresetKind kind) =>
        PersonaPresets.FirstOrDefault(p => p.Kind == kind) ?? PersonaPresets[0];

    public static AiAnswerLengthOption GetLengthOption(AiAnswerLength length) =>
        AnswerLengths.FirstOrDefault(l => l.Length == length) ?? AnswerLengths[1];

    public static IReadOnlyList<VoiceReadoutOption> VoiceModes { get; } =
    [
        new(VoiceReadoutMode.HindiDirect, "Hindi Voice (hi-IN Direct)", "Hindi Voice"),
        new(VoiceReadoutMode.HindiTranslate, "Hindi (Translate + Speak)", "Hindi (Translate)"),
        new(VoiceReadoutMode.English, "English Voice (en)", "English Voice")
    ];

    public static IReadOnlyList<string> StandardVoiceModels { get; } =
    [
        "Instagram Reels Male Narrator (Viral Baritone)",
        "Instagram Reels Voice (Brian / Storyteller)",
        "Instagram Reels Hindi (Madhur Male)",
        "Google Neural (Auto)",
        "Google Neural - Hindi (India)",
        "Google Neural - English (Natural)",
        "Windows Native Voice (System SAPI)"
    ];

    public static VoiceReadoutOption GetVoiceOption(VoiceReadoutMode mode) =>
        VoiceModes.FirstOrDefault(v => v.Mode == mode) ?? VoiceModes[0];
}

public sealed record AiProviderPreset(
    AiProviderKind Kind,
    string DisplayName,
    string DefaultBaseUrl,
    string DefaultModel,
    bool RequiresApiKey,
    bool IsLocal);

public static class AiProviderDefaults
{
    public static IReadOnlyList<AiProviderPreset> Presets { get; } =
    [
        new(AiProviderKind.OllamaLocal, "Ollama (Local)", "http://localhost:11434", "llama3.2", false, true),
        new(AiProviderKind.LmStudioLocal, "LM Studio (Local)", "http://localhost:1234/v1", "local-model", false, true),
        new(AiProviderKind.LlamaCppLocal, "llama.cpp (Local)", "http://localhost:9931/v1", "qwen2.5-1.5b-instruct-q4_k_m.gguf", false, true),
        new(AiProviderKind.OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", true, false),
        new(AiProviderKind.LiquidAi, "Liquid AI (LFM3 Omni / Audio)", "https://api.liquid.ai/openai/v1", "lfm3-omni-2b", true, false),
        new(AiProviderKind.Gemini, "Google Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash", true, false),
        new(AiProviderKind.Anthropic, "Anthropic Claude", "https://api.anthropic.com/v1", "claude-3-5-sonnet-latest", true, false),
        new(AiProviderKind.CustomOpenAiCompatible, "Custom (OpenAI-Compatible)", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", true, false)
    ];

    public static AiProviderPreset GetPreset(AiProviderKind kind) =>
        Presets.FirstOrDefault(p => p.Kind == kind) ?? Presets[0];
}

public sealed class AiAssistantSettings
{
    public const string DefaultPersonalityPrompt =
        "You are StudyMateAI, an accurate, rigorously grounded academic tutor and PDF study assistant. " +
        "Respond naturally to simple greetings without invoking tools. " +
        "CRITICAL GROUNDING RULE: Never generate fake facts, made-up statistics, or fabricated state rankings. " +
        "Always cite authentic official primary sources (e.g. NCRB, RBI, Census of India, PIB, NFHS). " +
        "When the user asks about the active document, use pdf_search or read_pdf_page and cite page numbers. " +
        "Use web_search only when external or up-to-date facts are required, and cite sources as [n]. " +
        "When explaining concepts, categories, or comparisons, use rich visual ```cards blocks. " +
        "When presenting data comparisons, distributions, or percentages, use interactive ```chart blocks. " +
        "When the user explicitly asks for an image, photo, or visual diagram from the internet, search for and provide authentic direct Markdown images `![Description](https://...)` from verified educational sources (e.g. Wikimedia Commons).";

    public const string DefaultResponseFormatPrompt =
        """
        RESPONSE FORMAT & ADVANCED UI SPECIFICATION (MANDATORY):
        Your output is rendered by StudyMateAI's rich native Markdown + UI engine. NEVER draw ASCII boxes (┌ ─ ┐ │ └ ┘), ASCII tables, or ASCII bar charts inside text or code blocks. Use the structured blocks below instead:

        1. Rich Text, Callouts & Tables:
           - Use clean Markdown headings (#, ##, ###), bullet/numbered lists, **bold**, *italic*, `inline code`, and [links](https://...).
           - Use GitHub-style callout blockquotes for highlights:
             > [!TIP] Key takeaway or formula intuition
             > [!IMPORTANT] Critical rule or exam point
             > [!WARNING] Common mistake to avoid
           - Use standard Markdown tables (`| Column A | Column B |`) for structured data.
           - Direct Internet Images:
             `![Descriptive caption](https://...)`
             (CRITICAL: Always start with the leading '!' exclamation mark. NEVER omit '!', otherwise it renders as a text hyperlink instead of displaying the image!).

        2. Math Equations (LaTeX):
           - Inline math: `$E = mc^2$` or `$\frac{dy}{dx}$`
           - Display equation block:
             $$
             \int_a^b f(x)\,dx = F(b) - F(a)
             $$

        3. Interactive Charts & Function Plots (fenced `chart` or `graph` JSON block):
           - Quantitative charts (`bar`, `line`, `pie`, `scatter`):
             ```chart
             {"type":"bar","title":"Share by Region","xLabel":"Region","yLabel":"Percent","data":[{"label":"Plains","value":43},{"label":"Plateau","value":30}]}
             ```
           - Mathematical function curves (`function`):
             ```chart
             {"type":"function","title":"Sine Curve","expression":"sin(x)","xMin":-6.28,"xMax":6.28,"yMin":-1.5,"yMax":1.5}
             ```

        4. Visual Summary Card Grids (`cards` JSON block) — ALWAYS use this instead of ASCII box layouts when grouping regions, categories, pros/cons, or topic cards:
           ```cards
           {"title":"Regional Distribution","columns":2,"cards":[{"title":"Northern Plains","badge":"Most Fertile","color":"#22C55E","items":["Alluvial Soils (~43%)","Rich in potash & lime"]},{"title":"Deccan Plateau","badge":"Volcanic","color":"#8B5CF6","items":["Black (Regur) Soils","Red & Yellow Soils"]}]}
           ```

        5. Process Flow & Timeline Diagrams (`diagram` JSON block):
           ```diagram
           {"title":"Process Flow","direction":"horizontal","steps":[{"label":"Step 1","detail":"Input stage","color":"#38BDF8"},{"label":"Step 2","detail":"Transform","color":"#8B5CF6"},{"label":"Step 3","detail":"Output","color":"#22C55E"}]}
           ```

        6. Key Metrics / Stat Tiles (`metrics` JSON block):
           ```metrics
           [{"label":"Area Share","value":"~43%","note":"Northern Plains"},{"label":"Crop Yield","value":"High","note":"Wheat, Rice, Sugarcane"}]
           ```

        7. Mermaid Diagrams (fenced `mermaid` block) — for flowcharts, systems, sequences, hierarchies:
           ```mermaid
           graph TD
             A[DNA Transcription] --> B[mRNA Processing]
             B --> C[Ribosome Translation]
             C --> D[Functional Protein]
           ```

        8. Interactive 3D Models (Sketchfab CDN) — whenever explaining cells, biology, human anatomy, organs, astronomy, or physical structures:
           Always call the tool `search_sketchfab_3d` with the concept name (e.g. `{"query": "solar system"}`).
           Embed ONLY the exact verified embed URL returned by the `search_sketchfab_3d` tool.
           CRITICAL: NEVER make up, invent, or guess a `{model_id}`. Fake or guessed URLs result in Error 404. Use ONLY real verified URLs from `search_sketchfab_3d`.
        """;

    public AiProviderKind Provider { get; set; } = AiProviderKind.OllamaLocal;
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ModelName { get; set; } = "llama3.2";
    public string ApiKey { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.4;
    public int MaxTokens { get; set; } = 2048;
    public string SystemPrompt { get; set; } = DefaultPersonalityPrompt;
    public AiPersonaPresetKind PersonaPreset { get; set; } = AiPersonaPresetKind.General;
    public AiAnswerLength AnswerLength { get; set; } = AiAnswerLength.Balanced;
    public VoiceReadoutMode VoiceMode { get; set; } = VoiceReadoutMode.HindiDirect;
    public string VoiceModel { get; set; } = "Google Neural (Auto)";
    public double VoiceSpeed { get; set; } = 1.0;
    public int VoicePitch { get; set; } = 0;
    public bool IncludeResponseFormatSpec { get; set; } = true;
    public string ResponseFormatPrompt { get; set; } = DefaultResponseFormatPrompt;

    public bool UseInternetByDefault { get; set; } = true;
    public int MaxWebResults { get; set; } = 4;
    public WebSearchProviderKind WebSearchProvider { get; set; } = WebSearchProviderKind.BuiltInWeb;
    public string WebSearchApiKey { get; set; } = string.Empty;
    public bool TalkMode { get; set; } = false;
    public string TalkModelName { get; set; } = string.Empty;
    public string OmniVoice { get; set; } = "female";
    public string OmniAudioFormat { get; set; } = "wav";
    public bool AutoReferenceOpenPdf { get; set; } = false;

    // Agentic Tools Control Toggles
    public bool EnableWebSearch { get; set; } = true;
    public bool EnableFetchWebpage { get; set; } = true;
    public bool EnableSketchfab3D { get; set; } = true;
    public bool EnableMermaidDiagrams { get; set; } = true;
    public bool EnableCharts { get; set; } = true;
    public bool EnablePdfTools { get; set; } = true;
    public bool EnableImageSearch { get; set; } = true;

    public string EffectiveTalkModelName =>
        !string.IsNullOrWhiteSpace(TalkModelName)
            ? TalkModelName.Trim()
            : (Provider == AiProviderKind.LlamaCppLocal ? "LiquidAI/LFM3-Omni-2B-GGUF:Q4_K_M" : (!string.IsNullOrWhiteSpace(ModelName) ? ModelName.Trim() : "LiquidAI/LFM3-Omni-2B-GGUF:Q4_K_M"));

    public AiAssistantSettings Clone() => new()
    {
        Provider = Provider,
        BaseUrl = BaseUrl,
        ModelName = ModelName,
        TalkModelName = TalkModelName,
        ApiKey = ApiKey,
        Temperature = Temperature,
        MaxTokens = MaxTokens,
        SystemPrompt = SystemPrompt,
        PersonaPreset = PersonaPreset,
        AnswerLength = AnswerLength,
        VoiceMode = VoiceMode,
        VoiceModel = VoiceModel,
        VoiceSpeed = VoiceSpeed,
        VoicePitch = VoicePitch,
        IncludeResponseFormatSpec = IncludeResponseFormatSpec,
        ResponseFormatPrompt = ResponseFormatPrompt,
        UseInternetByDefault = UseInternetByDefault,
        MaxWebResults = MaxWebResults,
        WebSearchProvider = WebSearchProvider,
        WebSearchApiKey = WebSearchApiKey,
        TalkMode = TalkMode,
        OmniVoice = OmniVoice,
        OmniAudioFormat = OmniAudioFormat,
        AutoReferenceOpenPdf = AutoReferenceOpenPdf,
        EnableWebSearch = EnableWebSearch,
        EnableFetchWebpage = EnableFetchWebpage,
        EnableSketchfab3D = EnableSketchfab3D,
        EnableMermaidDiagrams = EnableMermaidDiagrams,
        EnableCharts = EnableCharts,
        EnablePdfTools = EnablePdfTools,
        EnableImageSearch = EnableImageSearch
    };
}

public sealed class WebSourceReference
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}

public sealed class WebImageReference
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string SourcePageUrl { get; set; } = string.Empty;
    public string MarkdownSnippet => $"![{Title}]({ImageUrl})";
}

public sealed partial class AiChatMessage : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "assistant";

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _usedPdfContext;

    [ObservableProperty]
    private bool _usedWebResearch;

    [ObservableProperty]
    private bool _usedTimeContext;

    [ObservableProperty]
    private bool _isSpeaking;

    [ObservableProperty]
    private bool _hasAudio;

    [ObservableProperty]
    private bool _hasUserAudio;

    public byte[]? AudioBytes { get; set; }
    public string? AudioFormat { get; set; }

    public byte[]? UserInputAudioBytes { get; set; }
    public string? UserInputAudioFormat { get; set; } = "wav";

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public bool IsError { get; set; }
    public bool IsWelcome { get; set; }
    public ObservableCollection<WebSourceReference> Sources { get; set; } = [];
    public ObservableCollection<WebImageReference> Images { get; set; } = [];

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsAssistant => !IsUser;
    public bool HasSources => Sources.Count > 0;
    public bool HasImages => Images.Count > 0;
    public string TimeDisplay => TimestampUtc.ToLocalTime().ToString("HH:mm");
}

public sealed partial class AiChatSession : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _title = "New Study Chat";

    [ObservableProperty]
    private string _firstSearchedTopic = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime _lastUpdatedAtUtc = DateTime.UtcNow;

    [ObservableProperty]
    private int _messageCount;

    public ObservableCollection<AiChatMessage> Messages { get; set; } = [];

    public string PreviewText => Messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Content))?.Content ?? "Empty conversation";

    public string DateDisplay => LastUpdatedAtUtc.ToLocalTime().ToString("MMM dd, yyyy hh:mm tt");
    public string TimeDisplay => LastUpdatedAtUtc.ToLocalTime().ToString("hh:mm tt");

    public string FormattedTimeDisplay
    {
        get
        {
            var local = LastUpdatedAtUtc.ToLocalTime();
            var date = local.Date;
            var today = DateTime.Today;
            string timePart = local.ToString("h:mm tt");

            if (date == today)
            {
                return $"Today, {timePart}";
            }
            if (date == today.AddDays(-1))
            {
                return $"Yesterday, {timePart}";
            }
            if (date.Year == today.Year)
            {
                return $"{local:MMM dd}, {timePart}";
            }
            return $"{local:MMM dd, yyyy}, {timePart}";
        }
    }

    public string RelativeTimeDisplay
    {
        get
        {
            var diff = DateTime.UtcNow - LastUpdatedAtUtc;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return LastUpdatedAtUtc.ToLocalTime().ToString("MMM dd");
        }
    }

    public string DisplayTopic
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FirstSearchedTopic))
            {
                return FirstSearchedTopic;
            }
            if (!string.IsNullOrWhiteSpace(Title) && Title != "New Study Chat")
            {
                return Title;
            }
            return ExtractFirstSearchedTopic(Messages, Title);
        }
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTopic));
    }

    partial void OnFirstSearchedTopicChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTopic));
    }

    partial void OnLastUpdatedAtUtcChanged(DateTime value)
    {
        OnPropertyChanged(nameof(DateDisplay));
        OnPropertyChanged(nameof(TimeDisplay));
        OnPropertyChanged(nameof(FormattedTimeDisplay));
        OnPropertyChanged(nameof(RelativeTimeDisplay));
    }

    public static string ExtractFirstSearchedTopic(IEnumerable<AiChatMessage> messages, string? fallbackTitle = null)
    {
        var firstUser = messages.FirstOrDefault(m => m.IsUser && !m.IsWelcome && !string.IsNullOrWhiteSpace(m.Content));
        if (firstUser is null)
        {
            return !string.IsNullOrWhiteSpace(fallbackTitle) && fallbackTitle != "New Study Chat"
                ? fallbackTitle
                : "Study Chat";
        }

        string content = firstUser.Content.Trim();

        // 1. Circle to Search: [Circled on Page X of 'Title']: "Snippet"\n\nInstruction
        if (content.StartsWith("[Circled", StringComparison.OrdinalIgnoreCase))
        {
            int qStart = content.IndexOf('\"');
            int qEnd = qStart >= 0 ? content.IndexOf('\"', qStart + 1) : -1;
            if (qStart >= 0 && qEnd > qStart)
            {
                string snippet = content.Substring(qStart + 1, qEnd - qStart - 1).Trim();
                snippet = CleanTopicText(snippet);
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    int pIndex = content.IndexOf("Page ", StringComparison.OrdinalIgnoreCase);
                    string pageSuffix = string.Empty;
                    if (pIndex >= 0)
                    {
                        int pEnd = content.IndexOfAny([' ', ']', ':', '\r', '\n'], pIndex + 5);
                        if (pEnd > pIndex)
                        {
                            string pNum = content[(pIndex + 5)..pEnd].Trim();
                            if (int.TryParse(pNum, out int p))
                            {
                                pageSuffix = $" (p.{p})";
                            }
                        }
                    }

                    return $"🔍 {snippet}{pageSuffix}";
                }
            }

            int bracketEnd = content.IndexOf(']');
            if (bracketEnd > 0)
            {
                int pIndex = content.IndexOf("Page ", StringComparison.OrdinalIgnoreCase);
                string pagePart = pIndex >= 0 ? content.Substring(pIndex, Math.Min(10, bracketEnd - pIndex)).Trim() : "Diagram";
                return $"🔍 Circled {pagePart}";
            }

            return "🔍 Circled Search";
        }

        // 2. Regular user query
        return CleanTopicText(content);
    }

    private static string CleanTopicText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string cleaned = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }

        cleaned = cleaned.TrimStart('#', ' ', '*', '_');
        return cleaned;
    }
}

