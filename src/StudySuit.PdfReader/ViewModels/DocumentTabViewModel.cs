using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.ViewModels;

public sealed partial class DocumentTabViewModel : ObservableObject, IDisposable
{
    public static IReadOnlyList<string> ColorPalette { get; } =
    [
        "#FFFFE600", // Yellow
        "#FF4ADE80", // Mint Green
        "#FF38BDF8", // Sky Blue
        "#FFFB7185", // Rose Pink
        "#FFC084FC", // Lavender
        "#FFF97316", // Amber Orange
        "#FFEF4444"  // Crimson Red
    ];

    public static IReadOnlyList<double> StrokePresets { get; } = [2.0, 4.0, 7.0];

    private readonly IPdfService _pdfService;
    private readonly IAnnotationService? _annotationService;
    private readonly string? _password;
    private readonly Action<string, int>? _onPageChanged;
    private readonly Action<string, int>? _onAnnotationsChanged;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Stack<List<PdfAnnotation>> _undoStack = new();
    private readonly Stack<List<PdfAnnotation>> _redoStack = new();
    private readonly Dictionary<int, IReadOnlyList<PdfWordBox>> _pageWordsCache = [];
    private CancellationTokenSource? _renderCts;
    private CancellationTokenSource? _searchCts;
    private double _viewportWidth = 900;
    private double _viewportHeight = 700;
    private double _dpiScale = 1.0;
    private bool _suppressZoomChangedRender;
    private bool _disposed;

    public PdfDocumentInfo DocumentInfo { get; }
    public string FilePath => DocumentInfo.FilePath;
    public string TabTitle => !string.IsNullOrWhiteSpace(DocumentInfo.Title)
        ? $"{DocumentInfo.FileName}"
        : DocumentInfo.FileName;

    public int PageCount => DocumentInfo.PageCount;

    public ObservableCollection<PdfPageViewModel> Pages { get; } = [];
    public ObservableCollection<PdfPageViewModel> VisiblePages { get; } = [];
    public ObservableCollection<PdfBookmarkItem> Bookmarks { get; } = [];
    public ObservableCollection<SearchMatch> SearchResults { get; } = [];
    public ObservableCollection<PdfAnnotation> Annotations { get; } = [];
    public ObservableCollection<PdfAnnotation> FilteredAnnotations { get; } = [];

    [ObservableProperty]
    private int _currentPageNumber = 1;

    [ObservableProperty]
    private string _pageNumberInput = "1";

    [ObservableProperty]
    private double _zoomPercent = 100.0;

    [ObservableProperty]
    private PdfFitMode _fitMode = PdfFitMode.FitWidth;

    [ObservableProperty]
    private PdfViewMode _viewMode = PdfViewMode.Continuous;

    [ObservableProperty]
    private int _rotationDegrees;

    [ObservableProperty]
    private bool _isNightReadingMode;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _caseSensitiveSearch;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _currentSearchIndex = -1;

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private AnnotationTool _activeTool = AnnotationTool.Pan;

    [ObservableProperty]
    private string _selectedColorHex = "#FFFFE600";

    [ObservableProperty]
    private double _selectedStrokeWidth = 3.0;

    [ObservableProperty]
    private double _selectedFontSize = 14.0;

    [ObservableProperty]
    private string _annotationFilterText = string.Empty;

    [ObservableProperty]
    private bool _isAnnotationBarVisible = true;

    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasAnnotations => Annotations.Count > 0;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool IsContinuousMode => ViewMode == PdfViewMode.Continuous;
    public bool IsSinglePageMode => ViewMode == PdfViewMode.SinglePage;
    public bool IsFitWidth => FitMode == PdfFitMode.FitWidth;
    public bool IsFitPage => FitMode == PdfFitMode.FitPage;
    public string FileSizeDisplay => FormatFileSize(DocumentInfo.FileSizeBytes);

    public event Action<int>? ScrollToPageRequested;

    public DocumentTabViewModel(
        PdfDocumentInfo documentInfo,
        IPdfService pdfService,
        string? password = null,
        int initialPage = 1,
        Action<string, int>? onPageChanged = null,
        IAnnotationService? annotationService = null,
        Action<string, int>? onAnnotationsChanged = null)
    {
        DocumentInfo = documentInfo;
        _pdfService = pdfService;
        _password = password;
        _onPageChanged = onPageChanged;
        _annotationService = annotationService;
        _onAnnotationsChanged = onAnnotationsChanged;

        for (int i = 0; i < documentInfo.PageCount; i++)
        {
            var size = i < documentInfo.PageSizes.Count
                ? documentInfo.PageSizes[i]
                : new PdfPageSize(612, 792);
            Pages.Add(new PdfPageViewModel(i + 1, size.WidthPoints, size.HeightPoints));
        }

        foreach (var bm in documentInfo.Bookmarks)
        {
            Bookmarks.Add(bm);
        }

        if (_annotationService is not null)
        {
            foreach (var ann in _annotationService.LoadAnnotations(documentInfo.FilePath))
            {
                Annotations.Add(ann);
            }
        }

        _currentPageNumber = Math.Clamp(initialPage, 1, Math.Max(1, PageCount));
        _pageNumberInput = _currentPageNumber.ToString();

        RefreshVisiblePages();
        RecalculateLayout();
        RefreshFilteredAnnotations();
        MarkCurrentPageFlag();
        _ = LoadThumbnailsAsync();
    }

