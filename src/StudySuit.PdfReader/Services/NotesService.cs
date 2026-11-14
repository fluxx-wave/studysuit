using System.IO;
using System.Text.Json;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

public interface INotesService
{
    IReadOnlyList<StudyNoteEntry> LoadIndex();
    void SaveIndex(IReadOnlyList<StudyNoteEntry> notes);
    byte[]? LoadNotePackage(string noteId);
    void SaveNotePackage(string noteId, byte[] packageBytes);
    void DeleteNoteFiles(string noteId);
}

public sealed class NotesService : INotesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _notesDirectory;
    private readonly string _indexPath;

    public NotesService(string? customDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            _notesDirectory = customDirectory;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _notesDirectory = Path.Combine(appData, "StudySuit.PdfReader", "notes");
        }

        _indexPath = Path.Combine(_notesDirectory, "notes_index.json");
    }

    public IReadOnlyList<StudyNoteEntry> LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return [];
            }

            string json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<List<StudyNoteEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveIndex(IReadOnlyList<StudyNoteEntry> notes)
    {
        try
        {
            Directory.CreateDirectory(_notesDirectory);
            string json = JsonSerializer.Serialize(notes, JsonOptions);
            File.WriteAllText(_indexPath, json);
        }
        catch
        {
        }
    }

    public byte[]? LoadNotePackage(string noteId)
    {
        try
        {
            string path = GetNotePackagePath(noteId);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public void SaveNotePackage(string noteId, byte[] packageBytes)
    {
        try
        {
            Directory.CreateDirectory(_notesDirectory);
            File.WriteAllBytes(GetNotePackagePath(noteId), packageBytes);
        }
        catch
        {
        }
    }

    public void DeleteNoteFiles(string noteId)
    {
        try
        {
            string path = GetNotePackagePath(noteId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private string GetNotePackagePath(string noteId) => Path.Combine(_notesDirectory, $"{noteId}.xamlpkg");
}
