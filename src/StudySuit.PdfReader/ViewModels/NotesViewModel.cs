using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed record NoteFontOption(string DisplayName, string FontFamilyName, bool IsHandwriting)
{
    public override string ToString() => DisplayName;
}

public sealed partial class NotesViewModel : ObservableObject
{
    private readonly INotesService _notesService;
    private readonly List<StudyNoteEntry> _allNotes = [];

    public static IReadOnlyList<NoteFontOption> FontOptions { get; } = BuildFontOptions();
    public static IReadOnlyList<double> FontSizeOptions { get; } = [11, 12, 14, 16, 18, 22, 28, 36];
    public static IReadOnlyList<string> NoteColorSwatches { get; } =
    [
        "#FF111827", "#FFFFFFFF", "#FF2563EB", "#FF16A34A", "#FFDC2626", "#FF9333EA", "#FFD97706", "#FF0D9488"
    ];

    public ObservableCollection<StudyNoteEntry> FilteredNotes { get; } = [];

    [ObservableProperty]
    private StudyNoteEntry? _selectedNote;

    [ObservableProperty]
    private string _notesSearchText = string.Empty;

    [ObservableProperty]
    private NoteFontOption _selectedFont = FontOptions[0];

    [ObservableProperty]
    private double _selectedFontSize = 16.0;

    [ObservableProperty]
    private string _selectedInkColorHex = "#FF2563EB";

    [ObservableProperty]
    private bool _isSketchPadOpen;

    [ObservableProperty]
    private bool _isSketchHighlighter;

    [ObservableProperty]
    private bool _isSketchEraser;

    [ObservableProperty]
    private double _sketchStrokeWidth = 3.0;

    [ObservableProperty]
    private string _noteStatusText = "Ready";

    public bool HasSelectedNote => SelectedNote is not null;

    public event Action<StudyNoteEntry?>? SelectedNoteSwitched;

    public NotesViewModel(INotesService? notesService = null)
    {
        _notesService = notesService ?? new NotesService();
        _allNotes.AddRange(_notesService.LoadIndex().OrderByDescending(n => n.UpdatedUtc));

        if (_allNotes.Count == 0)
        {
            _allNotes.Add(new StudyNoteEntry
            {
                Title = "Welcome Study Note",
                PreviewText = "Write with handwriting fonts, embed math equations, plot graphs, sketch diagrams, and insert pictures."
            });
            _notesService.SaveIndex(_allNotes);
        }

        RefreshFilteredNotes();
        SelectedNote = FilteredNotes.FirstOrDefault();
    }

    partial void OnSelectedNoteChanged(StudyNoteEntry? value)
    {
        OnPropertyChanged(nameof(HasSelectedNote));
        SelectedNoteSwitched?.Invoke(value);
    }

    partial void OnNotesSearchTextChanged(string value) => RefreshFilteredNotes();

    [RelayCommand]
    public void CreateNewNote()
    {
        var note = new StudyNoteEntry
        {
            Title = $"Study Note {_allNotes.Count + 1}",
            PreviewText = string.Empty
        };

        _allNotes.Insert(0, note);
        _notesService.SaveIndex(_allNotes);
        RefreshFilteredNotes();
        SelectedNote = note;
        NoteStatusText = $"Created \"{note.Title}\"";
    }

    [RelayCommand]
    public void DeleteNote(StudyNoteEntry? note)
    {
        note ??= SelectedNote;
        if (note is null)
        {
            return;
        }

        _allNotes.Remove(note);
        _notesService.DeleteNoteFiles(note.Id);

        if (_allNotes.Count == 0)
        {
            CreateNewNote();
            return;
        }

        _notesService.SaveIndex(_allNotes);
        RefreshFilteredNotes();
        if (ReferenceEquals(SelectedNote, note))
        {
            SelectedNote = FilteredNotes.FirstOrDefault();
        }

        NoteStatusText = $"Deleted \"{note.Title}\"";
    }

    [RelayCommand]
    public void ToggleSketchPad() => IsSketchPadOpen = !IsSketchPadOpen;

    [RelayCommand]
    public void SetSketchColor(string colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            SelectedInkColorHex = colorHex;
            IsSketchEraser = false;
        }
    }

    public byte[]? LoadNotePackage(string noteId) => _notesService.LoadNotePackage(noteId);

    public void SaveNoteContent(StudyNoteEntry note, byte[] packageBytes, string plainTextPreview)
    {
        note.UpdatedUtc = DateTime.UtcNow;
        note.PreviewText = plainTextPreview.Length > 90 ? $"{plainTextPreview[..90]}…" : plainTextPreview;
        _notesService.SaveNotePackage(note.Id, packageBytes);
        _notesService.SaveIndex(_allNotes);
        NoteStatusText = $"Saved at {DateTime.Now:HH:mm:ss}";
    }

    public void SaveTitleChange()
    {
        _notesService.SaveIndex(_allNotes);
    }

    private void RefreshFilteredNotes()
    {
        FilteredNotes.Clear();
        string q = NotesSearchText?.Trim() ?? string.Empty;
        foreach (var note in _allNotes)
        {
            if (string.IsNullOrEmpty(q) ||
                note.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                note.PreviewText.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredNotes.Add(note);
            }
        }
    }

    private static IReadOnlyList<NoteFontOption> BuildFontOptions()
    {
        var installed = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var options = new List<NoteFontOption>
        {
            new("Segoe UI (Clean)", "Segoe UI", false)
        };

        string[] handwritingFonts =
        [
            "Segoe Script",
            "Segoe Print",
            "Ink Free",
            "Lucida Handwriting",
            "Gabriola",
            "Comic Sans MS",
            "Bradley Hand ITC",
            "Kristen ITC"
        ];

        foreach (string hf in handwritingFonts)
        {
            if (installed.Contains(hf))
            {
                options.Add(new NoteFontOption($"{hf} (Handwriting)", hf, true));
            }
        }

        string[] standardFonts = ["Calibri", "Georgia", "Cambria Math", "Consolas", "Arial"];
        foreach (string sf in standardFonts)
        {
            if (installed.Contains(sf))
            {
                options.Add(new NoteFontOption(sf, sf, false));
            }
        }

        return options;
    }
}

