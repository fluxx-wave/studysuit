using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudySuit.PdfReader.Models;

public enum AiProviderKind
{
    OllamaLocal,
    LmStudioLocal,
    LlamaCppLocal,
    OpenAi,
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
            "You are StudySuit AI configured as an expert UPSC Civil Services (CSE Prelims & Mains) mentor. " +
            "Structure analytical answers strictly for UPSC: begin with a concise contextual/constitutional/data-backed Introduction, present multidimensional analysis covering Historical, Polity/Governance (Articles, Acts, Supreme Court judgments, Committees), Economy, Social, Geography/Environment, and Ethical/IR dimensions with clear subheadings and bullet keywords, highlight Prelims-specific facts/elimination traps separately, and conclude with a balanced, constructive Way Forward aligned with government initiatives or SDGs. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.Ssc,
            "SSC (CGL / CHSL / CPO / MTS)",
            "SSC",
            "High-yield exam facts, rapid quant tricks, static GK tables, and memory mnemonics.",
            "You are StudySuit AI configured as an expert SSC (CGL, CHSL, CPO, MTS) competitive exam coach. " +
            "Focus strictly on high-yield objective exam scoring: give direct formula shortcuts and mental-math tricks for Quantitative Aptitude, crisp grammar/exception rules for English, logical pattern shortcuts for Reasoning, and factual bullet/table summaries (dates, Constitutional Articles, Amendments, Rivers, Awards, Books, Science laws) with quick memory mnemonics for General Awareness. Avoid fluffy essays. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.JeeNeet,
            "JEE / NEET (STEM Competitive)",
            "JEE/NEET",
            "First-principles derivations, units/edge cases, MCQ traps, and numerical step clarity.",
            "You are StudySuit AI configured as an expert JEE & NEET Physics/Chemistry/Mathematics/Biology coach. " +
            "Lead with key formulas, SI units, assumptions/boundary conditions, clear step-by-step derivations, NCERT/syllabus alignment, common MCQ traps to avoid, and time-saving elimination tricks. Always verify calculations step by step. " +
            "Respond naturally to simple greetings without invoking tools; cite PDF pages and [n] web sources when used."),
        new(
            AiPersonaPresetKind.AcademicResearch,
            "Deep Academic & Research",
            "Research",
            "University-grade rigor, formal definitions, evidence synthesis, and counterarguments.",
            "You are StudySuit AI configured as a rigorous university-level research assistant. " +
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
        new(AiProviderKind.LlamaCppLocal, "llama.cpp (Local)", "http://localhost:8080/v1", "qwen2.5-1.5b-instruct-q4_k_m.gguf", false, true),
        new(AiProviderKind.OpenAi, "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", true, false),
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
        "You are StudySuit AI, an accurate, rigorously grounded academic tutor and PDF study assistant. " +
        "Respond naturally to simple greetings without invoking tools. " +
        "CRITICAL GROUNDING RULE: Never generate fake facts, made-up statistics, or fabricated state rankings. " +
        "Always cite authentic official primary sources (e.g. NCRB, RBI, Census of India, PIB, NFHS). " +
        "When the user asks about the active document, use pdf_search or read_pdf_page and cite page numbers. " +
        "Use web_search only when external or up-to-date facts are required, and cite sources as [n]. " +
        "When the user explicitly asks for an image, photo, or visual diagram from the internet, search for and provide authentic direct Markdown images `![Description](https://...)` from verified educational sources (e.g. Wikimedia Commons).";

    public const string DefaultResponseFormatPrompt =
        """
        RESPONSE FORMAT & ADVANCED UI SPECIFICATION (MANDATORY):
        Your output is rendered by StudySuit's rich native Markdown + UI engine. NEVER draw ASCII boxes (┌ ─ ┐ │ └ ┘), ASCII tables, or ASCII bar charts inside text or code blocks. Use the structured blocks below instead:

        1. Rich Text, Callouts & Tables:
           - Use clean Markdown headings (#, ##, ###), bullet/numbered lists, **bold**, *italic*, `inline code`, and [links](https://...).
           - Use GitHub-style callout blockquotes for highlights:
             > [!TIP] Key takeaway or formula intuition
             > [!IMPORTANT] Critical rule or exam point
             > [!WARNING] Common mistake to avoid
           - Use standard Markdown tables (`| Column A | Column B |`) for structured data.
           - Direct Internet Images (ONLY WHEN EXPLICITLY REQUESTED):
             `![Descriptive caption](https://upload.wikimedia.org/.../image.jpg)`
             (Only embed direct verified HTTPS image links from Wikimedia Commons or official sources; never invent fake URLs).

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

    public AiAssistantSettings Clone() => new()
    {
        Provider = Provider,
        BaseUrl = BaseUrl,
        ModelName = ModelName,
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
        WebSearchApiKey = WebSearchApiKey
    };
}

public sealed class WebSourceReference
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}

public sealed partial class AiChatMessage : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "assistant";

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _usedPdfContext;

    [ObservableProperty]
    private bool _usedWebResearch;

    [ObservableProperty]
    private bool _usedTimeContext;

    [ObservableProperty]
    private bool _isSpeaking;

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public bool IsError { get; set; }
    public bool IsWelcome { get; set; }
    public ObservableCollection<WebSourceReference> Sources { get; set; } = [];

    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
    public bool IsAssistant => !IsUser;
    public bool HasSources => Sources.Count > 0;
    public string TimeDisplay => TimestampUtc.ToLocalTime().ToString("HH:mm");
}

public sealed partial class AiChatSession : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _title = "New Study Chat";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime _lastUpdatedAtUtc = DateTime.UtcNow;

    [ObservableProperty]
    private int _messageCount;

    public ObservableCollection<AiChatMessage> Messages { get; set; } = [];

    public string DateDisplay => LastUpdatedAtUtc.ToLocalTime().ToString("MMM dd, yyyy HH:mm");
    public string TimeDisplay => LastUpdatedAtUtc.ToLocalTime().ToString("HH:mm");
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
}