    public void UpdateViewportMetrics(double width, double height, double dpiScale)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        bool sizeChanged = Math.Abs(_viewportWidth - width) > 2 || Math.Abs(_viewportHeight - height) > 2;
        bool scaleChanged = Math.Abs(_dpiScale - dpiScale) > 0.05;

        _viewportWidth = width;
        _viewportHeight = height;
        _dpiScale = Math.Clamp(dpiScale, 1.0, 3.0);

        if (sizeChanged && FitMode != PdfFitMode.Custom)
        {
            RecalculateLayout();
            _ = RenderAroundCurrentPageAsync();
        }
        else if (scaleChanged)
        {
            _ = RenderAroundCurrentPageAsync();
        }
    }

    partial void OnCurrentPageNumberChanged(int value)
    {
        int clamped = Math.Clamp(value, 1, Math.Max(1, PageCount));
        if (clamped != value)
        {
            CurrentPageNumber = clamped;
            return;
        }

        PageNumberInput = clamped.ToString();
        MarkCurrentPageFlag();

        if (ViewMode == PdfViewMode.SinglePage)
        {
            RefreshVisiblePages();
        }

        _onPageChanged?.Invoke(FilePath, clamped);
        _ = RenderAroundCurrentPageAsync();
    }

    partial void OnZoomPercentChanged(double value)
    {
        double clamped = Math.Clamp(Math.Round(value, 1), 25.0, 400.0);
        if (Math.Abs(clamped - value) > 0.05)
        {
            ZoomPercent = clamped;
            return;
        }

        RecalculatePageDimensionsAndHighlights();
        if (!_suppressZoomChangedRender)
        {
            _ = RenderAroundCurrentPageAsync();
        }
    }

    partial void OnFitModeChanged(PdfFitMode value)
    {
        OnPropertyChanged(nameof(IsFitWidth));
        OnPropertyChanged(nameof(IsFitPage));
        RecalculateLayout();
        _ = RenderAroundCurrentPageAsync();
    }

    partial void OnViewModeChanged(PdfViewMode value)
    {
        OnPropertyChanged(nameof(IsContinuousMode));
        OnPropertyChanged(nameof(IsSinglePageMode));
        RefreshVisiblePages();
        RecalculateLayout();
        _ = RenderAroundCurrentPageAsync();
    }

    partial void OnRotationDegreesChanged(int value)
    {
        RecalculateLayout();
        _ = RenderAroundCurrentPageAsync();
    }

    partial void OnIsNightReadingModeChanged(bool value)
    {
        StatusMessage = value ? "Night reading mode enabled" : "Standard reading mode";
        _ = RenderAroundCurrentPageAsync();
    }

    public void SetVisiblePageFromScroll(int pageNumber)
    {
        if (ViewMode != PdfViewMode.Continuous || pageNumber < 1 || pageNumber > PageCount)
        {
            return;
        }

        if (CurrentPageNumber != pageNumber)
        {
            CurrentPageNumber = pageNumber;
        }
    }

    [RelayCommand]
    public void GoToPage(int pageNumber)
    {
        if (PageCount == 0)
        {
            return;
        }

        int target = Math.Clamp(pageNumber, 1, PageCount);
        CurrentPageNumber = target;
        ScrollToPageRequested?.Invoke(target);
    }

    public void CommitPageNumberInput()
    {
        if (int.TryParse(PageNumberInput?.Trim(), out int page))
        {
            GoToPage(page);
        }
        else
        {
            PageNumberInput = CurrentPageNumber.ToString();
        }
    }

    [RelayCommand]
    public void NextPage() => GoToPage(CurrentPageNumber + 1);

    [RelayCommand]
    public void PreviousPage() => GoToPage(CurrentPageNumber - 1);

    [RelayCommand]
    public void FirstPage() => GoToPage(1);

    [RelayCommand]
    public void LastPage() => GoToPage(PageCount);

    [RelayCommand]
    public void ZoomIn()
    {
        FitMode = PdfFitMode.Custom;
        ZoomPercent = Math.Min(400, ZoomPercent + 15);
    }

    [RelayCommand]
    public void ZoomOut()
    {
        FitMode = PdfFitMode.Custom;
        ZoomPercent = Math.Max(25, ZoomPercent - 15);
    }

    public void SetCustomZoom(double zoomPercent)
    {
        FitMode = PdfFitMode.Custom;
        ZoomPercent = zoomPercent;
    }

    [RelayCommand]
    public void SetFitWidth() => FitMode = PdfFitMode.FitWidth;

    [RelayCommand]
    public void SetFitPage() => FitMode = PdfFitMode.FitPage;

    [RelayCommand]
    public void SetActualSize()
    {
        FitMode = PdfFitMode.Custom;
        ZoomPercent = 100;
    }

    [RelayCommand]
    public void RotateRight()
    {
        RotationDegrees = (RotationDegrees + 90) % 360;
    }

    [RelayCommand]
    public void RotateLeft()
    {
        RotationDegrees = (RotationDegrees + 270) % 360;
    }

    [RelayCommand]
    public void ToggleViewMode()
    {
        ViewMode = ViewMode == PdfViewMode.Continuous
            ? PdfViewMode.SinglePage
            : PdfViewMode.Continuous;
    }

    [RelayCommand]
    public void ToggleNightReadingMode()
    {
        IsNightReadingMode = !IsNightReadingMode;
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var ct = _searchCts.Token;

        SearchResults.Clear();
        CurrentSearchIndex = -1;
        OnPropertyChanged(nameof(HasSearchResults));

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchSummary = string.Empty;
            UpdateAllPageHighlights();
            return;
        }

        IsSearching = true;
        SearchSummary = "Searching...";

        try
        {
            var matches = await _pdfService.SearchAsync(
                FilePath,
                SearchQuery,
                _password,
                CaseSensitiveSearch,
                ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            foreach (var match in matches)
            {
                SearchResults.Add(match);
            }

            OnPropertyChanged(nameof(HasSearchResults));

            if (SearchResults.Count > 0)
            {
                SelectSearchMatch(0);
            }
            else
            {
                SearchSummary = "No matches";
                UpdateAllPageHighlights();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SearchSummary = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public void NextSearchMatch()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        int next = (CurrentSearchIndex + 1) % SearchResults.Count;
        SelectSearchMatch(next);
    }

    [RelayCommand]
    public void PreviousSearchMatch()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        int prev = (CurrentSearchIndex - 1 + SearchResults.Count) % SearchResults.Count;
        SelectSearchMatch(prev);
    }

    [RelayCommand]
    public void ClearSearch()
    {
        _searchCts?.Cancel();
        SearchQuery = string.Empty;
        SearchResults.Clear();
        CurrentSearchIndex = -1;
        SearchSummary = string.Empty;
        OnPropertyChanged(nameof(HasSearchResults));
        UpdateAllPageHighlights();
    }

    public void SelectSearchMatch(int index)
    {
        if (index < 0 || index >= SearchResults.Count)
        {
            return;
        }

        CurrentSearchIndex = index;
        var match = SearchResults[index];
        SearchSummary = $"{index + 1} of {SearchResults.Count}";
        UpdateAllPageHighlights();
        GoToPage(match.PageNumber);
    }

    public async Task<string> ExtractTextInSelectionAsync(int pageNumber, NormalizedRect displaySelection)
    {
        var unrotatedRect = displaySelection.Unrotate(RotationDegrees);
        var words = await _pdfService.GetPageWordsAsync(FilePath, pageNumber, _password, _lifetimeCts.Token);
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var selected = words
            .Where(w => w.Bounds.Intersects(unrotatedRect))
            .Select(w => w.Text)
            .ToList();

        return string.Join(' ', selected);
    }

    public async Task<string> GetCurrentPageTextAsync()
    {
        return await _pdfService.GetPageTextAsync(FilePath, CurrentPageNumber, _password, _lifetimeCts.Token);
    }

    public async Task<string> GetPageTextForAiAsync(int pageNumber, CancellationToken ct)
    {
        int clamped = Math.Clamp(pageNumber, 1, Math.Max(1, PageCount));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        string text = await _pdfService.GetPageTextAsync(FilePath, clamped, _password, linked.Token);
        return text.Length > 6000 ? text[..6000] + "…" : text;
    }

    public async Task<IReadOnlyList<SearchMatch>> SearchAllPagesForAiAsync(string query, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        return await _pdfService.SearchAsync(FilePath, query, _password, caseSensitive: false, linked.Token);
    }

    public async Task ExportCurrentPagePngAsync(string outputPath)
    {
        StatusMessage = $"Exporting page {CurrentPageNumber}...";
        await _pdfService.ExportPageAsPngAsync(
            FilePath,
            CurrentPageNumber,
            outputPath,
            dpi: 200,
            RotationDegrees,
            IsNightReadingMode,
            _password,
            _lifetimeCts.Token);
        StatusMessage = $"Exported page {CurrentPageNumber} to {Path.GetFileName(outputPath)}";
    }

    public async Task RenderAroundCurrentPageAsync()
    {
        if (_disposed || Pages.Count == 0)
        {
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var ct = _renderCts.Token;

        int targetDpi = CalculateTargetRenderDpi();
        int center = CurrentPageNumber;
        int rotation = RotationDegrees;
        bool invert = IsNightReadingMode;

        var priorityOrder = new List<int> { center };
        if (ViewMode == PdfViewMode.Continuous)
        {
            for (int offset = 1; offset <= 3; offset++)
            {
                if (center + offset <= PageCount)
                {
                    priorityOrder.Add(center + offset);
                }

                if (center - offset >= 1)
                {
                    priorityOrder.Add(center - offset);
                }
            }
        }
        else
        {
            if (center + 1 <= PageCount)
            {
                priorityOrder.Add(center + 1);
            }
        }

        // Evict distant full-resolution bitmaps on large documents to keep memory bounded.
        if (PageCount > 25)
        {
            foreach (var page in Pages)
            {
                if (Math.Abs(page.PageNumber - center) > 12 && page.RenderedImage is not null)
                {
                    page.RenderedImage = null;
                    page.RenderedDpi = 0;
                }
            }
        }

        foreach (int pageNum in priorityOrder)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var pageVm = Pages[pageNum - 1];
            bool upToDate = pageVm.RenderedImage is not null &&
                            pageVm.RenderedDpi == targetDpi &&
                            pageVm.RenderedRotation == rotation &&
                            pageVm.RenderedInverted == invert;
            if (upToDate)
            {
                continue;
            }

            pageVm.IsLoading = true;
            try
            {
                var bitmap = await _pdfService.RenderPageAsync(
                    FilePath,
                    pageNum,
                    targetDpi,
                    rotation,
                    invert,
                    _password,
                    ct);

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                pageVm.RenderedImage = bitmap;
                pageVm.RenderedDpi = targetDpi;
                pageVm.RenderedRotation = rotation;
                pageVm.RenderedInverted = invert;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Render warning (page {pageNum}): {ex.Message}";
            }
            finally
            {
                pageVm.IsLoading = false;
            }
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        var ct = _lifetimeCts.Token;
        // Start from around the current page so visible thumbnails appear immediately.
        var ordered = Pages
            .OrderBy(p => Math.Abs(p.PageNumber - CurrentPageNumber))
            .ToList();

        foreach (var pageVm in ordered)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (pageVm.ThumbnailImage is not null)
            {
                continue;
            }

            try
            {
                var thumb = await _pdfService.RenderPageAsync(
                    FilePath,
                    pageVm.PageNumber,
                    dpi: 36,
                    rotationDegrees: 0,
                    invertColors: false,
                    _password,
                    ct);

                if (!ct.IsCancellationRequested)
                {
                    pageVm.ThumbnailImage = thumb;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Skip failed thumbnail gracefully.
            }
        }
    }

    private void RecalculateLayout()
    {
        if (Pages.Count == 0)
        {
            return;
        }

        if (FitMode != PdfFitMode.Custom)
        {
            var refPage = Pages[Math.Clamp(CurrentPageNumber - 1, 0, Pages.Count - 1)];
            bool swapped = ((RotationDegrees % 180) + 180) % 180 == 90;
            double ptsW = swapped ? refPage.HeightPoints : refPage.WidthPoints;
            double ptsH = swapped ? refPage.WidthPoints : refPage.HeightPoints;
            const double ptsToDip = 96.0 / 72.0;

            double availW = Math.Max(200, _viewportWidth - 56);
            double availH = Math.Max(200, _viewportHeight - 40);

            double computedZoom = FitMode == PdfFitMode.FitWidth
                ? availW / (ptsW * ptsToDip) * 100.0
                : Math.Min(availW / (ptsW * ptsToDip), availH / (ptsH * ptsToDip)) * 100.0;

            double clamped = Math.Clamp(Math.Round(computedZoom, 1), 25.0, 400.0);
            if (Math.Abs(clamped - ZoomPercent) > 0.2)
            {
                _suppressZoomChangedRender = true;
                try
                {
                    ZoomPercent = clamped;
                }
                finally
                {
                    _suppressZoomChangedRender = false;
                }
            }
        }

        RecalculatePageDimensionsAndHighlights();
    }

    private void RecalculatePageDimensionsAndHighlights()
    {
        double zoomFactor = ZoomPercent / 100.0;
        foreach (var page in Pages)
        {
            page.UpdateDisplaySize(zoomFactor, RotationDegrees);
        }

        UpdateAllPageHighlights();
        UpdateAllPageAnnotations();
    }

    private void RefreshVisiblePages()
    {
        VisiblePages.Clear();
        if (Pages.Count == 0)
        {
            return;
        }

        if (ViewMode == PdfViewMode.Continuous)
        {
            foreach (var p in Pages)
            {
                VisiblePages.Add(p);
            }
        }
        else
        {
            int idx = Math.Clamp(CurrentPageNumber - 1, 0, Pages.Count - 1);
            VisiblePages.Add(Pages[idx]);
        }
    }

    private void MarkCurrentPageFlag()
    {
        foreach (var page in Pages)
        {
            page.IsCurrentPage = page.PageNumber == CurrentPageNumber;
        }
    }

    partial void OnActiveToolChanged(AnnotationTool value)
    {
        StatusMessage = value switch
        {
            AnnotationTool.Pan => "Pan & scroll mode",
            AnnotationTool.SelectText => "Text selection: drag across text to copy",
            AnnotationTool.Highlight => "Highlight tool: drag across text or region",
            AnnotationTool.Underline => "Underline tool: drag across text or region",
            AnnotationTool.Strikethrough => "Strikethrough tool: drag across text or region",
            AnnotationTool.Ink => "Ink pen: draw freely on any page",
            AnnotationTool.Rectangle => "Rectangle tool: drag to draw a callout box",
            AnnotationTool.Ellipse => "Ellipse tool: drag to draw a circle or oval",
            AnnotationTool.Arrow => "Arrow tool: drag from start to target",
            AnnotationTool.StickyNote => "Sticky note: click anywhere on a page to add a note",
            AnnotationTool.TextBox => "Text callout: click anywhere on a page to place text",
            AnnotationTool.Eraser => "Eraser: click any highlight, stroke, shape, or note to delete",
            _ => "Ready"
        };
    }

    partial void OnAnnotationFilterTextChanged(string value) => RefreshFilteredAnnotations();

    [RelayCommand]
    public void SetActiveTool(AnnotationTool tool) => ActiveTool = tool;

    [RelayCommand]
    public void SetColor(string colorHex)
    {
        if (!string.IsNullOrWhiteSpace(colorHex))
        {
            SelectedColorHex = colorHex;
        }
    }

    [RelayCommand]
    public void SetStrokeWidth(double width)
    {
        SelectedStrokeWidth = Math.Clamp(width, 1.0, 16.0);
    }

    [RelayCommand]
    public void ToggleAnnotationBar() => IsAnnotationBarVisible = !IsAnnotationBarVisible;

    [RelayCommand]
    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(CloneAnnotationList(Annotations));
        RestoreAnnotationSnapshot(_undoStack.Pop());
        StatusMessage = "Undid annotation change";
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(CloneAnnotationList(Annotations));
        RestoreAnnotationSnapshot(_redoStack.Pop());
        StatusMessage = "Redid annotation change";
    }

    [RelayCommand]
    public void DeleteAnnotation(PdfAnnotation? annotation)
    {
        if (annotation is null)
        {
            return;
        }

        DeleteAnnotationById(annotation.Id);
    }

    public void DeleteAnnotationById(string annotationId)
    {
        var existing = Annotations.FirstOrDefault(a => a.Id == annotationId);
        if (existing is null)
        {
            return;
        }

        PushUndoSnapshot();
        Annotations.Remove(existing);
        CommitAnnotationChange($"Removed {existing.TypeDisplay.ToLowerInvariant()}");
    }

    [RelayCommand]
    public void ClearCurrentPageAnnotations()
    {
        var onPage = Annotations.Where(a => a.PageNumber == CurrentPageNumber).ToList();
        if (onPage.Count == 0)
        {
            StatusMessage = $"No annotations on page {CurrentPageNumber}";
            return;
        }

        PushUndoSnapshot();
        foreach (var ann in onPage)
        {
            Annotations.Remove(ann);
        }

        CommitAnnotationChange($"Cleared {onPage.Count} annotation(s) on page {CurrentPageNumber}");
    }

    public async Task<IReadOnlyList<PdfWordBox>> GetOrLoadPageWordsAsync(int pageNumber)
    {
        if (_pageWordsCache.TryGetValue(pageNumber, out var cached))
        {
            return cached;
        }

        var loaded = await _pdfService.GetPageWordsAsync(FilePath, pageNumber, _password, _lifetimeCts.Token);
        _pageWordsCache[pageNumber] = loaded;
        return loaded;
    }

    public async Task UpdateLiveWordSelectionAsync(
        int pageNumber,
        NormalizedPoint displayStart,
        NormalizedPoint displayEnd,
        System.Windows.Media.Brush previewBrush)
    {
        if (pageNumber < 1 || pageNumber > Pages.Count)
        {
            return;
        }

        var pageVm = Pages[pageNumber - 1];
        var words = await GetOrLoadPageWordsAsync(pageNumber);
        if (words.Count > 0)
        {
            var selected = GetSelectedWordsBetween(
                words,
                displayStart.Unrotate(RotationDegrees),
                displayEnd.Unrotate(RotationDegrees));
            var merged = MergeWordsIntoLineRects(selected);
            pageVm.UpdateLiveWordSelection(merged, RotationDegrees, previewBrush);
        }
        else
        {
            // Fallback for scanned/image-only pages with no selectable text layer.
            var s = displayStart.Unrotate(RotationDegrees);
            var e = displayEnd.Unrotate(RotationDegrees);
            var fallback = new NormalizedRect(
                Math.Min(s.X, e.X),
                Math.Min(s.Y, e.Y),
                Math.Abs(e.X - s.X),
                Math.Abs(e.Y - s.Y));
            pageVm.UpdateLiveWordSelection([fallback], RotationDegrees, previewBrush);
        }
    }

    public async Task<PdfAnnotation?> CommitWordSelectionAnnotationAsync(
        int pageNumber,
        NormalizedPoint displayStart,
        NormalizedPoint displayEnd,
        AnnotationType type)
    {
        if (pageNumber < 1 || pageNumber > Pages.Count)
        {
            return null;
        }

        Pages[pageNumber - 1].ClearLiveWordSelection();
        var words = await GetOrLoadPageWordsAsync(pageNumber);
        var unrotatedStart = displayStart.Unrotate(RotationDegrees);
        var unrotatedEnd = displayEnd.Unrotate(RotationDegrees);

        List<NormalizedRect> finalRects;
        string quotedText = string.Empty;

        if (words.Count > 0)
        {
            var slice = GetSelectedWordsBetween(words, unrotatedStart, unrotatedEnd);
            if (slice.Count == 0)
            {
                return null;
            }

            quotedText = string.Join(' ', slice.Select(w => w.Text));
            finalRects = MergeWordsIntoLineRects(slice);
        }
        else
        {
            double w = Math.Abs(unrotatedEnd.X - unrotatedStart.X);
            double h = Math.Abs(unrotatedEnd.Y - unrotatedStart.Y);
            if (w < 0.005 || h < 0.005)
            {
                return null;
            }

            finalRects =
            [
                new NormalizedRect(
                    Math.Min(unrotatedStart.X, unrotatedEnd.X),
                    Math.Min(unrotatedStart.Y, unrotatedEnd.Y),
                    w,
                    h)
            ];
        }

        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = type,
            ColorHex = SelectedColorHex,
            StrokeThickness = SelectedStrokeWidth,
            Rects = finalRects,
            Text = quotedText
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Added {annotation.TypeDisplay.ToLowerInvariant()} on page {pageNumber}");
        return annotation;
    }

    public async Task<string> CommitWordSelectionCopyAsync(
        int pageNumber,
        NormalizedPoint displayStart,
        NormalizedPoint displayEnd)
    {
        if (pageNumber < 1 || pageNumber > Pages.Count)
        {
            return string.Empty;
        }

        Pages[pageNumber - 1].ClearLiveWordSelection();
        var words = await GetOrLoadPageWordsAsync(pageNumber);
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var slice = GetSelectedWordsBetween(
            words,
            displayStart.Unrotate(RotationDegrees),
            displayEnd.Unrotate(RotationDegrees));

        return string.Join(' ', slice.Select(w => w.Text));
    }

    internal static IReadOnlyList<PdfWordBox> GetSelectedWordsBetween(
        IReadOnlyList<PdfWordBox> words,
        NormalizedPoint unrotatedStart,
        NormalizedPoint unrotatedEnd)
    {
        if (words.Count == 0)
        {
            return [];
        }

        int startIdx = FindNearestWordIndex(words, unrotatedStart);
        int endIdx = FindNearestWordIndex(words, unrotatedEnd);
        if (startIdx < 0 || endIdx < 0)
        {
            return [];
        }

        int min = Math.Min(startIdx, endIdx);
        int max = Math.Max(startIdx, endIdx);
        var slice = new List<PdfWordBox>((max - min) + 1);
        for (int i = min; i <= max; i++)
        {
            slice.Add(words[i]);
        }

        return slice;
    }

    internal static int FindNearestWordIndex(IReadOnlyList<PdfWordBox> words, NormalizedPoint pt)
    {
        if (words.Count == 0)
        {
            return -1;
        }

        // First look for a direct bounding-box hit (with small padding).
        const double pad = 0.004;
        for (int i = 0; i < words.Count; i++)
        {
            var b = words[i].Bounds;
            if (pt.X >= b.X - pad && pt.X <= b.X + b.Width + pad &&
                pt.Y >= b.Y - pad && pt.Y <= b.Y + b.Height + pad)
            {
                return i;
            }
        }

        // Otherwise choose the closest word, weighting vertical line offset higher so horizontal drags stay on-line.
        int bestIdx = 0;
        double bestScore = double.MaxValue;
        for (int i = 0; i < words.Count; i++)
        {
            var b = words[i].Bounds;
            double dx = Math.Max(0, Math.Max(b.X - pt.X, pt.X - (b.X + b.Width)));
            double dy = Math.Max(0, Math.Max(b.Y - pt.Y, pt.Y - (b.Y + b.Height)));
            double score = (dx * dx) + (dy * 2.5) * (dy * 2.5);
            if (score < bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    public async Task<PdfAnnotation?> AddRegionAnnotationAsync(
        int pageNumber,
        NormalizedRect displayRect,
        AnnotationType type)
    {
        var unrotated = displayRect.Unrotate(RotationDegrees);
        List<NormalizedRect> finalRects = [unrotated];
        string quotedText = string.Empty;

        if (type is AnnotationType.Highlight or AnnotationType.Underline or AnnotationType.Strikethrough)
        {
            var words = await _pdfService.GetPageWordsAsync(FilePath, pageNumber, _password, _lifetimeCts.Token);
            var hitWords = words.Where(w => w.Bounds.Intersects(unrotated)).ToList();
            if (hitWords.Count > 0)
            {
                quotedText = string.Join(' ', hitWords.Select(w => w.Text));
                finalRects = MergeWordsIntoLineRects(hitWords);
            }
        }

        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = type,
            ColorHex = SelectedColorHex,
            StrokeThickness = SelectedStrokeWidth,
            Rects = finalRects,
            Text = quotedText
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Added {annotation.TypeDisplay.ToLowerInvariant()} on page {pageNumber}");
        return annotation;
    }

    public async Task<bool> PaintBrushStepAsync(
        int pageNumber,
        NormalizedPoint displayPrev,
        NormalizedPoint displayCurr,
        HashSet<int> paintedWordIndices,
        System.Windows.Media.Brush previewBrush,
        AnnotationType type = AnnotationType.Highlight)
    {
        if (pageNumber < 1 || pageNumber > Pages.Count)
        {
            return false;
        }

        var pageVm = Pages[pageNumber - 1];
        var words = await GetOrLoadPageWordsAsync(pageNumber);
        if (words.Count == 0)
        {
            return false;
        }

        var p1 = displayPrev.Unrotate(RotationDegrees);
        var p2 = displayCurr.Unrotate(RotationDegrees);
        double padTop = type == AnnotationType.Underline ? 0.005 : 0.010;
        double padBottom = type == AnnotationType.Underline ? 0.030 : 0.010;
        double padSide = 0.008;

        bool addedAny = false;
        for (int i = 0; i < words.Count; i++)
        {
            var b = words[i].Bounds;
            var padded = new NormalizedRect(
                b.X - padSide,
                b.Y - padTop,
                b.Width + padSide * 2,
                b.Height + padTop + padBottom);

            if (SegmentIntersectsRect(p1, p2, padded) && paintedWordIndices.Add(i))
            {
                addedAny = true;
            }
        }

        if (addedAny)
        {
            var selected = paintedWordIndices.OrderBy(i => i).Select(i => words[i]).ToList();
            var merged = MergeWordsIntoLineRects(selected);
            pageVm.UpdateLiveWordSelection(merged, RotationDegrees, previewBrush, type);
        }

        return paintedWordIndices.Count > 0;
    }

    public async Task<PdfAnnotation?> CommitWordBrushAnnotationAsync(
        int pageNumber,
        HashSet<int> paintedWordIndices,
        IReadOnlyList<NormalizedPoint> displayStrokePoints,
        AnnotationType type)
    {
        if (pageNumber < 1 || pageNumber > Pages.Count)
        {
            return null;
        }

        Pages[pageNumber - 1].ClearLiveWordSelection();
        var words = await GetOrLoadPageWordsAsync(pageNumber);

        if (paintedWordIndices.Count > 0 && words.Count > 0)
        {
            var selected = paintedWordIndices
                .Where(i => i >= 0 && i < words.Count)
                .OrderBy(i => i)
                .Select(i => words[i])
                .ToList();

            if (selected.Count > 0)
            {
                var annotation = new PdfAnnotation
                {
                    PageNumber = pageNumber,
                    Type = type,
                    ColorHex = SelectedColorHex,
                    StrokeThickness = SelectedStrokeWidth,
                    Rects = MergeWordsIntoLineRects(selected),
                    Text = string.Join(' ', selected.Select(w => w.Text))
                };

                PushUndoSnapshot();
                Annotations.Add(annotation);
                CommitAnnotationChange($"Painted {annotation.TypeDisplay.ToLowerInvariant()} on page {pageNumber}");
                return annotation;
            }
        }

        // If the user painted anywhere without text underneath (margin, image, scanned page), save freehand stroke!
        if (displayStrokePoints.Count >= 2)
        {
            if (type is AnnotationType.Underline or AnnotationType.Strikethrough)
            {
                var stroke = displayStrokePoints
                    .Select(p => p.Unrotate(RotationDegrees))
                    .ToList();

                var annotation = new PdfAnnotation
                {
                    PageNumber = pageNumber,
                    Type = type,
                    ColorHex = SelectedColorHex,
                    StrokeThickness = Math.Max(2.0, SelectedStrokeWidth),
                    Points = stroke
                };

                PushUndoSnapshot();
                Annotations.Add(annotation);
                CommitAnnotationChange($"Drew {annotation.TypeDisplay.ToLowerInvariant()} on page {pageNumber}");
                return annotation;
            }

            return AddBrushAnnotation(pageNumber, displayStrokePoints);
        }

        return null;
    }

    internal static bool SegmentIntersectsRect(NormalizedPoint p1, NormalizedPoint p2, NormalizedRect rect)
    {
        double minX = rect.X;
        double maxX = rect.X + rect.Width;
        double minY = rect.Y;
        double maxY = rect.Y + rect.Height;

        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;

        double t0 = 0.0;
        double t1 = 1.0;

        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12)
            {
                return q >= 0;
            }

            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }

            return true;
        }

        return Clip(-dx, p1.X - minX) &&
               Clip(dx, maxX - p1.X) &&
               Clip(-dy, p1.Y - minY) &&
               Clip(dy, maxY - p1.Y);
    }

    public PdfAnnotation AddBrushAnnotation(int pageNumber, IReadOnlyList<NormalizedPoint> displayPoints)
    {
        var unrotatedPoints = displayPoints.Select(p => p.Unrotate(RotationDegrees)).ToList();
        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = AnnotationType.Brush,
            ColorHex = SelectedColorHex,
            StrokeThickness = SelectedStrokeWidth,
            Points = unrotatedPoints
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Painted brush stroke on page {pageNumber}");
        return annotation;
    }

    public PdfAnnotation AddInkAnnotation(int pageNumber, IReadOnlyList<NormalizedPoint> displayPoints)
    {
        var unrotatedPoints = displayPoints.Select(p => p.Unrotate(RotationDegrees)).ToList();
        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = AnnotationType.Ink,
            ColorHex = SelectedColorHex,
            StrokeThickness = SelectedStrokeWidth,
            Points = unrotatedPoints
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Added ink stroke on page {pageNumber}");
        return annotation;
    }

    public PdfAnnotation AddArrowAnnotation(int pageNumber, NormalizedPoint displayStart, NormalizedPoint displayEnd)
    {
        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = AnnotationType.Arrow,
            ColorHex = SelectedColorHex,
            StrokeThickness = SelectedStrokeWidth,
            Points =
            [
                displayStart.Unrotate(RotationDegrees),
                displayEnd.Unrotate(RotationDegrees)
            ]
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Added arrow on page {pageNumber}");
        return annotation;
    }

    public PdfAnnotation AddStickyNote(int pageNumber, NormalizedPoint displayPoint, string noteText)
    {
        var unrotated = displayPoint.Unrotate(RotationDegrees);
        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = AnnotationType.StickyNote,
            ColorHex = SelectedColorHex,
            Rects = [new NormalizedRect(unrotated.X, unrotated.Y, 0.04, 0.04)],
            NoteComment = noteText
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Added sticky note on page {pageNumber}");
        return annotation;
    }

    public PdfAnnotation AddTextBox(int pageNumber, NormalizedPoint displayPoint, string text)
    {
        var unrotated = displayPoint.Unrotate(RotationDegrees);
        var annotation = new PdfAnnotation
        {
            PageNumber = pageNumber,
            Type = AnnotationType.TextBox,
            ColorHex = SelectedColorHex,
            FontSize = SelectedFontSize,
            Rects = [new NormalizedRect(unrotated.X, unrotated.Y, 0.25, 0.06)],
            Text = text
        };

        PushUndoSnapshot();
        Annotations.Add(annotation);
        CommitAnnotationChange($"Added text callout on page {pageNumber}");
        return annotation;
    }

    public PdfAnnotation? GetAnnotationById(string annotationId) =>
        Annotations.FirstOrDefault(a => a.Id == annotationId);

    public void UpdateAnnotationText(string annotationId, string newText)
    {
        var ann = GetAnnotationById(annotationId);
        if (ann is null)
        {
            return;
        }

        PushUndoSnapshot();
        if (ann.Type == AnnotationType.TextBox)
        {
            ann.Text = newText;
        }
        else
        {
            ann.NoteComment = newText;
        }

        CommitAnnotationChange("Updated annotation note");
    }

    public string ExportAnnotationsAsMarkdown()
    {
        return _annotationService?.ExportToMarkdown(DocumentInfo, Annotations) ?? string.Empty;
    }

    public void JumpToAnnotation(PdfAnnotation? annotation)
    {
        if (annotation is null)
        {
            return;
        }

        GoToPage(annotation.PageNumber);
    }

    private void PushUndoSnapshot()
    {
        _undoStack.Push(CloneAnnotationList(Annotations));
        _redoStack.Clear();
    }

    private void RestoreAnnotationSnapshot(List<PdfAnnotation> snapshot)
    {
        Annotations.Clear();
        foreach (var item in snapshot)
        {
            Annotations.Add(item.Clone());
        }

        CommitAnnotationChange(StatusMessage);
    }

    private void CommitAnnotationChange(string status)
    {
        StatusMessage = status;
        OnPropertyChanged(nameof(HasAnnotations));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        RefreshFilteredAnnotations();
        UpdateAllPageAnnotations();
        _annotationService?.SaveAnnotations(FilePath, Annotations);
        _onAnnotationsChanged?.Invoke(FilePath, Annotations.Count);
    }

    private void RefreshFilteredAnnotations()
    {
        FilteredAnnotations.Clear();
        string filter = AnnotationFilterText?.Trim() ?? string.Empty;

        var items = Annotations
            .OrderBy(a => a.PageNumber)
            .ThenBy(a => a.CreatedUtc)
            .Where(a => string.IsNullOrEmpty(filter) ||
                        a.SummaryText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        a.TypeDisplay.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        $"page {a.PageNumber}".Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredAnnotations.Add(item);
        }
    }

    private void UpdateAllPageAnnotations()
    {
        double zoomFactor = ZoomPercent / 100.0;
        var byPage = Annotations.GroupBy(a => a.PageNumber).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var page in Pages)
        {
            if (byPage.TryGetValue(page.PageNumber, out var list))
            {
                page.UpdateAnnotations(list, RotationDegrees, zoomFactor);
            }
            else if (page.VectorAnnotations.Count > 0 || page.PinAnnotations.Count > 0)
            {
                page.UpdateAnnotations([], RotationDegrees, zoomFactor);
            }
        }
    }

    internal static List<NormalizedRect> MergeWordsIntoLineRects(IReadOnlyList<PdfWordBox> words)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var sorted = words
            .OrderBy(w => Math.Round(w.Bounds.Y, 3))
            .ThenBy(w => w.Bounds.X)
            .ToList();

        var merged = new List<NormalizedRect>();
        NormalizedRect current = sorted[0].Bounds;

        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i].Bounds;
            double currentMidY = current.Y + current.Height * 0.5;
            double nextMidY = next.Y + next.Height * 0.5;
            double lineTolerance = Math.Max(current.Height, next.Height) * 0.6;
            double horizontalGap = next.X - (current.X + current.Width);

            if (Math.Abs(currentMidY - nextMidY) <= lineTolerance && horizontalGap <= 0.03)
            {
                double minX = Math.Min(current.X, next.X);
                double minY = Math.Min(current.Y, next.Y);
                double maxX = Math.Max(current.X + current.Width, next.X + next.Width);
                double maxY = Math.Max(current.Y + current.Height, next.Y + next.Height);
                current = new NormalizedRect(minX, minY, maxX - minX, maxY - minY);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    private static List<PdfAnnotation> CloneAnnotationList(IEnumerable<PdfAnnotation> source) =>
        source.Select(a => a.Clone()).ToList();

    private void UpdateAllPageHighlights()
    {
        var activeMatch = CurrentSearchIndex >= 0 && CurrentSearchIndex < SearchResults.Count
            ? SearchResults[CurrentSearchIndex]
            : null;

        var byPage = SearchResults
            .GroupBy(m => m.PageNumber)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var page in Pages)
        {
            if (byPage.TryGetValue(page.PageNumber, out var matches))
            {
                var rects = new List<(NormalizedRect Rect, bool IsActive)>();
                foreach (var match in matches)
                {
                    bool isActive = ReferenceEquals(match, activeMatch);
                    foreach (var rect in match.Rects)
                    {
                        rects.Add((rect, isActive));
                    }
                }

                page.UpdateHighlights(rects, RotationDegrees);
            }
            else if (page.HighlightRects.Count > 0)
            {
                page.HighlightRects.Clear();
            }
        }
    }

    private int CalculateTargetRenderDpi()
    {
        double rawDpi = 96.0 * (ZoomPercent / 100.0) * _dpiScale;
        return Math.Clamp((int)Math.Round(rawDpi), 72, 360);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
    }
}
