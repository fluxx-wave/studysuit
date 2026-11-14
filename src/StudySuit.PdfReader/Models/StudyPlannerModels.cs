namespace StudySuit.PdfReader.Models;

public enum TodoPriority
{
    Low,
    Medium,
    High
}

public enum EventCategory
{
    StudySession,
    Exam,
    Assignment,
    Lecture,
    Other
}

public sealed class StudyTodoItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    public DateTime? DueDate { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public string PriorityColorHex => Priority switch
    {
        TodoPriority.High => "#FFEF4444",
        TodoPriority.Medium => "#FFF59E0B",
        TodoPriority.Low => "#FF38BDF8",
        _ => "#FF38BDF8"
    };

    public string PriorityLabel => Priority.ToString();
    public string DueDateDisplay => DueDate.HasValue ? DueDate.Value.ToString("MMM dd") : string.Empty;
}

public sealed class StudyEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public DateTime EventDateTime { get; set; }
    public EventCategory Category { get; set; } = EventCategory.StudySession;
    public int ReminderMinutesBefore { get; set; } = 15;
    public bool ReminderEnabled { get; set; } = true;
    public bool ReminderDismissed { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }

    public string CategoryColorHex => Category switch
    {
        EventCategory.Exam => "#FFEF4444",
        EventCategory.Assignment => "#FFF97316",
        EventCategory.StudySession => "#FF4ADE80",
        EventCategory.Lecture => "#FF38BDF8",
        _ => "#FFC084FC"
    };

    public string CategoryLabel => Category switch
    {
        EventCategory.StudySession => "Study",
        _ => Category.ToString()
    };

    public string TimeDisplay => EventDateTime.ToString("HH:mm");
    public string DateAndTimeDisplay => EventDateTime.ToString("MMM dd • HH:mm");
    public string ReminderLabel => ReminderEnabled
        ? (ReminderMinutesBefore == 0 ? "At time" : $"{ReminderMinutesBefore}m before")
        : "Off";
}

public sealed class StudySessionLog
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime DateLocal { get; init; } = DateTime.Today;
    public long DurationSeconds { get; init; }
    public string Label { get; init; } = "Study Session";
    public DateTime LoggedUtc { get; init; } = DateTime.UtcNow;
}

public sealed class StudyPlannerState
{
    public List<StudyTodoItem> Todos { get; set; } = [];
    public List<StudyEvent> Events { get; set; } = [];
    public List<StudySessionLog> StudySessions { get; set; } = [];
}

