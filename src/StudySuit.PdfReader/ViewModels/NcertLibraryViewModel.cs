using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed partial class NcertLibraryViewModel : ObservableObject
{
    private readonly INcertLibraryService _libraryService;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedClass = 0; // 0 = All

    [ObservableProperty]
    private string _selectedClassOption = "All Classes";

    [ObservableProperty]
    private string _selectedSubject = "All";

    [ObservableProperty]
    private bool _onlyDownloaded;

    [ObservableProperty]
    private bool _hasNoMatchingBooks;

    [ObservableProperty]
    private string _summaryText = "Loading NCERT catalog...";

    [ObservableProperty]
    private string? _globalStatusMessage;

    public ObservableCollection<NcertBookItem> AllBooks { get; } = [];
    public ObservableCollection<NcertBookItem> FilteredBooks { get; } = [];

    public IReadOnlyList<string> ClassOptions { get; } =
    [
        "All Classes",
        "Class 12",
        "Class 11",
        "Class 10",
        "Class 9",
        "Class 8",
        "Class 7",
        "Class 6"
    ];

    public IReadOnlyList<string> SubjectFilters { get; } =
    [
        "All",
        "Science",
        "Mathematics",
        "Social Science",
        "Physics",
        "Chemistry",
        "Biology",
        "History",
        "Geography",
        "Political Science",
        "Economics",
        "English"
    ];

    public event Action<string>? BookOpenRequested;
    private readonly object _filteredLock = new();

    public NcertLibraryViewModel(INcertLibraryService? libraryService = null)
    {
        _libraryService = libraryService ?? new NcertLibraryService();
        try
        {
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(FilteredBooks, _filteredLock);
        }
        catch
        {
        }
        LoadCatalog();
    }

    public void LoadCatalog()
    {
        AllBooks.Clear();
        var books = _libraryService.GetAllBooks();
        foreach (var b in books)
        {
            AllBooks.Add(b);
        }

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedClassChanged(int value) => ApplyFilter();
    partial void OnSelectedClassOptionChanged(string value)
    {
        SelectedClass = value switch
        {
            "Class 12" => 12,
            "Class 11" => 11,
            "Class 10" => 10,
            "Class 9" => 9,
            "Class 8" => 8,
            "Class 7" => 7,
            "Class 6" => 6,
            _ => 0
        };
    }
    partial void OnSelectedSubjectChanged(string value) => ApplyFilter();
    partial void OnOnlyDownloadedChanged(bool value) => ApplyFilter();

    [RelayCommand]
    public void SelectClass(int classNum)
    {
        SelectedClass = classNum;
    }

    [RelayCommand]
    public void SelectSubject(string subject)
    {
        SelectedSubject = subject ?? "All";
    }

    [RelayCommand]
    public void ToggleOnlyDownloaded()
    {
        OnlyDownloaded = !OnlyDownloaded;
    }

    [RelayCommand]
    public async Task RefreshStatesAsync()
    {
        await _libraryService.RefreshDownloadStatesAsync(AllBooks);
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(ApplyFilter);
            return;
        }

        FilteredBooks.Clear();
        string q = SearchText?.Trim() ?? string.Empty;

        var matching = AllBooks.Where(b =>
        {
            if (SelectedClass > 0 && b.ClassNumber != SelectedClass)
            {
                return false;
            }

            if (!string.Equals(SelectedSubject, "All", StringComparison.OrdinalIgnoreCase))
            {
                bool matchSubject = string.Equals(b.Subject, SelectedSubject, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(b.SubjectCategory, SelectedSubject, StringComparison.OrdinalIgnoreCase);
                if (!matchSubject)
                {
                    return false;
                }
            }

            if (OnlyDownloaded && !b.IsDownloaded)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                bool hit = b.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                           b.ShortTitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                           b.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                           b.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                           b.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                           b.ClassDisplay.Contains(q, StringComparison.OrdinalIgnoreCase);
                if (!hit)
                {
                    return false;
                }
            }

            return true;
        }).ToList();

        foreach (var b in matching)
        {
            FilteredBooks.Add(b);
        }

        HasNoMatchingBooks = FilteredBooks.Count == 0;
        int downloadedCount = AllBooks.Count(b => b.IsDownloaded);
        SummaryText = $"Showing {FilteredBooks.Count} NCERT Textbook{(FilteredBooks.Count == 1 ? "" : "s")} • {downloadedCount} Downloaded Locally";
    }

    [RelayCommand]
    public async Task DownloadBookAsync(NcertBookItem? book)
    {
        if (book is null || book.IsDownloading)
        {
            return;
        }

        GlobalStatusMessage = $"Downloading {book.Title}...";
        try
        {
            string primaryPath = await _libraryService.DownloadBookAsync(book);
            GlobalStatusMessage = $"Successfully downloaded {book.Title}!";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            book.IsDownloading = false;
            book.DownloadStatusText = $"Download failed: {ex.Message}";
            GlobalStatusMessage = $"Failed to download {book.ShortTitle}: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenBook(NcertBookItem? book)
    {
        if (book is null)
        {
            return;
        }

        if (!book.IsDownloaded)
        {
            _ = DownloadBookAsync(book);
            return;
        }

        if (!string.IsNullOrWhiteSpace(book.PrimaryPdfPath) && File.Exists(book.PrimaryPdfPath))
        {
            BookOpenRequested?.Invoke(book.PrimaryPdfPath);
        }
        else if (book.Chapters.Count > 0 && !string.IsNullOrWhiteSpace(book.Chapters[0].FilePath) && File.Exists(book.Chapters[0].FilePath))
        {
            BookOpenRequested?.Invoke(book.Chapters[0].FilePath!);
        }
    }

    [RelayCommand]
    public void OpenChapter(NcertChapterItem? chapter)
    {
        if (chapter is null || string.IsNullOrWhiteSpace(chapter.FilePath) || !File.Exists(chapter.FilePath))
        {
            return;
        }

        BookOpenRequested?.Invoke(chapter.FilePath);
    }

    [RelayCommand]
    public void ToggleChapters(NcertBookItem? book)
    {
        if (book is null)
        {
            return;
        }

        book.AreChaptersExpanded = !book.AreChaptersExpanded;
    }

    [RelayCommand]
    public async Task DeleteBookAsync(NcertBookItem? book)
    {
        if (book is null)
        {
            return;
        }

        await _libraryService.DeleteBookAsync(book);
        GlobalStatusMessage = $"Removed local download of {book.ShortTitle}.";
        ApplyFilter();
    }

    [RelayCommand]
    public void OpenLibraryFolder()
    {
        string dir = _libraryService.GetLibraryRootDirectory();
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
    }
}
