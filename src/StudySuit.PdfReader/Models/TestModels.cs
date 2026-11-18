using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudySuit.PdfReader.Models;

public enum TestStatus
{
    NotStarted,
    InProgress,
    Completed,
    TimedOut
}

public enum TestSourceKind
{
    Topic,
    OpenPdf,
    LocalFile,
    DownloadedNcert
}

public sealed partial class TestQuestion : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Number { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = "mcq"; // "mcq", "assertion_reason", "statement_reasoning", "case_reasoning"
    public string? Assertion { get; set; }
    public string? Reason { get; set; }
    public string? Statement1 { get; set; }
    public string? Statement2 { get; set; }
    public string? ContextOrCase { get; set; }
    public List<string> Options { get; set; } = [];
    public int CorrectOptionIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Subtopic { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasContextOrCase => !string.IsNullOrWhiteSpace(ContextOrCase);

    [JsonIgnore]
    public bool HasAssertionReason => !string.IsNullOrWhiteSpace(Assertion) && !string.IsNullOrWhiteSpace(Reason);

    [JsonIgnore]
    public bool HasStatements => !string.IsNullOrWhiteSpace(Statement1) && !string.IsNullOrWhiteSpace(Statement2);

    [JsonIgnore]
    public string QuestionTypeDisplay => QuestionType?.ToLowerInvariant() switch
    {
        "assertion_reason" or "assertion-reason" or "ar" => "Assertion & Reasoning",
        "statement_reasoning" or "statement" or "statements" => "Statement Analysis",
        "case_reasoning" or "case" or "case_based" => "Case-Based Reasoning",
        _ => "Conceptual MCQ"
    };

    [JsonIgnore]
    public string QuestionTypeBadgeColor => QuestionType?.ToLowerInvariant() switch
    {
        "assertion_reason" or "assertion-reason" or "ar" => "#8B5CF6", // Purple
        "statement_reasoning" or "statement" or "statements" => "#EC4899", // Pink
        "case_reasoning" or "case" or "case_based" => "#F59E0B", // Amber
        _ => "#38BDF8" // Cyan / Blue
    };

    [ObservableProperty]
    private int? _userSelectedOptionIndex;

    [ObservableProperty]
    private bool _isFlagged;

    [JsonIgnore]
    public bool IsAnswered => UserSelectedOptionIndex.HasValue &&
                              UserSelectedOptionIndex.Value >= 0 &&
                              UserSelectedOptionIndex.Value < Options.Count;

    [JsonIgnore]
    public bool IsCorrect => IsAnswered && UserSelectedOptionIndex == CorrectOptionIndex;

    [JsonIgnore]
    public string OptionA => Options.Count > 0 ? Options[0] : string.Empty;

    [JsonIgnore]
    public string OptionB => Options.Count > 1 ? Options[1] : string.Empty;

    [JsonIgnore]
    public string OptionC => Options.Count > 2 ? Options[2] : string.Empty;

    [JsonIgnore]
    public string OptionD => Options.Count > 3 ? Options[3] : string.Empty;

    [JsonIgnore]
    public bool IsOptionASelected => UserSelectedOptionIndex == 0;

    [JsonIgnore]
    public bool IsOptionBSelected => UserSelectedOptionIndex == 1;

    [JsonIgnore]
    public bool IsOptionCSelected => UserSelectedOptionIndex == 2;

    [JsonIgnore]
    public bool IsOptionDSelected => UserSelectedOptionIndex == 3;

    [JsonIgnore]
    public string SelectedOptionLabel => UserSelectedOptionIndex switch
    {
        0 => "A",
        1 => "B",
        2 => "C",
        3 => "D",
        _ => "None"
    };

    [JsonIgnore]
    public string CorrectOptionLabel => CorrectOptionIndex switch
    {
        0 => "A",
        1 => "B",
        2 => "C",
        3 => "D",
        _ => "A"
    };

    partial void OnUserSelectedOptionIndexChanged(int? value)
    {
        OnPropertyChanged(nameof(IsAnswered));
        OnPropertyChanged(nameof(IsCorrect));
        OnPropertyChanged(nameof(IsOptionASelected));
        OnPropertyChanged(nameof(IsOptionBSelected));
        OnPropertyChanged(nameof(IsOptionCSelected));
        OnPropertyChanged(nameof(IsOptionDSelected));
        OnPropertyChanged(nameof(SelectedOptionLabel));
    }

    public void SelectOption(int index)
    {
        if (UserSelectedOptionIndex == index)
        {
            // Toggle off if already selected
            UserSelectedOptionIndex = null;
        }
        else
        {
            UserSelectedOptionIndex = index;
        }
    }

    public void ClearSelection() => UserSelectedOptionIndex = null;
    public void ToggleFlag() => IsFlagged = !IsFlagged;
}

public sealed partial class TestEvaluationReport : ObservableObject
{
    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _overallAssessment = string.Empty;

