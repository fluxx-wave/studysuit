using System.IO;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public sealed class AppUserState
{
    public bool IsDarkTheme { get; set; } = true;
    public bool IsNightLightEnabled { get; set; }
    public double NightLightLevel { get; set; } = 45.0;
    public List<RecentFileItem> RecentFiles { get; set; } = [];
}

public interface IRecentFilesService
{
    AppUserState Load();
    void Save(AppUserState state);
    void RecordOpenedFile(AppUserState state, string filePath, int pageCount, int lastPage = 1, int? annotationCount = null);
    void UpdateLastPage(AppUserState state, string filePath, int lastPage);
    void UpdateAnnotationCount(AppUserState state, string filePath, int count);
    void TogglePin(AppUserState state, string filePath);
    void RemoveFile(AppUserState state, string filePath);
}

public sealed class RecentFilesService : IRecentFilesService
{
    private const int MaxRecentFiles = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;

    public RecentFilesService(string? customStateFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customStateFilePath))
        {
            _stateFilePath = customStateFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _stateFilePath = Path.Combine(appData, "StudySuit.PdfReader", "state.json");
        }
    }

    public AppUserState Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new AppUserState();
            }

            string json = File.ReadAllText(_stateFilePath);
            var loaded = JsonSerializer.Deserialize<AppUserState>(json, JsonOptions);
            if (loaded is null)
            {
                return new AppUserState();
            }

            loaded.RecentFiles = loaded.RecentFiles
                .Where(f => !string.IsNullOrWhiteSpace(f.FilePath))
                .Take(MaxRecentFiles)
                .ToList();
            return loaded;
        }
        catch
        {
            return new AppUserState();
        }
    }

    public void Save(AppUserState state)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // Non-fatal persistence error; keep UI uninterrupted.
        }
    }

    public void RecordOpenedFile(AppUserState state, string filePath, int pageCount, int lastPage = 1, int? annotationCount = null)
    {
        var existing = state.RecentFiles.FirstOrDefault(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        bool isPinned = existing?.IsPinned ?? false;
        int finalAnnotationCount = annotationCount ?? existing?.AnnotationCount ?? 0;

        if (existing is not null)
        {
            state.RecentFiles.Remove(existing);
        }

        int clampedPage = Math.Clamp(lastPage, 1, Math.Max(1, pageCount));
        state.RecentFiles.Insert(0, new RecentFileItem
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            LastOpenedUtc = DateTime.UtcNow,
            LastPage = clampedPage,
            PageCount = pageCount,
            IsPinned = isPinned,
            AnnotationCount = finalAnnotationCount
        });

        if (state.RecentFiles.Count > MaxRecentFiles)
        {
            // Prefer keeping pinned files when trimming old unpinned entries.
            var unpinned = state.RecentFiles.Where(f => !f.IsPinned).ToList();
            while (state.RecentFiles.Count > MaxRecentFiles && unpinned.Count > 0)
            {
                var toRemove = unpinned[^1];
                unpinned.RemoveAt(unpinned.Count - 1);
                state.RecentFiles.Remove(toRemove);
            }

            if (state.RecentFiles.Count > MaxRecentFiles)
            {
                state.RecentFiles.RemoveRange(MaxRecentFiles, state.RecentFiles.Count - MaxRecentFiles);
            }
        }

        Save(state);
    }

    public void UpdateLastPage(AppUserState state, string filePath, int lastPage)
    {
        var existing = state.RecentFiles.FirstOrDefault(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        existing.LastPage = Math.Clamp(lastPage, 1, Math.Max(1, existing.PageCount));
        Save(state);
    }

    public void UpdateAnnotationCount(AppUserState state, string filePath, int count)
    {
        var existing = state.RecentFiles.FirstOrDefault(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        existing.AnnotationCount = Math.Max(0, count);
        Save(state);
    }

    public void TogglePin(AppUserState state, string filePath)
    {
        var existing = state.RecentFiles.FirstOrDefault(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            return;
        }

        existing.IsPinned = !existing.IsPinned;
        Save(state);
    }

    public void RemoveFile(AppUserState state, string filePath)
    {
        state.RecentFiles.RemoveAll(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        Save(state);
    }
}

