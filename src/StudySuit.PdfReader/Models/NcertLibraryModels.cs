using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace StudySuit.PdfReader.Models;

public sealed partial class NcertChapterItem : ObservableObject
{
    [ObservableProperty]
    private int _chapterNumber;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isAvailable;

    public string DisplayText => string.IsNullOrWhiteSpace(Title)
        ? $"Chapter {ChapterNumber}"
        : $"Ch {ChapterNumber}: {Title}";
}

public sealed partial class NcertBookItem : ObservableObject
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ShortTitle { get; init; } = string.Empty;
    public int ClassNumber { get; init; }
    public string ClassDisplay => $"Class {ClassNumber}";
    public string Subject { get; init; } = string.Empty;
    public string SubjectCategory { get; init; } = string.Empty;
    public string Medium { get; init; } = "English";
    public string Description { get; init; } = string.Empty;
    public string BadgeColorHex { get; init; } = "#3B82F6";
    public string EstimatedSizeDisplay { get; init; } = "~45 MB";

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgressPercent;

    [ObservableProperty]
    private string _downloadStatusText = "Available to Download";

    [ObservableProperty]
    private string? _primaryPdfPath;

    [ObservableProperty]
    private bool _areChaptersExpanded;

    private readonly object _chaptersLock = new();

    public ObservableCollection<NcertChapterItem> Chapters { get; } = [];

    public NcertBookItem()
    {
        try
        {
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(Chapters, _chaptersLock);
        }
        catch
        {
        }
    }

    public void UpdateChapters(IEnumerable<NcertChapterItem> newChapters)
    {
        lock (_chaptersLock)
        {
            Chapters.Clear();
            foreach (var ch in newChapters)
            {
                Chapters.Add(ch);
            }
        }
        OnPropertyChanged(nameof(HasMultipleChapters));
    }

    public void ClearChapters()
    {
        lock (_chaptersLock)
        {
            Chapters.Clear();
        }
        OnPropertyChanged(nameof(HasMultipleChapters));
    }

    public bool HasMultipleChapters => Chapters.Count > 1;

    public string StatusBadgeText => IsDownloading
        ? $"Downloading ({DownloadProgressPercent:0}%)"
        : (IsDownloaded ? "Downloaded • Ready" : "Official NCERT");

    public string ActionButtonText => IsDownloaded ? "Open Book in Reader" : "Download Book";

    partial void OnIsDownloadedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(ActionButtonText));
    }

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(ActionButtonText));
    }

    partial void OnDownloadProgressPercentChanged(double value)
    {
        OnPropertyChanged(nameof(StatusBadgeText));
    }
}