    public List<string> StrongAreas { get; set; } = [];
    public List<string> WeakAreas { get; set; } = [];
    public List<string> ActionableSuggestions { get; set; } = [];
    public List<string> RecommendedTopicsToReview { get; set; } = [];
}

public sealed partial class AiTestSession : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "Intermediate";
    public string TargetExam { get; set; } = "General Study";
    public TestSourceKind SourceKind { get; set; } = TestSourceKind.Topic;
    public string? SourceDocumentName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public double DurationHours { get; set; } = 0.5;
    public TimeSpan TotalDuration { get; set; } = TimeSpan.FromMinutes(30);
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [ObservableProperty]
    private TimeSpan _timeRemaining;

    [ObservableProperty]
    private TestStatus _status = TestStatus.NotStarted;

    public ObservableCollection<TestQuestion> Questions { get; set; } = [];

    [ObservableProperty]
    private TestEvaluationReport? _evaluationReport;

    [JsonIgnore]
    public int TotalQuestions => Questions.Count;

    [JsonIgnore]
    public int AnsweredCount => Questions.Count(q => q.IsAnswered);

    [JsonIgnore]
    public int UnansweredCount => Questions.Count(q => !q.IsAnswered);

    [JsonIgnore]
    public int CorrectCount => Questions.Count(q => q.IsCorrect);

    [JsonIgnore]
    public int IncorrectCount => Questions.Count(q => q.IsAnswered && !q.IsCorrect);

    [JsonIgnore]
    public int FlaggedCount => Questions.Count(q => q.IsFlagged);

    [JsonIgnore]
    public double ScorePercentage => TotalQuestions > 0 ? Math.Round((double)CorrectCount / TotalQuestions * 100, 1) : 0;

    [JsonIgnore]
    public string ScoreDisplay => $"{CorrectCount}/{TotalQuestions}";

    [JsonIgnore]
    public string Grade => ScorePercentage switch
    {
        >= 90 => "A+ (Outstanding)",
        >= 80 => "A (Excellent)",
        >= 70 => "B+ (Very Good)",
        >= 60 => "B (Good)",
        >= 50 => "C (Average)",
        _ => "Needs Revision"
    };

    [JsonIgnore]
    public string FormattedDuration => TotalDuration.TotalHours >= 1
        ? $"{TotalDuration.TotalHours:0.#} hr"
        : $"{TotalDuration.TotalMinutes:0} min";

    [JsonIgnore]
    public string FormattedTimeSpent
    {
        get
        {
            if (CompletedAt.HasValue && StartedAt.HasValue)
            {
                var diff = CompletedAt.Value - StartedAt.Value;
                return FormatTimeSpan(diff);
            }
            var spent = TotalDuration - TimeRemaining;
            if (spent < TimeSpan.Zero) spent = TimeSpan.Zero;
            return FormatTimeSpan(spent);
        }
    }

    [JsonIgnore]
    public string FormattedTimeRemaining => TimeRemaining.TotalHours >= 1
        ? $"{(int)TimeRemaining.TotalHours:D2}:{TimeRemaining.Minutes:D2}:{TimeRemaining.Seconds:D2}"
        : $"{TimeRemaining.Minutes:D2}:{TimeRemaining.Seconds:D2}";

    [JsonIgnore]
    public bool IsCompleted => Status is TestStatus.Completed or TestStatus.TimedOut;

    [JsonIgnore]
    public bool CanTakeTest => Status is TestStatus.NotStarted or TestStatus.InProgress;

    public void NotifyScoresChanged()
    {
        OnPropertyChanged(nameof(AnsweredCount));
        OnPropertyChanged(nameof(UnansweredCount));
        OnPropertyChanged(nameof(CorrectCount));
        OnPropertyChanged(nameof(IncorrectCount));
        OnPropertyChanged(nameof(FlaggedCount));
        OnPropertyChanged(nameof(ScorePercentage));
        OnPropertyChanged(nameof(ScoreDisplay));
        OnPropertyChanged(nameof(Grade));
        OnPropertyChanged(nameof(FormattedTimeSpent));
        OnPropertyChanged(nameof(FormattedTimeRemaining));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanTakeTest));
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalMinutes < 1)
        {
            return $"{ts.Seconds}s";
        }
        if (ts.TotalHours < 1)
        {
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
        return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }
}
