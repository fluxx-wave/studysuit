using System.IO;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface IStudyPlannerService
{
    StudyPlannerState Load();
    void Save(StudyPlannerState state);
    IReadOnlyList<StudyEvent> GetDueReminders(StudyPlannerState state, DateTime nowLocal, DateTime nowUtc);
    TimeSpan GetTodayStudyTime(StudyPlannerState state, DateTime todayLocal);
    TimeSpan GetTotalStudyTime(StudyPlannerState state);
}

public sealed class StudyPlannerService : IStudyPlannerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public StudyPlannerService(string? customFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _filePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _filePath = Path.Combine(appData, "StudySuit.PdfReader", "study_planner.json");
        }
    }

    public StudyPlannerState Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new StudyPlannerState();
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<StudyPlannerState>(json, JsonOptions) ?? new StudyPlannerState();
        }
        catch
        {
            return new StudyPlannerState();
        }
    }

    public void Save(StudyPlannerState state)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Non-fatal persistence error; keep UI uninterrupted.
        }
    }

    public IReadOnlyList<StudyEvent> GetDueReminders(StudyPlannerState state, DateTime nowLocal, DateTime nowUtc)
    {
        var due = new List<StudyEvent>();
        foreach (var ev in state.Events)
        {
            if (!ev.ReminderEnabled || ev.ReminderDismissed)
            {
                continue;
            }

            if (ev.SnoozedUntilUtc.HasValue && nowUtc < ev.SnoozedUntilUtc.Value)
            {
                continue;
            }

            DateTime triggerLocal = ev.EventDateTime.AddMinutes(-Math.Max(0, ev.ReminderMinutesBefore));
            // Trigger if we're within the reminder window up to 24h after the event start.
            if (nowLocal >= triggerLocal && nowLocal <= ev.EventDateTime.AddHours(24))
            {
                due.Add(ev);
            }
        }

        return due.OrderBy(e => e.EventDateTime).ToList();
    }

    public TimeSpan GetTodayStudyTime(StudyPlannerState state, DateTime todayLocal)
    {
        long seconds = state.StudySessions
            .Where(s => s.DateLocal.Date == todayLocal.Date)
            .Sum(s => Math.Max(0, s.DurationSeconds));
        return TimeSpan.FromSeconds(seconds);
    }

    public TimeSpan GetTotalStudyTime(StudyPlannerState state)
    {
        long seconds = state.StudySessions.Sum(s => Math.Max(0, s.DurationSeconds));
        return TimeSpan.FromSeconds(seconds);
    }
}

