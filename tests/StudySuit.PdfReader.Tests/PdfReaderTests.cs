using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;
using StudySuit.PdfReader.ViewModels;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace StudySuit.PdfReader.Tests;

public sealed class PdfReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PdfService _pdfService = new();

    public PdfReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"StudySuitPdfTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task OpenAndRender_ValidPdf_ReadsMetadataAndRendersFrozenBitmap()
    {
        string pdfPath = CreateSamplePdf("sample.pdf");

        var info = await _pdfService.OpenDocumentAsync(pdfPath);

        Assert.Equal(2, info.PageCount);
        Assert.Equal("sample.pdf", info.FileName);
        Assert.Equal("StudySuit Test PDF", info.Title);
        Assert.Equal("Test Author", info.Author);
        Assert.Equal(2, info.PageSizes.Count);

        var normalBitmap = await _pdfService.RenderPageAsync(pdfPath, 1, dpi: 96, rotationDegrees: 0, invertColors: false);
        Assert.True(normalBitmap.IsFrozen);
        Assert.True(normalBitmap.PixelWidth > 0);
        Assert.True(normalBitmap.PixelHeight > 0);

        var rotatedBitmap = await _pdfService.RenderPageAsync(pdfPath, 1, dpi: 96, rotationDegrees: 90, invertColors: false);
        Assert.True(rotatedBitmap.PixelWidth > rotatedBitmap.PixelHeight);
    }

    [Fact]
    public async Task RenderPage_NightReadingMode_InvertsBackgroundToDark()
    {
        string pdfPath = CreateSamplePdf("night.pdf");

        var light = await _pdfService.RenderPageAsync(pdfPath, 1, dpi: 72, rotationDegrees: 0, invertColors: false);
        var dark = await _pdfService.RenderPageAsync(pdfPath, 1, dpi: 72, rotationDegrees: 0, invertColors: true);

        byte[] lightPixel = new byte[4];
        byte[] darkPixel = new byte[4];
        light.CopyPixels(new System.Windows.Int32Rect(5, 5, 1, 1), lightPixel, 4, 0);
        dark.CopyPixels(new System.Windows.Int32Rect(5, 5, 1, 1), darkPixel, 4, 0);

        Assert.True(lightPixel[0] > 200 && lightPixel[1] > 200 && lightPixel[2] > 200);
        Assert.True(darkPixel[0] < 60 && darkPixel[1] < 60 && darkPixel[2] < 60);
    }

    [Fact]
    public async Task SearchAsync_MultiWordAndCaseSensitivity_ReturnsAccurateMatches()
    {
        string pdfPath = CreateSamplePdf("search.pdf");

        var insensitive = await _pdfService.SearchAsync(pdfPath, "modern fluent", caseSensitive: false);
        Assert.Single(insensitive);
        Assert.Equal(1, insensitive[0].PageNumber);
        Assert.True(insensitive[0].Rects.Count >= 2);

        var sensitiveMiss = await _pdfService.SearchAsync(pdfPath, "modern fluent", caseSensitive: true);
        Assert.Empty(sensitiveMiss);

        var pageTwoHit = await _pdfService.SearchAsync(pdfPath, "Chapter Two", caseSensitive: true);
        Assert.Single(pageTwoHit);
        Assert.Equal(2, pageTwoHit[0].PageNumber);
    }

    [Fact]
    public async Task ExportPageAsPngAsync_CreatesNonEmptyPngFile()
    {
        string pdfPath = CreateSamplePdf("export.pdf");
        string pngPath = Path.Combine(_tempDir, "page1.png");

        await _pdfService.ExportPageAsPngAsync(pdfPath, 1, pngPath, dpi: 120, rotationDegrees: 0);

        Assert.True(File.Exists(pngPath));
        Assert.True(new FileInfo(pngPath).Length > 100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void NormalizedRect_RotateAndUnrotate_RoundTrips(int degrees)
    {
        var original = new NormalizedRect(0.15, 0.25, 0.30, 0.12);
        var rotated = original.Rotate(degrees);
        var restored = rotated.Unrotate(degrees);

        Assert.InRange(restored.X, original.X - 1e-6, original.X + 1e-6);
        Assert.InRange(restored.Y, original.Y - 1e-6, original.Y + 1e-6);
        Assert.InRange(restored.Width, original.Width - 1e-6, original.Width + 1e-6);
        Assert.InRange(restored.Height, original.Height - 1e-6, original.Height + 1e-6);
    }

    [Fact]
    public void RecentFilesService_PersistsAndUpdatesLastPageAndCapsList()
    {
        string statePath = Path.Combine(_tempDir, "state.json");
        var service = new RecentFilesService(statePath);

        var state = service.Load();
        Assert.True(state.IsDarkTheme);
        Assert.Empty(state.RecentFiles);

        service.RecordOpenedFile(state, @"C:\Docs\alpha.pdf", pageCount: 20, lastPage: 3);
        service.UpdateLastPage(state, @"C:\Docs\alpha.pdf", 14);

        var reloaded = service.Load();
        Assert.Single(reloaded.RecentFiles);
        Assert.Equal(14, reloaded.RecentFiles[0].LastPage);

        service.RemoveFile(reloaded, @"C:\Docs\alpha.pdf");
        Assert.Empty(service.Load().RecentFiles);
    }

    [Fact]
    public async Task DocumentTabViewModel_ExtractsSelectedTextAndNavigates()
    {
        string pdfPath = CreateSamplePdf("vm.pdf");
        var info = await _pdfService.OpenDocumentAsync(pdfPath);

        using var tab = new DocumentTabViewModel(info, _pdfService);
        Assert.Equal(1, tab.CurrentPageNumber);

        tab.NextPage();
        Assert.Equal(2, tab.CurrentPageNumber);

        tab.GoToPage(999);
        Assert.Equal(2, tab.CurrentPageNumber);

        tab.RotateRight();
        Assert.Equal(90, tab.RotationDegrees);
        tab.RotateLeft();
        Assert.Equal(0, tab.RotationDegrees);

        // Full-page selection on page 1 should capture page 1 words.
        string extracted = await tab.ExtractTextInSelectionAsync(1, new NormalizedRect(0, 0, 1, 1));
        Assert.Contains("Modern", extracted);
        Assert.Contains("Fluent", extracted);
    }

    [Fact]
    public async Task Annotations_CreateUndoRedoPersistAndExportMarkdown()
    {
        string pdfPath = CreateSamplePdf("annotated.pdf");
        string annDir = Path.Combine(_tempDir, "ann_store");
        var annotationService = new AnnotationService(annDir);
        var info = await _pdfService.OpenDocumentAsync(pdfPath);

        using var tab = new DocumentTabViewModel(info, _pdfService, annotationService: annotationService);

        // 1. Highlight snaps to text words and merges same-line rects
        var highlight = await tab.AddRegionAnnotationAsync(1, new NormalizedRect(0, 0, 1, 1), AnnotationType.Highlight);
        Assert.NotNull(highlight);
        Assert.Contains("Modern", highlight.Text);
        Assert.Single(tab.Annotations);
        Assert.Single(tab.Pages[0].VectorAnnotations);

        // 2. Add underline, ink, arrow, sticky note, and text box
        await tab.AddRegionAnnotationAsync(1, new NormalizedRect(0.1, 0.1, 0.3, 0.05), AnnotationType.Underline);
        tab.AddInkAnnotation(1, [new NormalizedPoint(0.1, 0.2), new NormalizedPoint(0.3, 0.3)]);
        tab.AddArrowAnnotation(1, new NormalizedPoint(0.2, 0.4), new NormalizedPoint(0.5, 0.5));
        var note = tab.AddStickyNote(2, new NormalizedPoint(0.3, 0.3), "Review chapter two");
        tab.AddTextBox(2, new NormalizedPoint(0.4, 0.4), "Key formula");

        Assert.Equal(6, tab.Annotations.Count);
        Assert.Equal(4, tab.Pages[0].VectorAnnotations.Count);
        Assert.Equal(2, tab.Pages[1].PinAnnotations.Count);

        // 3. Edit note, Undo, Redo
        tab.UpdateAnnotationText(note.Id, "Updated review note");
        Assert.Equal("Updated review note", tab.GetAnnotationById(note.Id)?.NoteComment);

        Assert.True(tab.CanUndo);
        tab.Undo();
        Assert.Equal("Review chapter two", tab.GetAnnotationById(note.Id)?.NoteComment);
        Assert.True(tab.CanRedo);
        tab.Redo();
        Assert.Equal("Updated review note", tab.GetAnnotationById(note.Id)?.NoteComment);

        // 4. Filter annotations & export Markdown
        tab.AnnotationFilterText = "Updated";
        Assert.Single(tab.FilteredAnnotations);
        tab.AnnotationFilterText = string.Empty;
        Assert.Equal(6, tab.FilteredAnnotations.Count);

        string md = tab.ExportAnnotationsAsMarkdown();
        Assert.Contains("# Study Notes:", md);
        Assert.Contains("Updated review note", md);
        Assert.Contains("Key formula", md);

        // 5. Persisted across reload
        var reloaded = annotationService.LoadAnnotations(pdfPath);
        Assert.Equal(6, reloaded.Count);
        Assert.Equal(6, annotationService.GetTotalAnnotationCount());
        Assert.NotEmpty(annotationService.GetRecentAnnotations());

        // 6. Clear page 1 annotations
        tab.GoToPage(1);
        tab.ClearCurrentPageAnnotations();
        Assert.Equal(2, tab.Annotations.Count);
        Assert.Empty(tab.Pages[0].VectorAnnotations);
    }

    [Fact]
    public async Task MainViewModel_DefaultHomeDashboard_TracksPinsFilterAndNavigation()
    {
        string pdfPath = CreateSamplePdf("dashboard.pdf");
        string statePath = Path.Combine(_tempDir, "dash_state.json");
        string annDir = Path.Combine(_tempDir, "dash_ann");
        var recentService = new RecentFilesService(statePath);
        var annotationService = new AnnotationService(annDir);

        using var mainVm = new MainViewModel(_pdfService, recentService, annotationService);

        // Default startup shows Home Dashboard
        Assert.True(mainVm.IsHomeActive);
        Assert.True(mainVm.ShowHomeDashboard);
        Assert.False(mainVm.ShowDocumentReader);

        await mainVm.OpenFileAsync(pdfPath);
        Assert.False(mainVm.IsHomeActive);
        Assert.True(mainVm.ShowDocumentReader);
        Assert.Single(mainVm.Tabs);
        Assert.Equal(1, mainVm.TotalDocumentsCount);

        // Add an annotation and verify dashboard reflects it
        mainVm.SelectedTab!.AddStickyNote(1, new NormalizedPoint(0.2, 0.2), "Important note");
        mainVm.ShowHome();

        Assert.True(mainVm.IsHomeActive);
        Assert.True(mainVm.ShowHomeDashboard);
        Assert.Equal(1, mainVm.TotalAnnotationsCount);
        Assert.Single(mainVm.RecentAnnotations);

        // Pin recent file & filter
        var item = Assert.Single(mainVm.RecentFiles);
        mainVm.TogglePinRecentFile(item);
        Assert.Equal(1, mainVm.PinnedDocumentsCount);

        mainVm.DashboardSearchText = "nonexistent";
        Assert.Empty(mainVm.FilteredRecentFiles);
        mainVm.DashboardSearchText = "dashboard";
        Assert.Single(mainVm.FilteredRecentFiles);

        // Search dialogue stays closed until explicitly requested
        Assert.False(mainVm.IsSearchBarVisible);
        mainVm.SelectTab(mainVm.Tabs[0]);
        Assert.False(mainVm.IsSearchBarVisible);
        mainVm.ToggleSearch();
        Assert.True(mainVm.IsSearchBarVisible);
    }

    [Fact]
    public async Task WordSelection_DragAcrossWords_HighlightsAndCopiesExactWordSpan()
    {
        string pdfPath = CreateSamplePdf("words.pdf");
        var info = await _pdfService.OpenDocumentAsync(pdfPath);

        using var tab = new DocumentTabViewModel(info, _pdfService);
        var words = await tab.GetOrLoadPageWordsAsync(1);
        Assert.True(words.Count >= 4);

        var firstWord = words[0].Bounds;
        var thirdWord = words[2].Bounds;
        var startPt = new NormalizedPoint(firstWord.X + firstWord.Width * 0.5, firstWord.Y + firstWord.Height * 0.5);
        var endPt = new NormalizedPoint(thirdWord.X + thirdWord.Width * 0.5, thirdWord.Y + thirdWord.Height * 0.5);

        await tab.UpdateLiveWordSelectionAsync(1, startPt, endPt, Brushes.Yellow);
        Assert.NotEmpty(tab.Pages[0].LiveWordSelectionRects);

        var highlight = await tab.CommitWordSelectionAnnotationAsync(1, startPt, endPt, AnnotationType.Highlight);
        Assert.NotNull(highlight);
        Assert.Equal("Modern Fluent PDF", highlight.Text);
        Assert.Empty(tab.Pages[0].LiveWordSelectionRects);

        string copied = await tab.CommitWordSelectionCopyAsync(1, startPt, endPt);
        Assert.Equal("Modern Fluent PDF", copied);
    }

    [Fact]
    public void StudyDashboard_StopwatchCalendarTodosAndReminders_WorkAndPersist()
    {
        string statePath = Path.Combine(_tempDir, "study_state.json");
        string annDir = Path.Combine(_tempDir, "study_ann");
        string plannerPath = Path.Combine(_tempDir, "study_planner.json");

        var recentService = new RecentFilesService(statePath);
        var annotationService = new AnnotationService(annDir);
        var plannerService = new StudyPlannerService(plannerPath);

        using var vm = new MainViewModel(_pdfService, recentService, annotationService, plannerService);

        // 1. To-Do List add, filter, toggle, clear
        vm.NewTodoTitle = "Finish linear algebra exercises";
        vm.NewTodoPriority = TodoPriority.High;
        vm.AddTodo();

        vm.NewTodoTitle = "Read Chapter 3";
        vm.AddTodo();

        Assert.Equal(2, vm.TotalTodoCount);
        Assert.Equal(2, vm.FilteredTodos.Count);

        vm.FilteredTodos[0].IsCompleted = true;
        vm.ToggleTodo(vm.FilteredTodos[0]);
        Assert.Equal(1, vm.CompletedTodoCount);

        vm.SetTodoFilter("Active");
        Assert.Single(vm.FilteredTodos);
        vm.SetTodoFilter("All");
        vm.ClearCompletedTodos();
        Assert.Equal(1, vm.TotalTodoCount);

        // 2. Calendar Events & Reminders
        DateTime targetDate = DateTime.Today;
        vm.SelectedCalendarDate = targetDate;
        vm.NewEventTitle = "Algorithms Exam";
        vm.NewEventTimeText = "14:00";
        vm.NewEventCategory = EventCategory.Exam;
        vm.NewEventReminderMinutes = 15;
        vm.AddStudyEvent();

        Assert.Single(vm.SelectedDateEvents);
        var ev = vm.SelectedDateEvents[0];

        // Before reminder window -> no due reminders
        vm.CheckDueReminders(targetDate.AddHours(13).AddMinutes(30), DateTime.UtcNow);
        Assert.False(vm.HasActiveReminders);

        // Inside 15-minute reminder window -> reminder triggers!
        vm.CheckDueReminders(targetDate.AddHours(13).AddMinutes(50), DateTime.UtcNow);
        Assert.True(vm.HasActiveReminders);
        Assert.Single(vm.ActiveReminders);

        // Snooze hides it for 5m, Dismiss removes it permanently
        vm.SnoozeReminder(ev);
        Assert.False(vm.HasActiveReminders);

        vm.CheckDueReminders(targetDate.AddHours(13).AddMinutes(56), DateTime.UtcNow.AddMinutes(6));
        Assert.True(vm.HasActiveReminders);

        vm.DismissReminder(ev);
        Assert.False(vm.HasActiveReminders);

        // 3. Stopwatch persistence
        vm.ToggleStopwatch();
        Assert.True(vm.IsStopwatchRunning);
        vm.ResetStopwatch();
        Assert.False(vm.IsStopwatchRunning);

        // Log a manual session via planner state reload check
        var reloadedPlanner = plannerService.Load();
        Assert.Single(reloadedPlanner.Todos);
        Assert.Single(reloadedPlanner.Events);
        Assert.True(reloadedPlanner.Events[0].ReminderDismissed);
    }

    [Fact]
    public async Task WordBrushPainting_PaintsTouchedWordsAndFallsBackToFreehandBrush()
    {
        string pdfPath = CreateSamplePdf("brush.pdf");
        var info = await _pdfService.OpenDocumentAsync(pdfPath);

        using var tab = new DocumentTabViewModel(info, _pdfService);
        var words = await tab.GetOrLoadPageWordsAsync(1);
        Assert.True(words.Count >= 3);

        var paintedIndices = new HashSet<int>();
        var w0 = words[0].Bounds;
        var w1 = words[1].Bounds;
        var p0 = new NormalizedPoint(w0.X + w0.Width * 0.5, w0.Y + w0.Height * 0.5);
        var p1 = new NormalizedPoint(w1.X + w1.Width * 0.5, w1.Y + w1.Height * 0.5);

        bool hit = await tab.PaintBrushStepAsync(1, p0, p1, paintedIndices, Brushes.Yellow);
        Assert.True(hit);
        Assert.Contains(0, paintedIndices);
        Assert.Contains(1, paintedIndices);
        Assert.NotEmpty(tab.Pages[0].LiveWordSelectionRects);

        var wordHighlight = await tab.CommitWordBrushAnnotationAsync(1, paintedIndices, [p0, p1], AnnotationType.Highlight);
        Assert.NotNull(wordHighlight);
        Assert.Equal(AnnotationType.Highlight, wordHighlight.Type);
        Assert.Contains("Modern", wordHighlight.Text);

        // Painting over an empty margin with no words creates a freehand Brush stroke
        var emptyIndices = new HashSet<int>();
        var marginBrush = await tab.CommitWordBrushAnnotationAsync(
            1,
            emptyIndices,
            [new NormalizedPoint(0.05, 0.90), new NormalizedPoint(0.25, 0.92)],
            AnnotationType.Highlight);

        Assert.NotNull(marginBrush);
        Assert.Equal(AnnotationType.Brush, marginBrush.Type);
        Assert.Equal(2, tab.Pages[0].VectorAnnotations.Count);
    }

    [Fact]
    public void NightLight_PersistsSettingsAndComputesOverlayOpacity()
    {
        string statePath = Path.Combine(_tempDir, "nl_state.json");
        var recentService = new RecentFilesService(statePath);

        using (var vm = new MainViewModel(_pdfService, recentService, new AnnotationService(Path.Combine(_tempDir, "nl_ann")), new StudyPlannerService(Path.Combine(_tempDir, "nl_plan.json")), new NotesService(Path.Combine(_tempDir, "nl_notes"))))
        {
            Assert.False(vm.IsNightLightEnabled);
            Assert.Equal(0.0, vm.NightLightOverlayOpacity);

            vm.IsNightLightEnabled = true;
            vm.NightLightLevel = 80.0;
            Assert.True(vm.NightLightOverlayOpacity > 0.3);
        }

        using var reloadedVm = new MainViewModel(_pdfService, recentService, new AnnotationService(Path.Combine(_tempDir, "nl_ann")), new StudyPlannerService(Path.Combine(_tempDir, "nl_plan.json")), new NotesService(Path.Combine(_tempDir, "nl_notes")));
        Assert.True(reloadedVm.IsNightLightEnabled);
        Assert.Equal(80.0, reloadedVm.NightLightLevel);
        Assert.True(reloadedVm.NightLightOverlayOpacity > 0.3);
    }

    [Fact]
    public void NotesStudio_EquationsChartsGraphsAndNotesPersistence_WorkCleanly()
    {
        // 1. Math expression evaluator for y = f(x) graphs
        Assert.InRange(MathExpressionEvaluator.Evaluate("sin(0) + 2^3", 0), 7.999, 8.001);
        Assert.InRange(MathExpressionEvaluator.Evaluate("2x^2 + 3x - 1", 2), 12.999, 13.001);
        Assert.True(double.IsNaN(MathExpressionEvaluator.Evaluate("1 / x", 0)));

        // 2. Math equation renderer produces frozen high-DPI bitmap
        var eqBitmap = MathEquationRenderer.RenderEquation(@"\frac{-b \pm \sqrt{b^2 - 4ac}}{2a}");
        Assert.True(eqBitmap.IsFrozen);
        Assert.True(eqBitmap.PixelWidth > 40);
        Assert.True(eqBitmap.PixelHeight > 30);

        // 3. Chart & Graph renderer supports Bar, Line, Pie, Scatter, and FunctionPlot
        var samplePoints = new List<ChartDataPoint>
        {
            new() { Label = "Mon", XValue = 1, Value = 3 },
            new() { Label = "Tue", XValue = 2, Value = 5 },
            new() { Label = "Wed", XValue = 3, Value = 4 }
        };

        foreach (var chartType in new[] { StudyChartType.Bar, StudyChartType.Line, StudyChartType.Pie, StudyChartType.Scatter })
        {
            var bmp = ChartRenderer.RenderDataChart(chartType, "Test", "X", "Y", samplePoints);
            Assert.True(bmp.IsFrozen);
            Assert.True(bmp.PixelWidth > 200);
        }

        var fnGraph = ChartRenderer.RenderFunctionGraph("sin(x)", "Sine Wave", -6, 6, -2, 2);
        Assert.True(fnGraph.IsFrozen);
        Assert.True(fnGraph.PixelWidth > 200);

        // 4. Notes service & NotesViewModel + handwriting fonts + MainViewModel tab switching
        var notesDir = Path.Combine(_tempDir, "notes_store");
        var notesService = new NotesService(notesDir);
        using var mainVm = new MainViewModel(
            _pdfService,
            new RecentFilesService(Path.Combine(_tempDir, "n_state.json")),
            new AnnotationService(Path.Combine(_tempDir, "n_ann")),
            new StudyPlannerService(Path.Combine(_tempDir, "n_plan.json")),
            notesService);

        mainVm.ShowNotes();
        Assert.True(mainVm.ShowNotesStudio);
        Assert.False(mainVm.ShowHomeDashboard);

        Assert.NotEmpty(NotesViewModel.FontOptions);
        mainVm.Notes.CreateNewNote();
        var activeNote = mainVm.Notes.SelectedNote!;
        mainVm.Notes.SaveNoteContent(activeNote, [1, 2, 3, 4], "Sample physics derivation");

        byte[]? loadedPkg = mainVm.Notes.LoadNotePackage(activeNote.Id);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, loadedPkg);
    }

    [Fact]
    public void AiSettingsService_ProtectsSecretsAtRestAndRoundTrips()
    {
        string settingsPath = Path.Combine(_tempDir, "ai_settings.json");
        var service = new AiSettingsService(settingsPath);

        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.OpenAi,
            BaseUrl = "https://api.openai.com/v1",
            ModelName = "gpt-4o-mini",
            ApiKey = "sk-test-secret-12345",
            WebSearchProvider = WebSearchProviderKind.Tavily,
            WebSearchApiKey = "tvly-secret-999",
            UseInternetByDefault = true,
            MaxWebResults = 5
        };

        service.Save(settings);

        string rawJson = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("sk-test-secret-12345", rawJson);
        Assert.DoesNotContain("tvly-secret-999", rawJson);
        Assert.Contains("dpapi:", rawJson);

        var loaded = service.Load();
        Assert.Equal(AiProviderKind.OpenAi, loaded.Provider);
        Assert.Equal("sk-test-secret-12345", loaded.ApiKey);
        Assert.Equal("tvly-secret-999", loaded.WebSearchApiKey);
        Assert.Equal(5, loaded.MaxWebResults);
    }

    [Fact]
    public async Task AiChatService_SupportsLocalOllamaOpenAiGeminiAndAnthropicProtocols()
    {
        string? lastUrl = null;
        string? lastAuth = null;
        string? lastApiKeyHeader = null;

        var handler = new StubHttpMessageHandler(req =>
        {
            lastUrl = req.RequestUri!.ToString();
            lastAuth = req.Headers.Authorization?.ToString();
            lastApiKeyHeader = req.Headers.TryGetValues("x-api-key", out var ak) ? ak.FirstOrDefault() :
                               req.Headers.TryGetValues("x-goog-api-key", out var gk) ? gk.FirstOrDefault() : null;

            if (lastUrl.EndsWith("/api/tags"))
            {
                return JsonResp("""{"models":[{"name":"llama3.2"},{"name":"qwen2.5:7b"}]}""");
            }
            if (lastUrl.EndsWith("/api/chat"))
            {
                return JsonResp("""{"message":{"role":"assistant","content":"Ollama reply"}}""");
            }
            if (lastUrl.EndsWith("/v1/models"))
            {
                return JsonResp("""{"data":[{"id":"gpt-4o-mini"},{"id":"local-gguf"}]}""");
            }
            if (lastUrl.EndsWith("/v1/chat/completions"))
            {
                return JsonResp("""{"choices":[{"message":{"content":"OpenAI compatible reply"}}]}""");
            }
            if (lastUrl.Contains(":generateContent"))
            {
                return JsonResp("""{"candidates":[{"content":{"parts":[{"text":"Gemini reply"}]}}]}""");
            }
            if (lastUrl.EndsWith("/v1/messages"))
            {
                return JsonResp("""{"content":[{"type":"text","text":"Anthropic reply"}]}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var chatService = new AiChatService(new HttpClient(handler));

        // 1. Local Ollama model discovery + chat
        var ollama = new AiAssistantSettings { Provider = AiProviderKind.OllamaLocal, BaseUrl = "http://localhost:11434", ModelName = "llama3.2" };
        var ollamaModels = await chatService.FetchAvailableModelsAsync(ollama);
        Assert.Contains("llama3.2", ollamaModels);
        Assert.Contains("qwen2.5:7b", ollamaModels);
        Assert.Equal("Ollama reply", await chatService.SendChatAsync(ollama, [], "Hi", null, []));

        // 2. OpenAI / LM Studio local compatible
        var openAi = new AiAssistantSettings { Provider = AiProviderKind.OpenAi, BaseUrl = "https://api.openai.com/v1", ModelName = "gpt-4o-mini", ApiKey = "sk-abc" };
        var openAiModels = await chatService.FetchAvailableModelsAsync(openAi);
        Assert.Contains("gpt-4o-mini", openAiModels);
        Assert.Equal("OpenAI compatible reply", await chatService.SendChatAsync(openAi, [], "Hi", null, []));
        Assert.Equal("Bearer sk-abc", lastAuth);

        // 3. Gemini
        var gemini = new AiAssistantSettings { Provider = AiProviderKind.Gemini, BaseUrl = "https://generativelanguage.googleapis.com/v1beta", ModelName = "gemini-2.5-flash", ApiKey = "gem-key" };
        Assert.Equal("Gemini reply", await chatService.SendChatAsync(gemini, [], "Hi", null, []));
        Assert.Equal("gem-key", lastApiKeyHeader);

        // 4. Anthropic
        var claude = new AiAssistantSettings { Provider = AiProviderKind.Anthropic, BaseUrl = "https://api.anthropic.com/v1", ModelName = "claude-3-5-sonnet-latest", ApiKey = "ant-key" };
        Assert.Equal("Anthropic reply", await chatService.SendChatAsync(claude, [], "Hi", null, []));
        Assert.Equal("ant-key", lastApiKeyHeader);

        // 5. Blank BaseUrl falls back to default URL for Ollama, LM Studio, and llama.cpp
        var ollamaBlankUrl = new AiAssistantSettings { Provider = AiProviderKind.OllamaLocal, BaseUrl = "", ModelName = "qwen3.5:latest" };
        Assert.Equal("Ollama reply", await chatService.SendChatAsync(ollamaBlankUrl, [], "Hi", null, []));
        Assert.Equal("http://localhost:11434/api/chat", lastUrl);

        var lmStudioBlankUrl = new AiAssistantSettings { Provider = AiProviderKind.LmStudioLocal, BaseUrl = "", ModelName = "local-model" };
        Assert.Equal("OpenAI compatible reply", await chatService.SendChatAsync(lmStudioBlankUrl, [], "Hi", null, []));
        Assert.Equal("http://localhost:1234/v1/chat/completions", lastUrl);

        var llamaCppBlankUrl = new AiAssistantSettings { Provider = AiProviderKind.LlamaCppLocal, BaseUrl = "", ModelName = "local-model" };
        Assert.Equal("OpenAI compatible reply", await chatService.SendChatAsync(llamaCppBlankUrl, [], "Hi", null, []));
        Assert.Equal("http://localhost:9931/v1/chat/completions", lastUrl);

        Assert.Equal("http://localhost:9931/v1", AiChatService.NormalizeBaseUrl("http://localhost:9931", AiProviderKind.LlamaCppLocal));
        Assert.Equal("http://localhost:11434", AiChatService.NormalizeBaseUrl("http://localhost:11434/v1", AiProviderKind.OllamaLocal));
    }

    [Fact]
    public async Task WebResearchAndAiAssistantViewModel_GroundsAnswersInPdfAndWebSources()
    {
        const string sampleDdgHtml = """
            <div class="result">
                <a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fen.wikipedia.org%2Fwiki%2FFourier_transform">Fourier transform - Wikipedia</a>
                <a class="result__snippet">A mathematical transform that decomposes functions into frequency components.</a>
            </div>
            """;

        var parsed = WebResearchService.ParseDuckDuckGoHtml(sampleDdgHtml, 4);
        Assert.Single(parsed);
        Assert.Equal("https://en.wikipedia.org/wiki/Fourier_transform", parsed[0].Url);
        Assert.Equal("Fourier transform - Wikipedia", parsed[0].Title);

        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "vm_ai.json"));
        int turn = 0;
        var stubHandler = new StubHttpMessageHandler(_ =>
        {
            turn++;
            if (turn == 1)
            {
                return JsonResp("""
                    {"message":{"role":"assistant","content":"","tool_calls":[
                      {"function":{"name":"pdf_search","arguments":{"query":"Fourier"}}},
                      {"function":{"name":"web_search","arguments":{"query":"Fourier transform overview"}}}
                    ]}}
                    """);
            }

            return JsonResp("""{"message":{"role":"assistant","content":"Grounded answer about Fourier transforms [1]."}}""");
        });
        var chatService = new AiChatService(new HttpClient(stubHandler));
        var webService = new StubWebResearchService(parsed);
        int pdfSearchCalls = 0;

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist1")))
        {
            UsePdfContext = true,
            UseWebResearch = true,
            HasOpenPdfProvider = () => true,
            GetActivePdfContextAsync = () => Task.FromResult<string?>("File: signals.pdf\nPage 3: Fourier analysis notes."),
            SearchPdfToolAsync = (q, _) =>
            {
                pdfSearchCalls++;
                return Task.FromResult($"Match for '{q}' on Page 3: Fourier analysis notes.");
            }
        };

        string? sentToNote = null;
        aiVm.InsertIntoNotesRequested += text => sentToNote = text;

        aiVm.PromptText = "Search the PDF and explain Fourier transform with latest web sources";
        await aiVm.SendMessageAsync();

        var assistantMsg = aiVm.Messages.Last();
        Assert.False(assistantMsg.IsError);
        Assert.True(assistantMsg.UsedPdfContext);
        Assert.True(assistantMsg.UsedWebResearch);
        Assert.Equal(1, pdfSearchCalls);
        Assert.Equal(1, webService.CallCount);
        Assert.Single(assistantMsg.Sources);
        Assert.Contains("Grounded answer", assistantMsg.Content);

        aiVm.SendToNotes(assistantMsg);
        Assert.Equal(assistantMsg.Content, sentToNote);
    }

    [Fact]
    public async Task AiAssistantViewModel_CasualGreeting_DoesNotInvokeWebOrPdfTools()
    {
        Assert.True(AiChatService.IsCasualMessage("hey"));
        Assert.True(AiChatService.IsCasualMessage("Hello!"));
        Assert.False(AiChatService.IsCasualMessage("What is photosynthesis according to my PDF?"));

        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "vm_casual.json"));
        string? capturedBody = null;
        var stubHandler = new StubHttpMessageHandler(async req =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return JsonResp("""{"message":{"role":"assistant","content":"Hey! How can I help with your studies today?"}}""");
        });
        var chatService = new AiChatService(new HttpClient(stubHandler));
        var webService = new StubWebResearchService([
            new WebSourceReference
            {
                Index = 1,
                Title = "Should Not Run",
                Url = "https://example.com",
                Snippet = "unused"
            }
        ]);
        int pdfSearchCalls = 0;

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist1")))
        {
            UsePdfContext = true,
            UseWebResearch = true,
            HasOpenPdfProvider = () => true,
            GetActivePdfContextAsync = () => Task.FromResult<string?>("File: secret.pdf\nPage 1: Sensitive notes."),
            SearchPdfToolAsync = (_, _) =>
            {
                pdfSearchCalls++;
                return Task.FromResult("unused");
            }
        };

        aiVm.PromptText = "hey";
        await aiVm.SendMessageAsync();

        var assistantMsg = aiVm.Messages.Last();
        Assert.False(assistantMsg.IsError);
        Assert.False(assistantMsg.UsedWebResearch);
        Assert.False(assistantMsg.UsedPdfContext);
        Assert.Empty(assistantMsg.Sources);
        Assert.Equal(0, webService.CallCount);
        Assert.Equal(0, pdfSearchCalls);
        Assert.DoesNotContain("\"tools\"", capturedBody ?? string.Empty);
    }

    [Fact]
    public void MarkdownFlowRenderer_RendersComplexMarkdownEquationsAndCharts()
    {
        var withSpec = new AiAssistantSettings
        {
            SystemPrompt = "Be a helpful tutor.",
            IncludeResponseFormatSpec = true,
            ResponseFormatPrompt = AiAssistantSettings.DefaultResponseFormatPrompt
        };
        string combined = AiChatService.BuildEffectiveSystemPrompt(withSpec);
        Assert.Contains("Be a helpful tutor.", combined);
        Assert.Contains("RESPONSE FORMAT & ADVANCED UI SPECIFICATION", combined);

        var withoutSpec = withSpec.Clone();
        withoutSpec.IncludeResponseFormatSpec = false;
        string personalityOnly = AiChatService.BuildEffectiveSystemPrompt(withoutSpec);
        Assert.Contains("Be a helpful tutor.", personalityOnly);
        Assert.DoesNotContain("RESPONSE FORMAT & ADVANCED UI SPECIFICATION", personalityOnly);

        const string markdown = """
            # Study Summary

            Here is **bold**, *italic*, `inlineCode()`, and [OpenAI](https://openai.com).

            > [!TIP] Key insight about **soil distribution** across India.

            ```metrics
            [{"label":"Alluvial Share","value":"~43%","note":"Northern Plains"}]
            ```

            ```cards
            {"title":"Regions","columns":2,"cards":[{"title":"Northern Plains","badge":"Fertile","color":"#22C55E","items":["Alluvial Soils"]},{"title":"Deccan Plateau","badge":"Volcanic","color":"#8B5CF6","items":["Black Soils"]}]}
            ```

            ```diagram
            {"title":"Soil Cycle","direction":"horizontal","steps":[{"label":"Weathering"},{"label":"Transport"},{"label":"Deposition"}]}
            ```

            | Term | Value |
            | --- | --- |
            | Velocity | $v = \frac{dx}{dt}$ |
            | Energy | $E = mc^2$ |

            $$
            \int_0^\infty e^{-x^2}\,dx = \frac{\sqrt{\pi}}{2}
            $$

            ```chart
            {
              "type": "function",
              "title": "Sine Wave",
              "expression": "sin(x)",
              "xMin": -3.14,
              "xMax": 3.14
            }
            ```

            ```csharp
            var x = 42;
            ```
            """;

        Exception? threadEx = null;
        int blockCount = 0;

        var sta = new Thread(() =>
        {
            try
            {
                var doc = Services.MarkdownFlowRenderer.RenderToFlowDocument(markdown, isDarkTheme: true);
                blockCount = doc.Blocks.Count;
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.True(blockCount >= 10, $"Expected >= 10 FlowDocument blocks, got {blockCount}.");
    }

    [Fact]
    public async Task AiChatService_SlowChunkedOllamaStream_DoesNotBlockCallingThread()
    {
        var chunk2Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowStream = new SlowChunkedStream(
            "{\"message\":{\"role\":\"assistant\",\"content\":\"Hello \"}}\n",
            "{\"message\":{\"role\":\"assistant\",\"content\":\"world!\"},\"done\":true}\n",
            chunk2Gate.Task);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(slowStream)
        });

        var chatService = new AiChatService(new HttpClient(handler));
        var settings = new AiAssistantSettings { Provider = AiProviderKind.OllamaLocal, BaseUrl = "http://localhost:11434", ModelName = "qwen3.5:latest" };

        var deltas = new List<string>();
        Task<string> chatTask = chatService.SendChatWithToolsAsync(
            settings,
            [],
            "hey",
            null,
            toolHost: null,
            onStreamDelta: deltas.Add);

        // Wait briefly for chunk 1 to arrive, verify task yields cleanly while waiting for chunk 2 (no EndOfStream block)
        await Task.Delay(80);
        Assert.False(chatTask.IsCompleted);

        chunk2Gate.SetResult(true);
        string full = await chatTask;
        Assert.Equal("Hello world!", full);
        Assert.Equal(2, deltas.Count);
    }

    [Fact]
    public async Task AiChatService_OpenAiStreaming_StreamsDeltasRealtime()
    {
        string? requestedUrl = null;
        string? capturedBody = null;
        string ssePayload =
            "data: {\"choices\":[{\"delta\":{\"content\":\"OpenAI \"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"streaming \"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"works!\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var handler = new StubHttpMessageHandler(async req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
        });

        var chatService = new AiChatService(new HttpClient(handler));
        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.OpenAi,
            BaseUrl = "https://api.openai.com/v1",
            ModelName = "gpt-4o",
            ApiKey = "sk-test"
        };

        var deltas = new List<string>();
        string result = await chatService.SendChatWithToolsAsync(
            settings,
            [],
            "Hello",
            pdfContext: null,
            toolHost: null,
            onStreamDelta: deltas.Add);

        Assert.Equal("https://api.openai.com/v1/chat/completions", requestedUrl);
        Assert.Contains("\"stream\":true", capturedBody);
        Assert.Equal(3, deltas.Count);
        Assert.Equal("OpenAI ", deltas[0]);
        Assert.Equal("streaming ", deltas[1]);
        Assert.Equal("works!", deltas[2]);
        Assert.Equal("OpenAI streaming works!", result);
    }

    [Fact]
    public async Task AiChatService_GeminiStreaming_StreamsDeltasRealtime()
    {
        string? requestedUrl = null;
        string ssePayload =
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Gemini \"}]}}]}\n\n" +
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"streaming \"}]}}]}\n\n" +
            "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"rocks!\"}]}}]}\n\n";

        var handler = new StubHttpMessageHandler(req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
        });

        var chatService = new AiChatService(new HttpClient(handler));
        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.Gemini,
            BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
            ModelName = "gemini-2.5-flash",
            ApiKey = "gem-test-key"
        };

        var deltas = new List<string>();
        string result = await chatService.SendChatWithToolsAsync(
            settings,
            [],
            "Hello Gemini",
            pdfContext: null,
            toolHost: null,
            onStreamDelta: deltas.Add);

        Assert.Contains(":streamGenerateContent?alt=sse", requestedUrl);
        Assert.Equal(3, deltas.Count);
        Assert.Equal("Gemini ", deltas[0]);
        Assert.Equal("streaming ", deltas[1]);
        Assert.Equal("rocks!", deltas[2]);
        Assert.Equal("Gemini streaming rocks!", result);
    }

    [Fact]
    public async Task AiChatService_AnthropicStreaming_StreamsDeltasRealtime()
    {
        string? requestedUrl = null;
        string? capturedBody = null;
        string ssePayload =
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Claude \"}}\n\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"streaming \"}}\n\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"online!\"}}\n\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n\n";

        var handler = new StubHttpMessageHandler(async req =>
        {
            requestedUrl = req.RequestUri!.ToString();
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            };
        });

        var chatService = new AiChatService(new HttpClient(handler));
        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.Anthropic,
            BaseUrl = "https://api.anthropic.com/v1",
            ModelName = "claude-3-7-sonnet",
            ApiKey = "ant-test-key"
        };

        var deltas = new List<string>();
        string result = await chatService.SendChatWithToolsAsync(
            settings,
            [],
            "Hello Claude",
            pdfContext: null,
            toolHost: null,
            onStreamDelta: deltas.Add);

        Assert.Equal("https://api.anthropic.com/v1/messages", requestedUrl);
        Assert.Contains("\"stream\":true", capturedBody);
        Assert.Equal(3, deltas.Count);
        Assert.Equal("Claude ", deltas[0]);
        Assert.Equal("streaming ", deltas[1]);
        Assert.Equal("online!", deltas[2]);
        Assert.Equal("Claude streaming online!", result);
    }

    [Fact]
    public async Task AiChatService_OpenAiStreaming_WithToolCalling_StreamsSynthesizedAnswer()
    {
        int requestCount = 0;
        var handler = new StubHttpMessageHandler(req =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                // Round 0: tool invocation
                string toolCallSse =
                    "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_time_1\",\"type\":\"function\",\"function\":{\"name\":\"get_current_time\",\"arguments\":\"{}\"}}]}}]}\n\n" +
                    "data: [DONE]\n\n";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(toolCallSse, Encoding.UTF8, "text/event-stream")
                };
            }

            // Round 1: answer stream
            string answerSse =
                "data: {\"choices\":[{\"delta\":{\"content\":\"Today in 2026 is \"}}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"a great day to study.\"}}]}\n\n" +
                "data: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answerSse, Encoding.UTF8, "text/event-stream")
            };
        });

        var chatService = new AiChatService(new HttpClient(handler));
        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.OpenAi,
            BaseUrl = "https://api.openai.com/v1",
            ModelName = "gpt-4o",
            ApiKey = "sk-test"
        };

        var toolHost = new AiToolHost
        {
            AllowTemporalTools = true
        };

        var deltas = new List<string>();
        string result = await chatService.SendChatWithToolsAsync(
            settings,
            [],
            "What is the date today?",
            pdfContext: null,
            toolHost: toolHost,
            onStreamDelta: deltas.Add);

        Assert.Equal(2, requestCount);
        Assert.Equal(2, deltas.Count);
        Assert.Equal("Today in 2026 is ", deltas[0]);
        Assert.Equal("a great day to study.", deltas[1]);
        Assert.Equal("Today in 2026 is a great day to study.", result);
    }

    [Fact]
    public void AiAssistant_PersonalisationPresetsAndAnswerLengths_PersistAndShapeSystemPrompt()
    {
        var settingsPath = Path.Combine(_tempDir, "preset_ai.json");
        var settingsService = new AiSettingsService(settingsPath);
        var chatService = new AiChatService(new HttpClient(new StubHttpMessageHandler(_ => JsonResp("{}"))));
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist1")));
        Assert.Equal(AiPersonaPresetKind.General, aiVm.SelectedPersonaPreset);
        Assert.Equal(AiAnswerLength.Balanced, aiVm.SelectedAnswerLength);

        aiVm.SelectedPersonaPreset = AiPersonaPresetKind.Upsc;
        aiVm.SelectedAnswerLength = AiAnswerLength.Detailed;

        string upscPrompt = AiChatService.BuildEffectiveSystemPrompt(aiVm.Settings);
        Assert.Contains("UPSC", upscPrompt);
        Assert.Contains("ANSWER LENGTH RULE — DETAILED", upscPrompt);

        aiVm.SelectedPersonaPreset = AiPersonaPresetKind.Ssc;
        aiVm.SelectedAnswerLength = AiAnswerLength.Concise;

        var reloaded = settingsService.Load();
        Assert.Equal(AiPersonaPresetKind.Ssc, reloaded.PersonaPreset);
        Assert.Equal(AiAnswerLength.Concise, reloaded.AnswerLength);

        string sscPrompt = AiChatService.BuildEffectiveSystemPrompt(reloaded);
        Assert.Contains("SSC", sscPrompt);
        Assert.Contains("ANSWER LENGTH RULE — SHORT", sscPrompt);
    }

    [Fact]
    public async Task LlamaCpp_EnforcesAlternatingUserAssistantRoles_AndFallsBackWhenToolsRejected()
    {
        var messyHistory = new List<AiChatMessage>
        {
            new() { Role = "assistant", IsWelcome = true, Content = "Welcome!" },
            new() { Role = "assistant", Content = "Orphan assistant before first user" },
            new() { Role = "user", Content = "First q" },
            new() { Role = "assistant", IsError = true, Content = "Old error" },
            new() { Role = "user", Content = "Second q after error" },
            new() { Role = "assistant", Content = "Clean reply" }
        };

        var turns = AiChatService.NormalizeConversationTurns(messyHistory, "Final question");
        Assert.Equal(3, turns.Count);
        Assert.True(turns[0].IsUser);
        Assert.Contains("First q", turns[0].Content);
        Assert.Contains("Second q after error", turns[0].Content);
        Assert.False(turns[1].IsUser);
        Assert.Equal("Clean reply", turns[1].Content);
        Assert.True(turns[2].IsUser);
        Assert.Equal("Final question", turns[2].Content);

        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "llamacpp_ai.json"));
        int callNum = 0;
        var sentPayloads = new List<string>();
        var handler = new StubHttpMessageHandler(async req =>
        {
            callNum++;
            string body = await req.Content!.ReadAsStringAsync();
            sentPayloads.Add(body);

            if (callNum == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("""{"error":{"code":500,"message":"Error: Jinja Exception: tool call unsupported"}}""", Encoding.UTF8, "application/json")
                };
            }

            return JsonResp("""{"choices":[{"message":{"content":"Ministral reply works!"}}]}""");
        });

        using var aiVm = new AiAssistantViewModel(
            settingsService,
            new AiChatService(new HttpClient(handler)),
            new StubWebResearchService([]),
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_mini")))
        {
            UsePdfContext = false,
            UseWebResearch = true
        };
        aiVm.UpdateSettings(new AiAssistantSettings
        {
            Provider = AiProviderKind.LlamaCppLocal,
            BaseUrl = "http://localhost:9931/v1",
            ModelName = "ggml-org/Ministral-3-8B-Instruct-2512-GGUF:Q8_0",
            UseInternetByDefault = true
        });

        aiVm.PromptText = "Explain photosynthesis briefly";
        await aiVm.SendMessageAsync();

        var lastMsg = aiVm.Messages.Last();
        Assert.False(lastMsg.IsError);
        Assert.Equal("Ministral reply works!", lastMsg.Content);
        Assert.Equal(2, sentPayloads.Count);

        // Verify welcome assistant message was stripped so first non-system turn is strictly "user"
        using var parsedDoc = System.Text.Json.JsonDocument.Parse(sentPayloads[0]);
        var msgRoles = parsedDoc.RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("role").GetString())
            .ToList();
        Assert.Equal(["system", "user"], msgRoles);
    }

    [Fact]
    public async Task VoiceReadoutService_PreparesCleanHindiAndMarkdownSpeech_AndTogglesPerMessage()
    {
        const string markdown = """
            ## भारत की मिट्टियाँ

            **जलोढ़ मिट्टी** सबसे उपजाऊ है। देखें [स्रोत](https://example.com)।

            ```cards
            {"title":"क्षेत्र","cards":[{"title":"उत्तरी मैदान","items":["जलोढ़ मिट्टी"]}]}
            ```

            ```chart
            {"type":"bar","title":"Share","data":[{"label":"A","value":43}]}
            ```
            """;

        string clean = VoiceReadoutService.PrepareSpeechText(markdown);
        Assert.Contains("भारत की मिट्टियाँ", clean);
        Assert.Contains("जलोढ़ मिट्टी", clean);
        Assert.Contains("उत्तरी मैदान", clean);
        Assert.DoesNotContain("https://example.com", clean);
        Assert.DoesNotContain("\"type\"", clean);

        var chunks = VoiceReadoutService.SplitIntoSpeechChunks("यह पहला वाक्य है। यह दूसरा लंबा वाक्य अध्ययन के लिए है।", maxLen: 25);
        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Length <= 25));

        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "voice_ai.json"));
        var stubVoice = new StubVoiceReadoutService();
        using var aiVm = new AiAssistantViewModel(
            settingsService,
            new AiChatService(new HttpClient(new StubHttpMessageHandler(_ => JsonResp("{}")))),
            new StubWebResearchService([]),
            stubVoice,
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_voice")));

        Assert.Equal(VoiceReadoutMode.HindiDirect, aiVm.SelectedVoiceMode);
        aiVm.SelectedVoiceMode = VoiceReadoutMode.HindiTranslate;
        Assert.Equal(VoiceReadoutMode.HindiTranslate, settingsService.Load().VoiceMode);

        var msg = new AiChatMessage { Role = "assistant", Content = "नमस्ते छात्र!" };
        Task speakTask = aiVm.ToggleSpeakMessageCommand.ExecuteAsync(msg);
        await Task.Delay(30);
        Assert.True(msg.IsSpeaking);
        Assert.Equal(VoiceReadoutMode.HindiTranslate, stubVoice.LastMode);

        // Toggling again stops reading immediately
        await aiVm.ToggleSpeakMessageCommand.ExecuteAsync(msg);
        await speakTask;
        Assert.False(msg.IsSpeaking);
        Assert.True(stubVoice.StopCount >= 1);
    }

    [Fact]
    public async Task NewsFeedService_ParsesRssAndViewModelFiltersDailyWeeklyMonthly()
    {
        const string sampleRss = """
            <rss version="2.0">
              <channel>
                <title>Google News</title>
                <item>
                  <title>Cabinet Approves National Green Hydrogen Mission Updates - The Hindu</title>
                  <link>https://example.com/green-hydrogen</link>
                  <description>&lt;p&gt;India expands incentive schemes for electrolyser manufacturing.&lt;/p&gt;</description>
                  <source url="https://thehindu.com">The Hindu</source>
                  <pubDate>Thu, 12 Nov 2026 12:00:00 GMT</pubDate>
                </item>
                <item>
                  <title>RBI Keeps Repo Rate Steady Amid Growth Momentum - Livemint</title>
                  <link>https://example.com/rbi-rate</link>
                  <description>Monetary policy committee maintains focus on inflation targets.</description>
                  <source url="https://livemint.com">Livemint</source>
                  <pubDate>Thu, 12 Nov 2026 10:30:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var parsedDaily = NewsFeedService.ParseRssFeed(sampleRss, NewsTimeframe.Daily, "Economy & RBI");
        Assert.Equal(2, parsedDaily.Count);
        Assert.Equal("Cabinet Approves National Green Hydrogen Mission Updates", parsedDaily[0].Title);
        Assert.Equal("The Hindu", parsedDaily[0].Source);
        Assert.Equal("RBI Keeps Repo Rate Steady Amid Growth Momentum", parsedDaily[1].Title);

        var stubFeedService = new StubNewsFeedService(parsedDaily);
        var stubVoice = new StubVoiceReadoutService();
        using var feedVm = new NewsFeedViewModel(stubFeedService, stubVoice);

        await feedVm.LoadFeedAsync(forceRefresh: true);
        Assert.True(feedVm.HasItems);
        Assert.Equal(2, feedVm.FilteredItems.Count);

        feedVm.SearchQuery = "Hydrogen";
        Assert.Single(feedVm.FilteredItems);
        Assert.Equal("The Hindu", feedVm.FilteredItems[0].Source);

        feedVm.SearchQuery = string.Empty;
        var firstItem = feedVm.FilteredItems[0];
        Task readTask = feedVm.ReadArticleAloudCommand.ExecuteAsync(firstItem);
        await Task.Delay(30);
        Assert.True(firstItem.IsSpeaking);

        feedVm.StopArticleReadoutCommand.Execute(null);
        await readTask;
        Assert.False(firstItem.IsSpeaking);
        Assert.True(stubVoice.StopCount >= 1);
    }

    [Fact]
    public void NewsFeedService_ExtractsImagesAndArticleBodyCleanly()
    {
        const string sampleRssWithImages = """
            <rss xmlns:media="http://search.yahoo.com/mrss/" version="2.0">
              <channel>
                <title>Curated Feed</title>
                <item>
                  <title>ISRO Launches Advanced Earth Observation Satellite - The Hindu</title>
                  <link>https://example.com/isro-satellite</link>
                  <description>ISRO achieved successful orbital insertion.</description>
                  <media:content url="https://example.com/satellite.jpg" type="image/jpeg" />
                  <pubDate>Thu, 12 Nov 2026 12:00:00 GMT</pubDate>
                </item>
                <item>
                  <title>Cabinet Clears New Semiconductor Subsidy Scheme - NDTV</title>
                  <link>https://example.com/semi-scheme</link>
                  <description>&lt;img src="https://example.com/chip.png" /&gt;Scheme boost for local chip manufacturing units.</description>
                  <pubDate>Thu, 12 Nov 2026 11:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var parsed = NewsFeedService.ParseRssFeed(sampleRssWithImages, NewsTimeframe.Daily, "Science & Tech");
        Assert.Equal(2, parsed.Count);
        Assert.Equal("https://example.com/satellite.jpg", parsed[0].ImageUrl);
        Assert.True(parsed[0].HasImage);
        Assert.Equal("https://example.com/chip.png", parsed[1].ImageUrl);
        Assert.True(parsed[1].HasImage);

        // Fallback image test for empty image feed
        const string sampleRssNoImage = """
            <rss version="2.0">
              <channel>
                <item>
                  <title>Parliament Passes Landmark Data Governance Bill - PIB</title>
                  <link>https://example.com/parliament-bill</link>
                  <description>New bill sets framework for citizen digital rights.</description>
                  <pubDate>Thu, 12 Nov 2026 09:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;
        var parsedNoImage = NewsFeedService.ParseRssFeed(sampleRssNoImage, NewsTimeframe.Daily, "India & Polity");
        Assert.Single(parsedNoImage);
        Assert.True(parsedNoImage[0].HasImage);
        Assert.Contains("unsplash.com", parsedNoImage[0].ImageUrl);

        // Article body extraction test
        const string sampleHtml = """
            <!DOCTYPE html>
            <html>
              <body>
                <header><nav><a href="/">Home</a></nav></header>
                <article>
                  <h1>Satellite Mission Achieves Orbit</h1>
                  <p>Subscribe to our newsletter for daily updates.</p>
                  <p>The Indian Space Research Organisation successfully placed the new generation observation satellite into sun-synchronous polar orbit this morning.</p>
                  <p>Telemetry data confirmed that the solar panels deployed without anomalies and ground stations in Bengaluru have established full control of the satellite systems.</p>
                  <p>Click here to read more stories.</p>
                </article>
              </body>
            </html>
            """;

        string? extracted = NewsFeedService.ExtractArticleBody(sampleHtml);
        Assert.NotNull(extracted);
        Assert.Contains("Indian Space Research Organisation", extracted);
        Assert.Contains("Telemetry data confirmed", extracted);
        Assert.DoesNotContain("Subscribe to our newsletter", extracted);
        Assert.DoesNotContain("Click here to read", extracted);
    }

    [Fact]
    public async Task NewsFeedViewModel_InAppReader_OpensReadsAloudAndSupportsAiInsights()
    {
        var item = new NewsFeedItem
        {
            Title = "RBI Updates Sovereign Green Bond Framework",
            Summary = "Reserve Bank of India has updated the operational modalities for institutional investors.",
            Source = "RBI Bulletin",
            Url = "https://example.com/rbi-green-bonds",
            CategoryLabel = "Economy & RBI",
            ImageUrl = "https://example.com/rbi.jpg"
        };

        var stubFeed = new StubNewsFeedService([item], "The Reserve Bank of India announced updated guidelines today for green bond issuance.\n\nThe framework aligns with international sustainability standards.");
        var stubVoice = new StubVoiceReadoutService();
        using var feedVm = new NewsFeedViewModel(stubFeed, stubVoice);

        await feedVm.LoadFeedAsync(forceRefresh: true);
        Assert.False(feedVm.IsReaderOpen);
        Assert.Null(feedVm.SelectedArticle);

        // Open in-app reader
        await feedVm.OpenArticleCommand.ExecuteAsync(item);
        Assert.True(feedVm.IsReaderOpen);
        Assert.Same(item, feedVm.SelectedArticle);
        Assert.Contains("Reserve Bank of India announced updated guidelines", feedVm.ActiveArticleContent);
        Assert.False(feedVm.IsActiveArticleSpeaking);

        // Listen aloud inside reader
        Task readTask = feedVm.ReadActiveArticleAloudCommand.ExecuteAsync(null);
        await Task.Delay(30);
        Assert.True(feedVm.IsActiveArticleSpeaking);

        feedVm.StopArticleReadoutCommand.Execute(null);
        await readTask;
        Assert.False(feedVm.IsActiveArticleSpeaking);

        // AI study analysis quick prompt
        string? receivedPrompt = null;
        feedVm.DiscussCustomPromptRequested += (_, prompt) => receivedPrompt = prompt;

        feedVm.QuickAiAnalysisCommand.Execute("Explain Exam Relevance");
        Assert.NotNull(receivedPrompt);
        Assert.Contains("UPSC / SSC / State PCS", receivedPrompt);
        Assert.Contains("RBI Updates Sovereign Green Bond Framework", receivedPrompt);

        // Close reader
        feedVm.CloseReaderCommand.Execute(null);
        Assert.False(feedVm.IsReaderOpen);
        Assert.Null(feedVm.SelectedArticle);
    }

    [Fact]
    public async Task MainViewModel_PdfReadAloudAndNewsFeedNavigation_WorkCleanly()
    {
        string samplePdfPath = CreateSamplePdf("read_aloud_test.pdf");
        var notesDir = Path.Combine(_tempDir, "read_notes");
        using var mainVm = new MainViewModel(
            _pdfService,
            new RecentFilesService(Path.Combine(_tempDir, "read_state.json")),
            new AnnotationService(Path.Combine(_tempDir, "read_ann")),
            new StudyPlannerService(Path.Combine(_tempDir, "read_plan.json")),
            new NotesService(notesDir),
            aiSettingsService: new AiSettingsService(Path.Combine(_tempDir, "read_ai.json")),
            voiceReadoutService: new StubVoiceReadoutService());

        // Feed tab switching
        mainVm.ShowFeed();
        Assert.True(mainVm.ShowNewsFeed);
        Assert.False(mainVm.ShowHomeDashboard);
        Assert.False(mainVm.ShowNotesStudio);
        Assert.False(mainVm.ShowDocumentReader);

        // Open PDF and verify Read Aloud command
        await mainVm.OpenFileAsync(samplePdfPath);
        Assert.True(mainVm.ShowDocumentReader);
        Assert.False(mainVm.ShowNewsFeed);
        Assert.NotNull(mainVm.SelectedTab);

        Task readTask = mainVm.ReadCurrentPdfPageAloudCommand.ExecuteAsync(null);
        await Task.Delay(30);
        Assert.True(mainVm.IsSpeakingCurrentPage);

        mainVm.StopPdfSpeechCommand.Execute(null);
        await readTask;
        Assert.False(mainVm.IsSpeakingCurrentPage);
    }

    [Fact]
    public void AiChatService_RealTimeTemporalGrounding_InjectsDateAndPreventsCutoffHallucinations()
    {
        var fixedDate = new DateTime(2026, 11, 12, 23, 15, 0, DateTimeKind.Local);
        var settings = new AiAssistantSettings
        {
            PersonaPreset = AiPersonaPresetKind.General,
            AnswerLength = AiAnswerLength.Balanced
        };

        string prompt = AiChatService.BuildEffectiveSystemPrompt(settings, fixedDate);

        Assert.Contains("[REAL-TIME TEMPORAL GROUNDING & CALENDAR ANCHOR]", prompt);
        Assert.Contains("Thursday, November 12, 2026", prompt);
        Assert.Contains("Real-world Present Year: 2026", prompt);
        Assert.Contains("NEVER assume or state that the present year is 2024", prompt);
        Assert.Contains("ground the search in 2026", prompt);
        Assert.Contains("get_current_time", prompt);
        Assert.Contains("get_calendar_info", prompt);
    }

    [Fact]
    public void TemporalTools_CurrentTimeTodayOverviewAndCalendarMath_FormatCorrectDeterministicOutput()
    {
        var fixedDate = new DateTime(2026, 11, 12, 14, 30, 45, DateTimeKind.Local);

        // 1. Current Time tool
        string timeJson = AiChatService.FormatCurrentTimeResponse(fixedDate);
        using var timeDoc = JsonDocument.Parse(timeJson);
        var timeRoot = timeDoc.RootElement;
        Assert.Equal("Thursday, November 12, 2026", timeRoot.GetProperty("current_date").GetString());
        Assert.Equal(2026, timeRoot.GetProperty("year").GetInt32());
        Assert.Equal("November", timeRoot.GetProperty("month").GetString());
        Assert.Equal(12, timeRoot.GetProperty("day").GetInt32());
        Assert.Equal("Thursday", timeRoot.GetProperty("day_of_week").GetString());
        Assert.False(timeRoot.GetProperty("is_leap_year").GetBoolean());
        Assert.Contains("2026", timeRoot.GetProperty("temporal_anchor").GetString());

        // 2. Today Overview tool
        string overviewJson = AiChatService.FormatTodayOverviewResponse(fixedDate);
        using var overviewDoc = JsonDocument.Parse(overviewJson);
        var overviewRoot = overviewDoc.RootElement;
        Assert.Equal("Q4", overviewRoot.GetProperty("quarter").GetString());
        Assert.Equal(316, overviewRoot.GetProperty("day_of_year").GetInt32());
        Assert.Equal(49, overviewRoot.GetProperty("days_remaining_in_year").GetInt32());
        Assert.Equal(2026, overviewRoot.GetProperty("year").GetInt32());

        // 3. Calendar Math - countdown to Dec 31
        string calJson = AiChatService.FormatCalendarInfoResponse("2026-12-31", daysOffset: null, month: null, year: null, fixedDate);
        using var calDoc = JsonDocument.Parse(calJson);
        var calRoot = calDoc.RootElement;
        Assert.Equal(49, calRoot.GetProperty("countdown_days_remaining").GetInt32());
        Assert.Contains("49 day(s) remaining", calRoot.GetProperty("status").GetString());

        // 4. Calendar Math - relative word "tomorrow"
        string tomorrowJson = AiChatService.FormatCalendarInfoResponse("tomorrow", daysOffset: null, month: null, year: null, fixedDate);
        using var tomorrowDoc = JsonDocument.Parse(tomorrowJson);
        Assert.Equal(1, tomorrowDoc.RootElement.GetProperty("countdown_days_remaining").GetInt32());

        // 5. Calendar Math - days in month lookup (Feb 2026 non-leap vs Feb 2024 leap)
        string feb26Json = AiChatService.FormatCalendarInfoResponse(null, null, month: 2, year: 2026, fixedDate);
        using var feb26Doc = JsonDocument.Parse(feb26Json);
        Assert.Equal(28, feb26Doc.RootElement.GetProperty("specified_month_total_days").GetInt32());

        string feb24Json = AiChatService.FormatCalendarInfoResponse(null, null, month: 2, year: 2024, fixedDate);
        using var feb24Doc = JsonDocument.Parse(feb24Json);
        Assert.Equal(29, feb24Doc.RootElement.GetProperty("specified_month_total_days").GetInt32());
    }

    [Fact]
    public void WebResearchService_SanitizesAndGroundsTemporalQueries_FixingCutoffBiasWhilePreservingHistory()
    {
        var fixedDate = new DateTime(2026, 11, 12);

        // A) Stale cutoff year 2024 rewritten to current 2026 when combined with recency words or recurring exam/news concepts
        Assert.Equal(
            "latest current affairs 2026 in hindi",
            WebResearchService.SanitizeAndGroundTemporalQuery("latest current affairs 2024 in hindi", fixedDate));

        Assert.Equal(
            "union budget 2026 highlights",
            WebResearchService.SanitizeAndGroundTemporalQuery("union budget 2024 highlights", fixedDate));

        Assert.Equal(
            "upsc prelims 2026 syllabus updates",
            WebResearchService.SanitizeAndGroundTemporalQuery("upsc prelims 2024 syllabus updates", fixedDate));

        // B) Recency queries without year grounded to 2026
        Assert.Equal(
            "top tech news today 2026",
            WebResearchService.SanitizeAndGroundTemporalQuery("top tech news today", fixedDate));

        // C) Explicit historical events preserved intact
        Assert.Equal(
            "2024 Paris Olympics 100m gold winner",
            WebResearchService.SanitizeAndGroundTemporalQuery("2024 Paris Olympics 100m gold winner", fixedDate));

        Assert.Equal(
            "French Revolution 1789",
            WebResearchService.SanitizeAndGroundTemporalQuery("French Revolution 1789", fixedDate));
    }

    [Fact]
    public async Task AiAssistantViewModel_TemporalToolInvocation_MarksUsedTimeContextAndInvokesClockTools()
    {
        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "time_ai.json"));
        int turn = 0;
        var stubHandler = new StubHttpMessageHandler(_ =>
        {
            turn++;
            if (turn == 1)
            {
                return JsonResp("""
                    {"message":{"role":"assistant","content":"","tool_calls":[
                      {"function":{"name":"get_current_time","arguments":{}}},
                      {"function":{"name":"get_calendar_info","arguments":{"target_date":"2026-12-31"}}}
                    ]}}
                    """);
            }

            return JsonResp("""{"message":{"role":"assistant","content":"Today is Nov 12, 2026 and 49 days remain until the new year."}}""");
        });

        var chatService = new AiChatService(new HttpClient(stubHandler));
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist1")))
        {
            UsePdfContext = false,
            UseWebResearch = false
        };

        aiVm.PromptText = "What is today's date and how many days are left in 2026?";
        await aiVm.SendMessageAsync();

        var assistantMsg = aiVm.Messages.Last();
        Assert.False(assistantMsg.IsError);
        Assert.True(assistantMsg.UsedTimeContext);
        Assert.Contains("Nov 12, 2026", assistantMsg.Content);
        Assert.Contains("49 days remain", assistantMsg.Content);
    }

    [Fact]
    public async Task AiChatHistoryService_PersistsAndRestoresSessionsWithFullDetail()
    {
        string historyDir = Path.Combine(_tempDir, "history_test");
        var service = new AiChatHistoryService(historyDir);

        var session1 = new AiChatSession
        {
            Title = "Photosynthesis & Plant Biology"
        };
        session1.Messages.Add(new AiChatMessage
        {
            Role = "user",
            Content = "Explain the light reaction in photosynthesis."
        });
        session1.Messages.Add(new AiChatMessage
        {
            Role = "assistant",
            Content = "The light reactions occur in the thylakoid membranes.",
            UsedPdfContext = true,
            UsedTimeContext = true,
            Sources = { new WebSourceReference { Index = 1, Title = "Biology Notes", Url = "https://biology.example.com", Snippet = "Thylakoids capture photons." } }
        });

        await service.SaveSessionAsync(session1);

        var loadedList = await service.LoadSessionsAsync();
        Assert.Single(loadedList);
        Assert.Equal(session1.Id, loadedList[0].Id);
        Assert.Equal("Photosynthesis & Plant Biology", loadedList[0].Title);
        Assert.Equal(2, loadedList[0].MessageCount);

        var reloaded = await service.LoadSessionAsync(session1.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded.Messages.Count);
        Assert.Equal("The light reactions occur in the thylakoid membranes.", reloaded.Messages[1].Content);
        Assert.True(reloaded.Messages[1].UsedPdfContext);
        Assert.True(reloaded.Messages[1].UsedTimeContext);
        Assert.Single(reloaded.Messages[1].Sources);

        await service.DeleteSessionAsync(session1.Id);
        var afterDelete = await service.LoadSessionsAsync();
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task AiAssistantViewModel_ChatHistoryManagement_SavesSwitchesAndFiltersSessions()
    {
        string historyDir = Path.Combine(_tempDir, "vm_history");
        var historyService = new AiChatHistoryService(historyDir);
        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "vm_ai_hist.json"));
        var chatService = new AiChatService(new HttpClient(new StubHttpMessageHandler(_ => JsonResp("""{"message":{"role":"assistant","content":"Response to question"}}"""))));
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, chatHistoryService: historyService);

        // Send first message
        aiVm.PromptText = "What is the capital of Australia?";
        await aiVm.SendMessageAsync();

        Assert.Equal("What is the capital of Australia?", aiVm.CurrentSession?.Title);
        Assert.Single(aiVm.AllSessions);

        // Start a new chat
        await aiVm.NewChatAsync();
        Assert.Empty(aiVm.Messages); // dummy welcome message removed
        Assert.True(aiVm.HasNoMessages);
        Assert.Equal("New Study Chat", aiVm.CurrentSession?.Title);

        // Send message in second chat
        aiVm.PromptText = "Explain quantum superposition";
        await aiVm.SendMessageAsync();

        Assert.Equal("Explain quantum superposition", aiVm.CurrentSession?.Title);
        Assert.Equal(2, aiVm.AllSessions.Count);

        // Filter history by search query
        aiVm.HistorySearchQuery = "Australia";
        Assert.Single(aiVm.FilteredSessions);
        Assert.Contains("Australia", aiVm.FilteredSessions[0].Title);

        aiVm.HistorySearchQuery = string.Empty;
        Assert.Equal(2, aiVm.FilteredSessions.Count);

        // Switch back to Australia session
        var australiaSession = aiVm.AllSessions.First(s => s.Title.Contains("Australia"));
        await aiVm.SelectSessionAsync(australiaSession);

        Assert.Equal(australiaSession.Id, aiVm.CurrentSession?.Id);
        Assert.Contains(aiVm.Messages, m => m.Content == "What is the capital of Australia?");
    }

    [Fact]
    public async Task AiChatHistoryService_ExcludesDummyAndFailedSessions_AndPurgesThemFromDisk()
    {
        string historyDir = Path.Combine(_tempDir, "purge_dummy_hist");
        Directory.CreateDirectory(historyDir);

        // 1. Create a dummy/failed session file manually on disk
        string dummyId = Guid.NewGuid().ToString("N");
        string dummyFile = Path.Combine(historyDir, $"{dummyId}.json");
        string dummyJson = """
        {
          "Id": "dummy123",
          "Title": "Explain photosynthesis",
          "CreatedAtUtc": "2026-11-15T07:22:59Z",
          "LastUpdatedAtUtc": "2026-11-15T07:22:59Z",
          "Messages": [
            { "Role": "user", "Content": "Explain photosynthesis", "IsUser": true },
            { "Role": "assistant", "Content": "Could not reach Local LLM", "IsError": true }
          ]
        }
        """;
        await File.WriteAllTextAsync(dummyFile, dummyJson);

        // 2. Create a real session
        var historyService = new AiChatHistoryService(historyDir);
        var realSession = new AiChatSession
        {
            Title = "Real Study Session",
            FirstSearchedTopic = "Thermodynamics principles"
        };
        realSession.Messages.Add(new AiChatMessage { Role = "user", Content = "Thermodynamics principles" });
        realSession.Messages.Add(new AiChatMessage { Role = "assistant", Content = "Energy cannot be created or destroyed." });
        await historyService.SaveSessionAsync(realSession);

        // 3. Load sessions
        var loaded = await historyService.LoadSessionsAsync();

        // 4. Assert only the real session is loaded
        Assert.Single(loaded);
        Assert.Equal(realSession.Id, loaded[0].Id);
        Assert.Equal("Thermodynamics principles", loaded[0].DisplayTopic);

        // 5. Assert the dummy file was purged from disk
        Assert.False(File.Exists(dummyFile));
    }

    [Fact]
    public void AiChatSession_FirstSearchedTopicAndFormattedTime_WorkProperly()
    {
        // 1. Circle to Search topic extraction
        var circleMessages = new List<AiChatMessage>
        {
            new() { Role = "user", Content = "[Circled on Page 16 of 'Chemistry']:\n\"Explain the following terms with one example each. (a) Corrosion (b) Rancidity\"\n\nExplain this." },
            new() { Role = "assistant", Content = "Corrosion is..." }
        };
        string circleTopic = AiChatSession.ExtractFirstSearchedTopic(circleMessages);
        Assert.Contains("Corrosion (b) Rancidity", circleTopic);
        Assert.Contains("p.16", circleTopic);

        // 2. Normal prompt topic extraction
        var regularMessages = new List<AiChatMessage>
        {
            new() { Role = "user", Content = "What is Newton's second law of motion?" },
            new() { Role = "assistant", Content = "F = ma..." }
        };
        string regularTopic = AiChatSession.ExtractFirstSearchedTopic(regularMessages);
        Assert.Equal("What is Newton's second law of motion?", regularTopic);

        // 3. Formatted time check
        var session = new AiChatSession
        {
            LastUpdatedAtUtc = DateTime.UtcNow,
            FirstSearchedTopic = regularTopic
        };
        Assert.Equal(regularTopic, session.DisplayTopic);
        Assert.StartsWith("Today,", session.FormattedTimeDisplay);
    }

    [Fact]
    public async Task AiToolHost_SearchImages_ReturnsFormattedMarkdownAndImageReferences()
    {
        var sampleImages = new List<WebImageReference>
        {
            new()
            {
                Index = 1,
                Title = "Photosynthesis Light Reaction Diagram",
                ImageUrl = "https://cdn.example.com/photosynthesis.png",
                SourcePageUrl = "https://example.com/biology"
            }
        };

        var webService = new StubWebResearchService([], sampleImages);
        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "img_test_settings.json"));
        var settings = new AiAssistantSettings { Provider = AiProviderKind.OpenAi, ApiKey = "fake-key" };
        settingsService.Save(settings);

        string toolCallResponse = """
        {
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                            {
                                "id": "call_img_1",
                                "type": "function",
                                "function": {
                                    "name": "search_images",
                                    "arguments": "{\"query\":\"photosynthesis diagram\"}"
                                }
                            }
                        ]
                    }
                }
            ]
        }
        """;

        string secondTurnResponse = """
        {
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": "Here is the diagram:\n\n![Photosynthesis Light Reaction Diagram](https://cdn.example.com/photosynthesis.png)\n\nThis shows the light reactions."
                    }
                }
            ]
        }
        """;

        int callCount = 0;
        var handler = new StubHttpMessageHandler(req =>
        {
            callCount++;
            return JsonResp(callCount == 1 ? toolCallResponse : secondTurnResponse);
        });

        var chatService = new AiChatService(new HttpClient(handler));
        using var aiVm = new AiAssistantViewModel(
            settingsService,
            chatService,
            webService,
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_img")));

        aiVm.PromptText = "Show me a photosynthesis diagram";
        await aiVm.SendMessageAsync();

        Assert.Equal(2, aiVm.Messages.Count);
        var asstMsg = aiVm.Messages[1];
        Assert.Contains("![Photosynthesis Light Reaction Diagram](https://cdn.example.com/photosynthesis.png)", asstMsg.Content);
        Assert.Single(asstMsg.Images);
        Assert.Equal("https://cdn.example.com/photosynthesis.png", asstMsg.Images[0].ImageUrl);
    }

    [Fact]
    public void AiChatService_NormalizeConversationTurns_PreservesAllTurnsBeyondOldLimit()
    {
        var history = new List<AiChatMessage>();
        for (int i = 1; i <= 20; i++)
        {
            history.Add(new AiChatMessage { Role = "user", Content = $"Question {i}" });
            history.Add(new AiChatMessage { Role = "assistant", Content = $"Answer {i}" });
        }

        var turns = AiChatService.NormalizeConversationTurns(history, "Final Question");

        // Old limit was 12 items. With 60 turns, all 20 pairs (40 turns) + final user turn (41 turns total) are preserved!
        Assert.Equal(41, turns.Count);
        Assert.Equal("Question 1", turns[0].Content);
        Assert.Equal("Final Question", turns[^1].Content);
    }

    [Fact]
    public void AiProviderDefaults_LlamaCppPreset_HasDefaultGgufModel()
    {
        var preset = AiProviderDefaults.GetPreset(AiProviderKind.LlamaCppLocal);
        Assert.Equal("llama.cpp (Local)", preset.DisplayName);
        Assert.Equal("qwen2.5-1.5b-instruct-q4_k_m.gguf", preset.DefaultModel);
        Assert.True(preset.IsLocal);
        Assert.False(preset.RequiresApiKey);
    }

    [Fact]
    public void LlamaCppAutoSetupService_DetectsHardwareSpecsAndRecommendsModel()
    {
        var service = new LlamaCppAutoSetupService(customRootDir: Path.Combine(_tempDir, "llama_test"));
        var specs = service.DetectSystemSpecs();

        Assert.True(specs.TotalRamGb > 0, "Total RAM should be detected.");
        Assert.True(specs.CpuCores >= 1, "CPU Cores should be >= 1.");
        Assert.False(string.IsNullOrWhiteSpace(specs.SummaryText));

        var packages = service.GetAvailablePackages();
        Assert.True(packages.Count >= 3);
        Assert.Contains(packages, p => p.Id == "qwen-1.5b");
        Assert.Contains(packages, p => p.Id == "qwen-3b");
        Assert.Contains(packages, p => p.Id == "qwen-7b");

        var recommended = service.GetRecommendedPackage(specs);
        Assert.NotNull(recommended);
        Assert.False(string.IsNullOrWhiteSpace(recommended.FileName));
        Assert.StartsWith("https://", recommended.DownloadUrl);
    }

    [Fact]
    public void AiAssistantSettings_VoiceSpeedAndPitch_ArePersistedAndCloned()
    {
        var settings = new AiAssistantSettings
        {
            VoiceMode = VoiceReadoutMode.HindiTranslate,
            VoiceModel = "Microsoft David",
            VoiceSpeed = 1.4,
            VoicePitch = 3
        };

        var cloned = settings.Clone();
        Assert.Equal(VoiceReadoutMode.HindiTranslate, cloned.VoiceMode);
        Assert.Equal("Microsoft David", cloned.VoiceModel);
        Assert.Equal(1.4, cloned.VoiceSpeed);
        Assert.Equal(3, cloned.VoicePitch);
    }

    [Fact]
    public async Task AiAssistantViewModel_PassesVoicePitchSpeedAndModelToVoiceService()
    {
        var settingsPath = Path.Combine(_tempDir, "vm_voice_settings.json");
        var settingsService = new AiSettingsService(settingsPath);
        var stubVoiceService = new StubVoiceReadoutService();
        var chatService = new AiChatService(new HttpClient(new StubHttpMessageHandler(_ => JsonResp("{}"))));
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(
            settingsService,
            chatService,
            webService,
            stubVoiceService,
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_vmvoice")));
        aiVm.Settings.VoiceSpeed = 1.3;
        aiVm.Settings.VoicePitch = 2;
        aiVm.Settings.VoiceModel = "Microsoft Zira";
        aiVm.SelectedVoiceMode = VoiceReadoutMode.English;

        var message = new AiChatMessage { Role = "assistant", Content = "Test voice output" };

        Task speakTask = aiVm.ToggleSpeakMessageAsync(message);
        await Task.Delay(40);
        aiVm.StopSpeaking();
        await speakTask;

        Assert.Equal(VoiceReadoutMode.English, stubVoiceService.LastMode);
        Assert.Equal(1.3, stubVoiceService.LastSpeed);
        Assert.Equal(2, stubVoiceService.LastPitch);
        Assert.Equal("Microsoft Zira", stubVoiceService.LastVoiceName);
    }

    [Fact]
    public void AiAssistant_SystemPrompt_EnforcesFactualGrounding()
    {
        var settings = new AiAssistantSettings();
        string systemPrompt = AiChatService.BuildEffectiveSystemPrompt(settings);

        Assert.Contains("FACTUAL INTEGRITY", systemPrompt);
        Assert.Contains("ZERO TOLERANCE FOR FAKE FACTS", systemPrompt);
        Assert.Contains("National Crime Records Bureau (NCRB)", systemPrompt);
        Assert.Contains("Reserve Bank of India (RBI)", systemPrompt);
        Assert.Contains("Census of India", systemPrompt);
        Assert.Contains("TOTAL VOLUME vs PER-CAPITA RATE", systemPrompt);
        Assert.Contains("Instagram Reels Male Narrator (Viral Baritone)", AiPersonalisationDefaults.StandardVoiceModels);

        // Verify map feature is completely removed from prompts
        Assert.DoesNotContain("map:india", systemPrompt);
        Assert.DoesNotContain("map:world", systemPrompt);
        Assert.DoesNotContain("map:india", settings.ResponseFormatPrompt);
        Assert.DoesNotContain("map:world", settings.ResponseFormatPrompt);
    }

    [Fact]
    public async Task UnderlineAndStrikethrough_Drawing_CreatesVectorStrokeAndRendersLines()
    {
        string pdfPath = CreateSamplePdf("drawing_markup.pdf");
        var info = await _pdfService.OpenDocumentAsync(pdfPath);
        using var tab = new DocumentTabViewModel(info, _pdfService);

        // 1. Drawing Underline with freehand points (e.g. margin or non-text region)
        var drawnUnderline = await tab.CommitWordBrushAnnotationAsync(
            1,
            [],
            [new NormalizedPoint(0.1, 0.5), new NormalizedPoint(0.4, 0.51)],
            AnnotationType.Underline);

        Assert.NotNull(drawnUnderline);
        Assert.Equal(AnnotationType.Underline, drawnUnderline.Type);
        Assert.Equal(2, drawnUnderline.Points.Count);
        Assert.Empty(drawnUnderline.Rects);

        // Verify page vector visual renders as PathGeometry stroke
        var pageVisuals = tab.Pages[0].VectorAnnotations;
        var underlineVisual = Assert.Single(pageVisuals, v => v.Type == AnnotationType.Underline);
        Assert.IsType<System.Windows.Media.PathGeometry>(underlineVisual.Geometry);
        Assert.True(underlineVisual.StrokeThickness > 0);

        // 2. Drawing Strikethrough with freehand points
        var drawnStrike = await tab.CommitWordBrushAnnotationAsync(
            1,
            [],
            [new NormalizedPoint(0.1, 0.6), new NormalizedPoint(0.5, 0.61)],
            AnnotationType.Strikethrough);

        Assert.NotNull(drawnStrike);
        Assert.Equal(AnnotationType.Strikethrough, drawnStrike.Type);
        Assert.Equal(2, drawnStrike.Points.Count);

        var strikeVisual = Assert.Single(tab.Pages[0].VectorAnnotations, v => v.Type == AnnotationType.Strikethrough);
        Assert.IsType<System.Windows.Media.PathGeometry>(strikeVisual.Geometry);

        // 3. Drawing Underline that hits words
        var words = await tab.GetOrLoadPageWordsAsync(1);
        Assert.NotEmpty(words);
        var paintedIndices = new HashSet<int> { 0, 1 };
        var textUnderline = await tab.CommitWordBrushAnnotationAsync(
            1,
            paintedIndices,
            [new NormalizedPoint(0.1, 0.1), new NormalizedPoint(0.3, 0.1)],
            AnnotationType.Underline);

        Assert.NotNull(textUnderline);
        Assert.Equal(AnnotationType.Underline, textUnderline.Type);
        Assert.NotEmpty(textUnderline.Rects);
    }

    private sealed class StubNewsFeedService(IReadOnlyList<NewsFeedItem> items, string? articleContent = null) : INewsFeedService
    {
        public Task<IReadOnlyList<NewsFeedItem>> FetchNewsAsync(NewsTimeframe timeframe, NewsCategory category, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(items);

        public Task<string?> FetchArticleContentAsync(string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(articleContent ?? "Full extracted article content paragraph 1.\n\nFull extracted article content paragraph 2.");
    }

    private sealed class StubVoiceReadoutService : IVoiceReadoutService
    {
        private TaskCompletionSource<bool>? _tcs;
        public VoiceReadoutMode LastMode { get; private set; }
        public double LastSpeed { get; private set; } = 1.0;
        public int LastPitch { get; private set; }
        public string? LastVoiceName { get; private set; }
        public int StopCount { get; private set; }

        public Task SpeakAsync(string markdownText, VoiceReadoutMode mode, CancellationToken cancellationToken = default) =>
            SpeakAsync(markdownText, mode, 1.0, 0, null, cancellationToken);

        public async Task SpeakAsync(string markdownText, VoiceReadoutMode mode, double speed, int pitch, string? voiceName = null, CancellationToken cancellationToken = default)
        {
            LastMode = mode;
            LastSpeed = speed;
            LastPitch = pitch;
            LastVoiceName = voiceName;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
            await _tcs.Task;
        }

        public IReadOnlyList<string> GetInstalledWindowsVoices() => ["Microsoft David", "Microsoft Zira"];

        public Task PlayAudioBytesAsync(byte[] audioBytes, string format = "wav", double speed = 1.0, CancellationToken cancellationToken = default)
        {
            LastSpeed = speed;
            return Task.CompletedTask;
        }

        public void Stop()
        {
            StopCount++;
            _tcs?.TrySetCanceled();
        }

        public void Dispose() => Stop();
    }

    private sealed class SlowChunkedStream(string chunk1, string chunk2, Task gate) : Stream
    {
        private int _stage;
        private byte[] _current = [];
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous Read should never be called while streaming!");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_offset >= _current.Length)
            {
                if (_stage == 0)
                {
                    _current = Encoding.UTF8.GetBytes(chunk1);
                    _offset = 0;
                    _stage = 1;
                }
                else if (_stage == 1)
                {
                    await gate.WaitAsync(cancellationToken);
                    _current = Encoding.UTF8.GetBytes(chunk2);
                    _offset = 0;
                    _stage = 2;
                }
                else
                {
                    return 0;
                }
            }

            int copy = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsSpan(_offset, copy).CopyTo(buffer.Span);
            _offset += copy;
            return copy;
        }
    }

    private static HttpResponseMessage JsonResp(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = req => Task.FromResult(responder(req));

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responder(request);
    }

    private sealed class StubWebResearchService(
        IReadOnlyList<WebSourceReference> sources,
        IReadOnlyList<WebImageReference>? images = null) : IWebResearchService
    {
        public int CallCount { get; private set; }
        public int ImageCallCount { get; private set; }

        public Task<IReadOnlyList<WebSourceReference>> SearchAsync(string query, AiAssistantSettings settings, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(sources);
        }

        public Task<IReadOnlyList<WebImageReference>> SearchImagesAsync(string query, AiAssistantSettings settings, CancellationToken cancellationToken = default)
        {
            ImageCallCount++;
            return Task.FromResult<IReadOnlyList<WebImageReference>>(images ?? []);
        }
    }

    [Fact]
    public void NcertLibraryCatalog_ContainsAllClassesAndSubjects()
    {
        var books = NcertLibraryCatalog.AllBooks;
        Assert.NotEmpty(books);
        Assert.True(books.Count >= 30);

        // Verify Classes 6 to 12 exist
        for (int c = 6; c <= 12; c++)
        {
            Assert.Contains(books, b => b.ClassNumber == c);
        }

        // Verify Class 10 has Science and Math
        Assert.Contains(books, b => b.ClassNumber == 10 && b.Code == "jesc1" && b.Title.Contains("Science"));
        Assert.Contains(books, b => b.ClassNumber == 10 && b.Code == "jemh1" && b.Title.Contains("Mathematics"));

        // Verify Class 12 has Physics, Chemistry, Biology, Math
        Assert.Contains(books, b => b.ClassNumber == 12 && b.Code == "leph1");
        Assert.Contains(books, b => b.ClassNumber == 12 && b.Code == "lech1");
        Assert.Contains(books, b => b.ClassNumber == 12 && b.Code == "lebo1");
        Assert.Contains(books, b => b.ClassNumber == 12 && b.Code == "lemh1");
    }

    [Fact]
    public async Task NcertLibraryViewModel_FiltersByClassSubjectAndSearch()
    {
        var libraryService = new NcertLibraryService(customLibraryRoot: Path.Combine(_tempDir, "ncert_test_lib"));
        var vm = new NcertLibraryViewModel(libraryService);

        Assert.NotEmpty(vm.AllBooks);
        Assert.Equal(vm.AllBooks.Count, vm.FilteredBooks.Count);

        // Filter by Class 10
        vm.SelectedClassOption = "Class 10";
        Assert.True(vm.FilteredBooks.All(b => b.ClassNumber == 10));
        Assert.True(vm.FilteredBooks.Count >= 5);

        // Filter by Subject Science within Class 10
        vm.SelectedSubject = "Science";
        Assert.Single(vm.FilteredBooks);
        Assert.Equal("jesc1", vm.FilteredBooks[0].Code);

        // Reset class to All
        vm.SelectedClassOption = "All Classes";
        vm.SelectedSubject = "Physics";
        Assert.All(vm.FilteredBooks, b => Assert.Equal("Physics", b.Subject));

        // Search query
        vm.SelectedSubject = "All";
        vm.SearchText = "Harappa";
        Assert.Contains(vm.FilteredBooks, b => b.Code == "lehs1");

        // Open Book Event
        string? openedPath = null;
        vm.BookOpenRequested += path => openedPath = path;

        // Mock a downloaded book
        var testBook = vm.FilteredBooks[0];
        string fakeBookDir = Path.Combine(libraryService.GetLibraryRootDirectory(), testBook.Code);
        Directory.CreateDirectory(fakeBookDir);
        string fakePdf = Path.Combine(fakeBookDir, $"{testBook.Code}01.pdf");
        File.WriteAllText(fakePdf, "%PDF-1.4 test");

        await libraryService.RefreshDownloadStatesAsync([testBook]);
        Assert.True(testBook.IsDownloaded);

        vm.OpenBookCommand.Execute(testBook);
        Assert.Equal(fakePdf, openedPath);
    }

    [Fact]
    public async Task DocumentTabViewModel_AddUnderlineAndStrikethroughStroke_DrawsFreehandPolylineVectors()
    {
        string samplePdfPath = CreateSamplePdf("markup_stroke_test.pdf");
        var info = await _pdfService.OpenDocumentAsync(samplePdfPath);
        using var tab = new DocumentTabViewModel(info, _pdfService);

        var strokePoints = new List<NormalizedPoint>
        {
            new(0.1, 0.5),
            new(0.2, 0.51),
            new(0.3, 0.49),
            new(0.4, 0.52),
            new(0.5, 0.50)
        };

        var underlineAnn = tab.AddUnderlineStrokeAnnotation(1, strokePoints);
        Assert.NotNull(underlineAnn);
        Assert.Equal(AnnotationType.Underline, underlineAnn.Type);
        Assert.Equal(5, underlineAnn.Points.Count);
        Assert.Equal(0.1, underlineAnn.Points[0].X);

        var strikeAnn = tab.AddStrikethroughStrokeAnnotation(1, strokePoints);
        Assert.NotNull(strikeAnn);
        Assert.Equal(AnnotationType.Strikethrough, strikeAnn.Type);
        Assert.Equal(5, strikeAnn.Points.Count);

        // Verify page visual collection contains these annotations
        var page1 = tab.Pages[0];
        Assert.Contains(page1.VectorAnnotations, v => v.AnnotationId == underlineAnn.Id);
        Assert.Contains(page1.VectorAnnotations, v => v.AnnotationId == strikeAnn.Id);
    }

    [Fact]
    public void MainWindowXaml_AllSymbolsAreValidEnumValues()
    {
        string? dir = AppContext.BaseDirectory;
        string xamlPath = "";
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "src", "StudySuit.PdfReader", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                xamlPath = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.True(File.Exists(xamlPath), $"Could not find MainWindow.xaml from {AppContext.BaseDirectory}");

        string xamlContent = File.ReadAllText(xamlPath);
        var matches = System.Text.RegularExpressions.Regex.Matches(xamlContent, @"Symbol=""([^""{}]+)""");

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            string symbolName = match.Groups[1].Value.Trim();
            bool isValid = Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(symbolName, out _);
            Assert.True(isValid, $"Symbol '{symbolName}' in MainWindow.xaml is not a valid SymbolRegular enum value!");
        }
    }

    [Fact]
    public void MainViewModel_WorkspaceTabNavigation_MutuallyExclusiveViews()
    {
        using var vm = new MainViewModel(
            _pdfService,
            new RecentFilesService(Path.Combine(_tempDir, "tabs_nav_state.json")),
            new AnnotationService(Path.Combine(_tempDir, "tabs_nav_ann")));

        // 1. Initial State: Home is active by default
        Assert.True(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNcertLibrary);
        Assert.False(vm.ShowNewsFeed);
        Assert.False(vm.ShowNotesStudio);
        Assert.False(vm.ShowDocumentReader);

        // 2. Switch to NCERT Library
        vm.ShowLibraryCommand.Execute(null);
        Assert.True(vm.ShowNcertLibrary);
        Assert.False(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNewsFeed);
        Assert.False(vm.ShowNotesStudio);
        Assert.False(vm.ShowDocumentReader);

        // 3. Switch to News Feed
        vm.ShowFeedCommand.Execute(null);
        Assert.True(vm.ShowNewsFeed);
        Assert.False(vm.ShowNcertLibrary);
        Assert.False(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNotesStudio);
        Assert.False(vm.ShowDocumentReader);

        // 4. Switch to Notes Studio
        vm.ShowNotesCommand.Execute(null);
        Assert.True(vm.ShowNotesStudio);
        Assert.False(vm.ShowNcertLibrary);
        Assert.False(vm.ShowNewsFeed);
        Assert.False(vm.ShowHomeDashboard);
        Assert.False(vm.ShowDocumentReader);

        // 5. Switch back to Home
        vm.ShowHomeCommand.Execute(null);
        Assert.True(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNcertLibrary);
        Assert.False(vm.ShowNewsFeed);
        Assert.False(vm.ShowNotesStudio);
        Assert.False(vm.ShowDocumentReader);
    }

    [Fact]
    public void MainWindowXaml_WorkspaceRoots_AreSolidAndProperlyScoped()
    {
        string? dir = AppContext.BaseDirectory;
        string xamlPath = "";
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "src", "StudySuit.PdfReader", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                xamlPath = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.True(File.Exists(xamlPath));

        string xaml = File.ReadAllText(xamlPath);

        // Ensure NcertLibraryRoot does NOT bind DataContext on its outer element (which would break ShowNcertLibrary visibility)
        Assert.DoesNotMatch(@"<Grid\s+x:Name=""NcertLibraryRoot""[^>]*DataContext=", xaml);

        // Ensure NcertLibraryRoot has solid background
        Assert.Matches(@"<Grid\s+x:Name=""NcertLibraryRoot""[^>]*Background=""{DynamicResource SolidBackgroundFillColorBaseBrush}""", xaml);

        // Ensure HomeDashboardRoot has solid background
        Assert.Matches(@"<Grid\s+x:Name=""HomeDashboardRoot""[^>]*Background=""{DynamicResource SolidBackgroundFillColorBaseBrush}""", xaml);

        // Ensure default window dimensions in XAML do not exceed 1200x750
        var widthMatch = System.Text.RegularExpressions.Regex.Match(xaml, @"Width=""(\d+)""");
        Assert.True(widthMatch.Success);
        Assert.True(int.Parse(widthMatch.Groups[1].Value) <= 1200, "Default window Width should not exceed 1200 DIPs");

        var heightMatch = System.Text.RegularExpressions.Regex.Match(xaml, @"Height=""(\d+)""");
        Assert.True(heightMatch.Success);
        Assert.True(int.Parse(heightMatch.Groups[1].Value) <= 750, "Default window Height should not exceed 750 DIPs");
    }

    [Fact]
    public async Task NcertLibraryService_BackgroundThreadUpdates_DoNotThrowCollectionViewException()
    {
        string customRoot = Path.Combine(_tempDir, "ncert_test_books");
        string bookCode = "testbook1";
        string bookDir = Path.Combine(customRoot, bookCode);
        Directory.CreateDirectory(bookDir);

        // Create sample chapter PDFs
        File.WriteAllBytes(Path.Combine(bookDir, $"{bookCode}01.pdf"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(bookDir, $"{bookCode}02.pdf"), [4, 5, 6]);

        var service = new NcertLibraryService(customLibraryRoot: customRoot);
        var book = new NcertBookItem
        {
            Code = bookCode,
            Title = "Test Book",
            ShortTitle = "Test",
            ClassNumber = 10,
            Subject = "Science"
        };

        // Run from a background thread to simulate async network completion
        await Task.Run(async () =>
        {
            await service.RefreshDownloadStatesAsync([book]);
        });

        Assert.True(book.IsDownloaded);
        Assert.Equal(2, book.Chapters.Count);
        Assert.NotNull(book.PrimaryPdfPath);
        Assert.True(File.Exists(book.PrimaryPdfPath));

        // Test delete on background thread as well
        await Task.Run(async () =>
        {
            await service.DeleteBookAsync(book);
        });

        Assert.False(book.IsDownloaded);
        Assert.Empty(book.Chapters);
    }

    [Fact]
    public void CircleToSearch_ToolAndXamlValidation()
    {
        // 1. Tool Enum has CircleToSearch
        Assert.True(Enum.IsDefined(typeof(AnnotationTool), AnnotationTool.CircleToSearch));

        // 2. MainWindow.xaml has the Circle to Search toolbar button and card
        string? dir = AppContext.BaseDirectory;
        string xamlPath = "";
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "src", "StudySuit.PdfReader", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                xamlPath = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }
        Assert.True(File.Exists(xamlPath));

        string xaml = File.ReadAllText(xamlPath);
        Assert.Contains("ConverterParameter=CircleToSearch", xaml);
        Assert.Contains("x:Name=\"CircleToSearchCard\"", xaml);
        Assert.Contains("CircleAction_Explain_Click", xaml);
        Assert.Contains("CircleAction_Solve_Click", xaml);
        Assert.Contains("CircleAction_WebSearch_Click", xaml);
        Assert.Contains("CircleAction_Practice_Click", xaml);
        Assert.Contains("x:Name=\"CircleToSearchCustomPromptBox\"", xaml);
    }

    [Fact]
    public void MathEquationRenderer_RendersScientificAndChemicalEquationsCorrectly()
    {
        // 1. User's exact chemical decomposition reaction
        string chemicalDecomposition = @"\text{FeSO}_4(s) \xrightarrow{\Delta} \text{Fe}_2\text{O}_3(s) + \text{SO}_2(g) + \text{SO}_3(g)";
        var bmp1 = MathEquationRenderer.RenderEquation(chemicalDecomposition);
        Assert.True(bmp1.IsFrozen);
        Assert.True(bmp1.PixelWidth > 120);
        Assert.True(bmp1.PixelHeight > 40);

        // 2. Reaction with condition below arrow
        string arrowWithConditions = @"\text{N}_2(g) + 3\text{H}_2(g) \xrightarrow[\text{450}^\circ\text{C}]{\text{Fe catalyst}} 2\text{NH}_3(g)";
        var bmp2 = MathEquationRenderer.RenderEquation(arrowWithConditions);
        Assert.True(bmp2.IsFrozen);
        Assert.True(bmp2.PixelWidth > 150);

        // 3. Equilibrium reaction
        string equilibrium = @"\text{N}_2\text{O}_4(g) \rightleftharpoons 2\text{NO}_2(g)";
        var bmp3 = MathEquationRenderer.RenderEquation(equilibrium);
        Assert.True(bmp3.IsFrozen);
        Assert.True(bmp3.PixelWidth > 80);

        // 4. \ce{...} syntax preprocessing
        string ceSyntax = @"\ce{2FeSO4 ->[\Delta] Fe2O3 + SO2 + SO3}";
        var bmp4 = MathEquationRenderer.RenderEquation(ceSyntax);
        Assert.True(bmp4.IsFrozen);
        Assert.True(bmp4.PixelWidth > 100);

        // 5. Physics vectors and accents
        string physicsVector = @"\vec{F} = m\vec{a} = m\frac{d\vec{v}}{dt}";
        var bmp5 = MathEquationRenderer.RenderEquation(physicsVector);
        Assert.True(bmp5.IsFrozen);
        Assert.True(bmp5.PixelWidth > 80);

        // 6. Physics unit vectors
        string unitVectors = @"\vec{r} = x\hat{i} + y\hat{j} + z\hat{k}";
        var bmp6 = MathEquationRenderer.RenderEquation(unitVectors);
        Assert.True(bmp6.IsFrozen);

        // 7. Binomial coefficient and root with degree
        string mathAdvanced = @"\binom{n}{k} = \frac{n!}{k!(n-k)!}, \quad \sqrt[3]{x^3 + y^3}";
        var bmp7 = MathEquationRenderer.RenderEquation(mathAdvanced);
        Assert.True(bmp7.IsFrozen);
        Assert.True(bmp7.PixelHeight > 40);

        // 8. Multi-line equations with \\
        string multiLine = @"2\text{H}_2 + \text{O}_2 \rightarrow 2\text{H}_2\text{O} \\ \text{C} + \text{O}_2 \rightarrow \text{CO}_2";
        var bmp8 = MathEquationRenderer.RenderEquation(multiLine);
        Assert.True(bmp8.IsFrozen);
        Assert.True(bmp8.PixelHeight > 60); // Multiple stacked lines have increased height

        // 9. Exact unescaped scientific equation from user screenshot (e.g. from loose markdown/tokens)
        string userUnescapedReaction = @"textFeSO_4(s) xrightarrow\Delta textFe_2textO_3(s) + textSO_2(g) + textSO_3(g)";
        var bmp9 = MathEquationRenderer.RenderEquation(userUnescapedReaction);
        Assert.True(bmp9.IsFrozen);
        Assert.True(bmp9.PixelWidth > 120);
        Assert.True(bmp9.PixelHeight > 25);
    }

    [Fact]
    public void CircleToSearch_StrictlyConsidersAreaUnderCircle_AndSortsReadingOrder()
    {
        // 1. Ray-casting point-in-polygon unit test
        var polygon = new List<NormalizedPoint>
        {
            new(0.2, 0.2),
            new(0.8, 0.2),
            new(0.8, 0.6),
            new(0.2, 0.6)
        };

        Assert.True(DocumentTabViewModel.IsPointInPolygon(new NormalizedPoint(0.5, 0.4), polygon));
        Assert.True(DocumentTabViewModel.IsPointInPolygon(new NormalizedPoint(0.3, 0.3), polygon));
        Assert.False(DocumentTabViewModel.IsPointInPolygon(new NormalizedPoint(0.1, 0.1), polygon));
        Assert.False(DocumentTabViewModel.IsPointInPolygon(new NormalizedPoint(0.5, 0.7), polygon)); // Below polygon (e.g. next question)

        // 2. Reading order test: 2-line question with words presented out of order
        // Line 1: "What" (x=0.1, y=0.2), "is" (x=0.2, y=0.2), "matter?" (x=0.3, y=0.2)
        // Line 2: "Explain" (x=0.1, y=0.25), "in" (x=0.25, y=0.25), "detail." (x=0.35, y=0.25)
        var words = new List<PdfWordBox>
        {
            new("Explain", new NormalizedRect(0.1, 0.25, 0.1, 0.03)),
            new("is", new NormalizedRect(0.2, 0.20, 0.05, 0.03)),
            new("What", new NormalizedRect(0.1, 0.20, 0.08, 0.03)),
            new("detail.", new NormalizedRect(0.35, 0.25, 0.1, 0.03)),
            new("matter?", new NormalizedRect(0.3, 0.20, 0.1, 0.03)),
            new("in", new NormalizedRect(0.25, 0.25, 0.05, 0.03)),
        };

        var sorted = DocumentTabViewModel.SortWordsInReadingOrder(words);
        string extracted = string.Join(' ', sorted.Select(w => w.Text));
        Assert.Equal("What is matter? Explain in detail.", extracted);
    }

    [Fact]
    public async Task AiAssistant_CircleToSearchCreatesAndPersistsDedicatedSession()
    {
        var settingsPath = Path.Combine(_tempDir, "circle_settings.json");
        var settingsService = new AiSettingsService(settingsPath);
        var stubHandler = new StubHttpMessageHandler(_ =>
        {
            return JsonResp("""{"message":{"role":"assistant","content":"Ferrous sulphate undergoes thermal decomposition."}}""");
        });
        var chatService = new AiChatService(new HttpClient(stubHandler));
        var webService = new StubWebResearchService([]);
        var histDir = Path.Combine(_tempDir, "circle_hist");
        var historyService = new AiChatHistoryService(histDir);

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, chatHistoryService: historyService);

        string sessionTitle = "🔍 FeSO4 thermal decomposition (p.6)";
        string prompt = "[Circled on Page 6 of 'Chemistry']: \"FeSO4 heating\"\n\nExplain this decomposition reaction.";

        await aiVm.StartCircleToSearchSessionAsync(sessionTitle, prompt);

        // Verify session was updated and persisted
        Assert.Equal(sessionTitle, aiVm.CurrentSession?.Title);
        Assert.Equal(2, aiVm.Messages.Count);
        Assert.False(aiVm.HasNoMessages);

        // Verify it was persisted to history
        var sessions = await historyService.LoadSessionsAsync();
        Assert.Single(sessions);
        Assert.Equal(sessionTitle, sessions[0].Title);
        Assert.Equal(2, sessions[0].MessageCount);
    }

    [Fact]
    public async Task WindowsOcrService_RecognizesMultiLineCircledQuestionFromRenderedBitmap()
    {
        var ocr = new WindowsOcrService();
        Assert.True(ocr.IsSupported, $"OCR must be supported! Available languages: {string.Join(", ", Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag))}");

        int width = 950;
        int height = 140;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
            var ft = new System.Windows.Media.FormattedText(
                "12. Write one equation each for decomposition reactions where energy is supplied in\nthe form of heat, light or electricity.",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Arial"),
                22,
                System.Windows.Media.Brushes.Black,
                1.0);
            dc.DrawText(ft, new System.Windows.Point(20, 20));
        }
        rtb.Render(dv);
        rtb.Freeze();

        var crop = new System.Windows.Media.Imaging.CroppedBitmap(rtb, new System.Windows.Int32Rect(10, 10, width - 20, height - 20));
        crop.Freeze();

        string recognized = await ocr.RecognizeTextFromBitmapAsync(crop);

        Assert.False(string.IsNullOrWhiteSpace(recognized));
        Assert.Contains("Write one equation each for decomposition", recognized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heat, light or electricity", recognized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealPage16_PdfWords_AndOcr_Inspection()
    {
        string pdfPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");

        if (!File.Exists(pdfPath)) return;

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = doc.GetPage(16);
        var cb = page.CropBox.Bounds;

        double cropLeft = cb.Left;
        double cropTop = cb.Top;
        double boxW = cb.Width;
        double boxH = cb.Height;

        var q12Words = page.GetWords()
            .Where(w => w.Text.Contains("decomposition", StringComparison.OrdinalIgnoreCase) ||
                        w.Text.Contains("electricity", StringComparison.OrdinalIgnoreCase) ||
                        w.Text.Contains("energy", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();
        foreach (var w in q12Words)
        {
            var b = w.BoundingBox;
            double x = (b.Left - cropLeft) / boxW;
            double y = (cropTop - b.Top) / boxH;
            sb.AppendLine($"{w.Text}: raw Left={b.Left}, Top={b.Top} -> Norm X={x:F4}, Y={y:F4}");
        }

        Assert.True(q12Words.Count >= 3, sb.ToString());
    }

    [Fact]
    public void MainViewModel_WorkspaceNavigation_MaintainsStrictMutualExclusion()
    {
        string statePath = Path.Combine(_tempDir, "excl_state.json");
        string annDir = Path.Combine(_tempDir, "excl_ann");
        var recentService = new RecentFilesService(statePath);
        var annotationService = new AnnotationService(annDir);
        using var vm = new MainViewModel(_pdfService, recentService, annotationService);

        // 1. Initially Home is active
        Assert.True(vm.IsHomeActive);
        Assert.False(vm.IsNotesActive);
        Assert.False(vm.IsFeedActive);
        Assert.False(vm.IsLibraryActive);
        Assert.True(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNcertLibrary);

        // 2. Switch to Library
        vm.ShowLibraryCommand.Execute(null);
        Assert.False(vm.IsHomeActive);
        Assert.False(vm.IsNotesActive);
        Assert.False(vm.IsFeedActive);
        Assert.True(vm.IsLibraryActive);
        Assert.False(vm.ShowHomeDashboard);
        Assert.True(vm.ShowNcertLibrary);

        // 3. Switch to Notes
        vm.ShowNotesCommand.Execute(null);
        Assert.False(vm.IsHomeActive);
        Assert.True(vm.IsNotesActive);
        Assert.False(vm.IsFeedActive);
        Assert.False(vm.IsLibraryActive);
        Assert.False(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNcertLibrary);
        Assert.True(vm.ShowNotesStudio);

        // 4. Switch back to Home
        vm.ShowHomeCommand.Execute(null);
        Assert.True(vm.IsHomeActive);
        Assert.False(vm.IsNotesActive);
        Assert.False(vm.IsFeedActive);
        Assert.False(vm.IsLibraryActive);
        Assert.True(vm.ShowHomeDashboard);
        Assert.False(vm.ShowNcertLibrary);
    }

    [Fact]
    public void AiAssistantViewModel_StartsCleanWithZeroDummyMessages()
    {
        string settingsPath = Path.Combine(_tempDir, "clean_ai_settings.json");
        var settingsService = new AiSettingsService(settingsPath);
        var chatService = new AiChatService(new HttpClient(new StubHttpMessageHandler(_ => JsonResp("{}"))));
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(
            settingsService,
            chatService,
            webService,
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_clean")));

        // Verify initial state has 0 messages (dummy message removed)
        Assert.Empty(aiVm.Messages);
        Assert.True(aiVm.HasNoMessages);
        Assert.False(aiVm.HasMessages);
    }

    [Fact]
    public async Task AiAssistantViewModel_LocalLlmWithoutModel_DisplaysSetupInstructions()
    {
        string settingsPath = Path.Combine(_tempDir, "local_no_model.json");
        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.OllamaLocal,
            BaseUrl = "http://localhost:11434",
            ModelName = "" // empty model name
        };
        var settingsService = new AiSettingsService(settingsPath);
        settingsService.Save(settings);

        var chatService = new AiChatService(new HttpClient(new StubHttpMessageHandler(_ => JsonResp("{}"))));
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(
            settingsService,
            chatService,
            webService,
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_no_model")));
        aiVm.Settings.ModelName = ""; // user has not configured a model
        aiVm.PromptText = "Hello!";
        await aiVm.SendMessageAsync();

        // Must show setup instructions in chat instead of throwing socket error
        Assert.Equal(2, aiVm.Messages.Count); // 1 user, 1 assistant
        var assistantMsg = aiVm.Messages[1];
        Assert.Contains("Local LLM Setup Required", assistantMsg.Content);
        Assert.Contains("AI Settings", assistantMsg.Content);
        Assert.Contains("Ollama", assistantMsg.Content);
    }

    [Fact]
    public async Task AiAssistantViewModel_LocalLlmConnectionFailure_DisplaysHelpfulGuide()
    {
        string settingsPath = Path.Combine(_tempDir, "local_offline.json");
        var settings = new AiAssistantSettings
        {
            Provider = AiProviderKind.OllamaLocal,
            BaseUrl = "http://localhost:59999", // dead port
            ModelName = "llama3.2"
        };
        var settingsService = new AiSettingsService(settingsPath);
        settingsService.Save(settings);

        // Mock chat service that simulates unreachable local server
        var mockFailingService = new FailingLocalChatService();
        var webService = new StubWebResearchService([]);

        using var aiVm = new AiAssistantViewModel(
            settingsService,
            mockFailingService,
            webService,
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_offline")));
        aiVm.PromptText = "Explain photosynthesis";
        await aiVm.SendMessageAsync();

        Assert.Equal(2, aiVm.Messages.Count);
        var errMsg = aiVm.Messages[1];
        Assert.True(errMsg.IsError);
        Assert.Contains("Could not reach Local LLM", errMsg.Content);
        Assert.Contains("ollama run", errMsg.Content);
    }

    private sealed class FailingLocalChatService : IAiChatService
    {
        public Task<string> SendChatAsync(AiAssistantSettings settings, IReadOnlyList<AiChatMessage> history, string userPrompt, string? pdfContext, IReadOnlyList<WebSourceReference> webSources, Action<string>? onStreamDelta = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("No connection could be made because the target machine actively refused it.");

        public Task<string> SendChatWithToolsAsync(AiAssistantSettings settings, IReadOnlyList<AiChatMessage> history, string userPrompt, string? pdfContext, AiToolHost? toolHost, Action<string>? onStreamDelta = null, byte[]? inputAudioBytes = null, string inputAudioFormat = "wav", CancellationToken cancellationToken = default)
            => throw new HttpRequestException("No connection could be made because the target machine actively refused it.");

        public Task<IReadOnlyList<string>> FetchAvailableModelsAsync(AiAssistantSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<(bool Success, string Message)> TestConnectionAsync(AiAssistantSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult((false, "Connection refused"));
    }

    private string CreateSamplePdf(string fileName)
    {
        string path = Path.Combine(_tempDir, fileName);
        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = "StudySuit Test PDF";
        builder.DocumentInformation.Author = "Test Author";

        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page1 = builder.AddPage(PageSize.Letter);
        page1.AddText("Modern Fluent PDF Reader Test Page One", 16, new PdfPoint(72, 700), font);

        var page2 = builder.AddPage(PageSize.Letter);
        page2.AddText("Chapter Two Additional Content", 16, new PdfPoint(72, 700), font);

        byte[] bytes = builder.Build();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task ScanlineMaskedOcr_NcErtPage16_Question20()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");

        if (!File.Exists(ncertPath)) return;

        var ocr = new WindowsOcrService();
        if (!ocr.IsSupported) return;

        // Render page 16 at 192 DPI
        var bmp = await _pdfService.RenderPageAsync(ncertPath, 16, 192, 0);

        string scratchDir = @"C:\Users\SURYA\.gemini\antigravity\brain\d218edce-7961-4978-918b-55ddf267c5d1\scratch";
        Directory.CreateDirectory(scratchDir);
        var encFull = new PngBitmapEncoder();
        encFull.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(Path.Combine(scratchDir, "page16_full.png")))
        {
            encFull.Save(fs);
        }

        // Define user's circle around Question 20:
        // Question 19 ends at Y=0.358 (y=750)
        // Question 20 starts at Y=0.360 (y=755) down to Y=0.420 (y=878)
        var polygon = new List<NormalizedPoint>
        {
            new(0.08, 0.359),
            new(0.50, 0.359),
            new(0.85, 0.359),
            new(0.86, 0.390),
            new(0.85, 0.422),
            new(0.50, 0.422),
            new(0.08, 0.422),
            new(0.07, 0.390),
            new(0.08, 0.359)
        };

        double minX = polygon.Min(p => p.X);
        double maxX = polygon.Max(p => p.X);
        double minY = polygon.Min(p => p.Y);
        double maxY = polygon.Max(p => p.Y);

        int pw = bmp.PixelWidth;
        int ph = bmp.PixelHeight;

        int px = (int)Math.Floor(minX * pw);
        int py = (int)Math.Floor(minY * ph);
        int cropW = (int)Math.Ceiling((maxX - minX) * pw);
        int cropH = (int)Math.Ceiling((maxY - minY) * ph);

        var crop = new CroppedBitmap(bmp, new System.Windows.Int32Rect(px, py, cropW, cropH));
        var bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
        int stride = cropW * 4;
        byte[] pixels = new byte[cropH * stride];
        bgra.CopyPixels(pixels, stride, 0);

        // Scanline mask: for each row, find polygon intersections
        var polyPts = polygon.Select(p => new System.Windows.Point(p.X * pw - px, p.Y * ph - py)).ToList();
        for (int y = 0; y < cropH; y++)
        {
            var intersections = new List<double>();
            for (int i = 0; i < polyPts.Count; i++)
            {
                var p1 = polyPts[i];
                var p2 = polyPts[(i + 1) % polyPts.Count];

                if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                {
                    double t = (y - p1.Y) / (p2.Y - p1.Y);
                    intersections.Add(p1.X + t * (p2.X - p1.X));
                }
            }

            intersections.Sort();

            int rowOffset = y * stride;
            for (int x = 0; x < cropW; x++)
            {
                bool inside = false;
                for (int k = 0; k + 1 < intersections.Count; k += 2)
                {
                    if (x >= intersections[k] && x <= intersections[k + 1])
                    {
                        inside = true;
                        break;
                    }
                }

                if (!inside)
                {
                    int idx = rowOffset + x * 4;
                    pixels[idx] = 255;
                    pixels[idx + 1] = 255;
                    pixels[idx + 2] = 255;
                    pixels[idx + 3] = 255;
                }
            }
        }

        var maskedBmp = BitmapSource.Create(cropW, cropH, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        maskedBmp.Freeze();

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(maskedBmp));
        using (var fs = File.Create(Path.Combine(scratchDir, "scanline_masked_q20.png")))
        {
            enc.Save(fs);
        }

        string text = await ocr.RecognizeTextFromBitmapAsync(maskedBmp);
        File.WriteAllText(Path.Combine(scratchDir, "scanline_ocr_text.txt"), text);

        Assert.NotEmpty(text);
        Assert.DoesNotContain("nitrogen", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flushed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Corrosion", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractTextWithOcr_NcErtPage16_Question20_PolygonTests()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");

        if (!File.Exists(ncertPath)) return;

        var docInfo = new PdfDocumentInfo
        {
            FileName = "jesc101.pdf",
            FilePath = ncertPath,
            PageCount = 16,
            PageSizes = [new PdfPageSize(612, 820.8)]
        };
        var tabVm = new DocumentTabViewModel(docInfo, _pdfService);

        // Polygon strictly circling question 20 (with parts a and b):
        // Question 19 is at Y=0.366 to 0.372.
        // Question 20 starts at Y=0.380 and ends around Y=0.435.
        var polygon = new List<NormalizedPoint>
        {
            new(0.08, 0.380),
            new(0.85, 0.380),
            new(0.85, 0.438),
            new(0.08, 0.438)
        };
        var normRect = new NormalizedRect(0.08, 0.380, 0.77, 0.058);

        var ocr = new WindowsOcrService();
        if (!ocr.IsSupported) return;

        var rendered = await _pdfService.RenderPageAsync(ncertPath, 16, 200, 0);
        var pageWords = await _pdfService.GetPageWordsAsync(ncertPath, 16);
        var q1920 = pageWords.Where(w => w.Bounds.Y >= 0.35 && w.Bounds.Y <= 0.43).ToList();
        var q1920Debug = string.Join("\n", q1920.Select(w => $"'{w.Text}' Y={w.Bounds.Y:F4} H={w.Bounds.Height:F4} CenterY={w.Bounds.Y + w.Bounds.Height*0.5:F4} Inside={DocumentTabViewModel.IsPointInPolygon(new NormalizedPoint(w.Bounds.X + w.Bounds.Width*0.5, w.Bounds.Y + w.Bounds.Height*0.5), polygon)}"));
        string text = await tabVm.ExtractTextWithOcrAsync(16, normRect, ocr, rendered, polygon);

        string scratchDir = @"C:\Users\SURYA\.gemini\antigravity\brain\d218edce-7961-4978-918b-55ddf267c5d1\scratch";
        Directory.CreateDirectory(scratchDir);
        File.WriteAllText(Path.Combine(scratchDir, "ocr_output.txt"), text);
        File.WriteAllText(Path.Combine(scratchDir, "q1920_words.txt"), q1920Debug);

        Assert.NotEmpty(text);
        Assert.DoesNotContain("nitrogen", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flushed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food items", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Explain the following terms", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Corrosion", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rancidity", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractTextWithOcr_UserTouchCircle_GrazingQuestion19_DropsQuestion19()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");

        if (!File.Exists(ncertPath)) return;

        var docInfo = new PdfDocumentInfo
        {
            FileName = "jesc101.pdf",
            FilePath = ncertPath,
            PageCount = 16,
            PageSizes = [new PdfPageSize(612, 820.8)]
        };
        var tabVm = new DocumentTabViewModel(docInfo, _pdfService);

        // User's freehand touch circle around Question 20 whose top arc grazes into Line 19 (Y=0.365):
        var poly = new List<NormalizedPoint>
        {
            new(0.14, 0.368),
            new(0.35, 0.365),
            new(0.60, 0.368),
            new(0.65, 0.390),
            new(0.60, 0.415),
            new(0.35, 0.418),
            new(0.14, 0.415),
            new(0.12, 0.390),
            new(0.14, 0.368)
        };
        var normRect = new NormalizedRect(0.12, 0.365, 0.53, 0.053);

        var ocr = new WindowsOcrService();
        if (!ocr.IsSupported) return;

        var rendered = await _pdfService.RenderPageAsync(ncertPath, 16, 200, 0);
        string text = await tabVm.ExtractTextWithOcrAsync(16, normRect, ocr, rendered, poly);

        Assert.NotEmpty(text);
        Assert.DoesNotContain("nitrogen", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flushed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food items", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terms", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Corrosion", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractTextWithOcr_NcErtPage16_Question12_TwoLinesOcrTest()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");

        if (!File.Exists(ncertPath)) return;

        var docInfo = new PdfDocumentInfo
        {
            FileName = "jesc101.pdf",
            FilePath = ncertPath,
            PageCount = 16,
            PageSizes = [new PdfPageSize(576, 784.8)]
        };
        var tabVm = new DocumentTabViewModel(docInfo, _pdfService);

        // Circled around Question 12 (both lines: Y from ~0.110 to 0.155)
        // Line 1: "12. Write one equation each for decomposition reactions where energy is supplied in" (Y ~ 0.122)
        // Line 2: "the form of heat, light or electricity." (Y ~ 0.139)
        var poly = new List<NormalizedPoint>
        {
            new(0.06, 0.110),
            new(0.50, 0.108),
            new(0.92, 0.110),
            new(0.94, 0.132),
            new(0.92, 0.155),
            new(0.50, 0.157),
            new(0.06, 0.155),
            new(0.04, 0.132),
            new(0.06, 0.110)
        };
        var normRect = new NormalizedRect(0.04, 0.108, 0.90, 0.049);

        var ocr = new WindowsOcrService();
        if (!ocr.IsSupported) return;

        var rendered = await _pdfService.RenderPageAsync(ncertPath, 16, 200, 0);
        string text = await tabVm.ExtractTextWithOcrAsync(16, normRect, ocr, rendered, poly);

        Assert.Contains("Write one equation each for decomposition", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("energy is supplied in", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("heat, light or electricity", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("displacement", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractTextInPolygon_TwoLineQuestion12_ExtractsBothLinesCompletely()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");

        if (!File.Exists(ncertPath)) return;

        var docInfo = new PdfDocumentInfo
        {
            FileName = "jesc101.pdf",
            FilePath = ncertPath,
            PageCount = 16,
            PageSizes = [new PdfPageSize(612, 820.8)]
        };
        var tabVm = new DocumentTabViewModel(docInfo, _pdfService);

        // Polygon enclosing Question 12 (Y is approx 0.118 to 0.150):
        // Elliptical circle surrounding Question 12
        var poly = new List<NormalizedPoint>
        {
            new(0.10, 0.115),
            new(0.50, 0.113),
            new(0.88, 0.115),
            new(0.90, 0.133),
            new(0.88, 0.152),
            new(0.50, 0.155),
            new(0.10, 0.152),
            new(0.08, 0.133),
            new(0.10, 0.115)
        };

        var (extracted, rects) = await tabVm.ExtractTextInPolygonWithRectsAsync(16, poly);

        Assert.Contains("Write one equation each for decomposition", extracted);
        Assert.Contains("form of heat, light or electricity", extracted);
        // Ensure Question 13 below is NOT included
        Assert.DoesNotContain("displacement", extracted);
    }

    [Fact]
    public async Task WinOcr_Limits_Test()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");
        if (!File.Exists(ncertPath)) return;

        var ocr = new WindowsOcrService();
        if (!ocr.IsSupported) return;

        var rendered = await _pdfService.RenderPageAsync(ncertPath, 16, 200, 0);

        // 1. Test Question 12 (two lines) with user's circle:
        // The circle covers Question 12, but user's right curve is slightly lazy and doesn't reach the extreme margin:
        var polyQ12 = new List<NormalizedPoint>
        {
            new(0.06, 0.110),
            new(0.65, 0.110),
            new(0.70, 0.130),
            new(0.65, 0.155),
            new(0.06, 0.155),
            new(0.04, 0.130),
            new(0.06, 0.110)
        };
        var normRect12 = new NormalizedRect(0.04, 0.110, 0.66, 0.045);

        // 2. Test Question 20 where circle grazes Question 19 above:
        // Top of circle enters Question 19 at X=0.20 to 0.70, bottom covers (a) Corrosion:
        var polyQ20 = new List<NormalizedPoint>
        {
            new(0.20, 0.355), // in Q19
            new(0.75, 0.355), // in Q19
            new(0.80, 0.380),
            new(0.75, 0.420), // under Corrosion
            new(0.15, 0.420), // under Corrosion
            new(0.12, 0.380), // beside Q20
            new(0.20, 0.355)
        };
        var normRect20 = new NormalizedRect(0.12, 0.355, 0.68, 0.065);

        var wordsPage16 = await _pdfService.GetPageWordsAsync(ncertPath, 16);

        string q12Smart = DocumentTabViewModel.SmartFilterWordsInPolygon(wordsPage16, polyQ12, normRect12);
        string q20Smart = DocumentTabViewModel.SmartFilterWordsInPolygon(wordsPage16, polyQ20, normRect20);

        var q12DebugWords = wordsPage16.Where(w => w.Bounds.Y >= 0.10 && w.Bounds.Y <= 0.15).ToList();
        string scratchDir = @"C:\Users\SURYA\.gemini\antigravity\brain\d218edce-7961-4978-918b-55ddf267c5d1\scratch";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Q12 DEBUG WORDS ===");
        foreach (var w in q12DebugWords)
        {
            sb.AppendLine($"'{w.Text}' X={w.Bounds.X:F3} Y={w.Bounds.Y:F3} W={w.Bounds.Width:F3} H={w.Bounds.Height:F3}");
        }
        sb.AppendLine("\n=== Q12 SMART RESULT ===");
        sb.AppendLine(q12Smart);
        sb.AppendLine("\n=== Q20 SMART RESULT ===");
        sb.AppendLine(q20Smart);
        File.WriteAllText(Path.Combine(scratchDir, "smart_filter_results.txt"), sb.ToString());

        // Assert Q12 has both lines completely
        Assert.Contains("decomposition", q12Smart);
        Assert.Contains("reactions where energy is supplied in", q12Smart);
        Assert.Contains("the form of heat, light or electricity", q12Smart);

        // Assert Q20 has Question 20 and Corrosion, but NOT nitrogen/flushed from Question 19
        Assert.DoesNotContain("nitrogen", q20Smart, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flushed", q20Smart, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("food items", q20Smart, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Explain the following terms", q20Smart);
        Assert.Contains("Corrosion", q20Smart);
    }

    [Fact]
    public async Task ScanlineMaskedBitmap_TouchGrazingCircle_RemovesNitrogenPixels()
    {
        string ncertPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StudySuit.PdfReader", "library", "ncert", "jesc1", "jesc101.pdf");
        if (!File.Exists(ncertPath)) return;

        var ocr = new WindowsOcrService();
        if (!ocr.IsSupported) return;

        var bmp = await _pdfService.RenderPageAsync(ncertPath, 16, 200, 0);

        var poly = new List<NormalizedPoint>
        {
            new(0.14, 0.368),
            new(0.35, 0.365),
            new(0.60, 0.368),
            new(0.65, 0.390),
            new(0.60, 0.415),
            new(0.35, 0.418),
            new(0.14, 0.415),
            new(0.12, 0.390),
            new(0.14, 0.368)
        };

        var maskedBmp = DocumentTabViewModel.CreateScanlineMaskedBitmap(bmp, poly, out int px, out int py);
        string ocrResult = await ocr.RecognizeTextFromBitmapAsync(maskedBmp);

        Assert.NotEmpty(ocrResult);
        Assert.DoesNotContain("nitrogen", ocrResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flushed", ocrResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkdownRenderer_RendersCardsBlock_FlexibleSchemas()
    {
        Exception? threadEx = null;
        var sta = new Thread(() =>
        {
            try
            {
                // 1. Standard cards spec
                string md1 = "```cards\n{\"title\":\"Features\",\"columns\":2,\"cards\":[{\"title\":\"Card 1\",\"badge\":\"A\",\"items\":[\"Point 1\",\"Point 2\"]}]}\n```";
                var doc1 = MarkdownFlowRenderer.RenderToFlowDocument(md1, false);
                Assert.NotNull(doc1);
                Assert.NotEmpty(doc1.Blocks);

                // 2. Singular ```card block
                string md2 = "```card\n{\"title\":\"Single Card\",\"badge\":\"Note\",\"body\":\"This is a single card description.\"}\n```";
                var doc2 = MarkdownFlowRenderer.RenderToFlowDocument(md2, false);
                Assert.NotNull(doc2);
                Assert.NotEmpty(doc2.Blocks);

                // 3. Root array of cards [ {...}, {...} ]
                string md3 = "```cards\n[{\"title\":\"Alpha\",\"items\":[\"Item 1\"]},{\"title\":\"Beta\",\"items\":[\"Item 2\"]}]\n```";
                var doc3 = MarkdownFlowRenderer.RenderToFlowDocument(md3, false);
                Assert.NotNull(doc3);
                Assert.NotEmpty(doc3.Blocks);

                // 4. Auto-detect inside ```json block
                string md4 = "```json\n{\"cards\":[{\"title\":\"Detected Card\",\"badge\":\"Smart\",\"items\":[\"Auto-detected in json block\"]}]}\n```";
                var doc4 = MarkdownFlowRenderer.RenderToFlowDocument(md4, false);
                Assert.NotNull(doc4);
                Assert.NotEmpty(doc4.Blocks);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
    }

    [Fact]
    public void MarkdownRenderer_RendersChartBlock_FlexibleDataAndAliases()
    {
        Exception? threadEx = null;
        var sta = new Thread(() =>
        {
            try
            {
                // 1. Bar chart with string numbers ("45.5", "80%")
                string md1 = "```chart\n{\"type\":\"bar\",\"title\":\"Test Chart\",\"data\":[{\"label\":\"A\",\"value\":\"45.5\"},{\"label\":\"B\",\"value\":\"80%\"}]}\n```";
                var doc1 = MarkdownFlowRenderer.RenderToFlowDocument(md1, false);
                Assert.NotNull(doc1);
                Assert.NotEmpty(doc1.Blocks);

                // 2. Key-value dictionary data: { "Gas": 78 }
                string md2 = "```chart\n{\"type\":\"pie\",\"title\":\"Atmosphere\",\"data\":{\"Nitrogen\":78,\"Oxygen\":21,\"Other\":1}}\n```";
                var doc2 = MarkdownFlowRenderer.RenderToFlowDocument(md2, false);
                Assert.NotNull(doc2);
                Assert.NotEmpty(doc2.Blocks);

                // 3. Alias: ```bar-chart
                string md3 = "```bar-chart\n{\"title\":\"Alias Chart\",\"data\":[{\"label\":\"X\",\"value\":10},{\"label\":\"Y\",\"value\":20}]}\n```";
                var doc3 = MarkdownFlowRenderer.RenderToFlowDocument(md3, false);
                Assert.NotNull(doc3);
                Assert.NotEmpty(doc3.Blocks);

                // 4. Auto-detect chart inside ```json block
                string md4 = "```json\n{\"type\":\"line\",\"title\":\"Line Graph\",\"data\":[{\"x\":1,\"y\":10},{\"x\":2,\"y\":25}]}\n```";
                var doc4 = MarkdownFlowRenderer.RenderToFlowDocument(md4, false);
                Assert.NotNull(doc4);
                Assert.NotEmpty(doc4.Blocks);

                // 5. Mathematical function curve
                string md5 = "```chart\n{\"type\":\"function\",\"title\":\"Cosine\",\"expression\":\"cos(x)\",\"xMin\":-3.14,\"xMax\":3.14}\n```";
                var doc5 = MarkdownFlowRenderer.RenderToFlowDocument(md5, false);
                Assert.NotNull(doc5);
                Assert.NotEmpty(doc5.Blocks);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
    }

    [Fact]
    public void MarkdownFlowRenderer_RenderCards_FromUserChat()
    {
        Exception? threadEx = null;
        bool isCardBlock = false;
        string? failureReason = null;
        var sta = new Thread(() =>
        {
            try
            {
                string jsonFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudySuit.PdfReader", "chat_history", "2086e14dc7c841169779122fb1d33d08.json");
                if (File.Exists(jsonFile))
                {
                    using var docJson = JsonDocument.Parse(File.ReadAllText(jsonFile));
                    string content = docJson.RootElement.GetProperty("Messages")[1].GetProperty("Content").GetString()!;

                    var doc = MarkdownFlowRenderer.RenderToFlowDocument(content, false);
                    var codeContainers = doc.Blocks.OfType<BlockUIContainer>()
                        .Where(bui => bui.Child is Border b && b.Child is StackPanel s && s.Children.OfType<TextBlock>().Any(tb => tb.Text == "CARDS"))
                        .ToList();
                    if (codeContainers.Count > 0)
                    {
                        failureReason = "Found BlockUIContainer with CARDS fallback in rendered full message!";
                    }
                    else
                    {
                        isCardBlock = true;
                    }
                }
                else
                {
                    isCardBlock = true;
                }

                // Also test malformed cards with extra closing braces, smart quotes, and unescaped newlines:
                string malformedCards = "```cards\n{“title”: “Malformed Cards Test”, “columns”: 2, “cards”: [{“title”: “Card 1”, “badge”: “Important”, “items”: [“Item 1”, “Item 2”,]}, {“title”: “Card 2”, “badge”: “Note”, “items”: [“Item 3”, “Item 4”]}]}}\n```";
                var malformedDoc = MarkdownFlowRenderer.RenderToFlowDocument(malformedCards, false);
                var fallbackContainers = malformedDoc.Blocks.OfType<BlockUIContainer>()
                    .Where(bui => bui.Child is Border b && b.Child is StackPanel s && s.Children.OfType<TextBlock>().Any(tb => tb.Text == "CARDS"))
                    .ToList();
                if (fallbackContainers.Count > 0)
                {
                    failureReason = "Malformed cards with extra braces and smart quotes fell back to CARDS code block!";
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.Null(failureReason);
        Assert.True(isCardBlock);
    }

    [Fact]
    public async Task AiAssistantViewModel_UsePdfContextToggle_OmitsPdfContextWhenFalse()
    {
        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "ai_toggle_settings.json"));
        settingsService.Save(new AiAssistantSettings { Provider = AiProviderKind.OpenAi, ApiKey = "fake-key" });

        var handler = new StubHttpMessageHandler(req =>
        {
            string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"General knowledge answer.\"}}]}\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            };
        });

        var chatService = new AiChatService(new HttpClient(handler));
        using var aiVm = new AiAssistantViewModel(
            settingsService,
            chatService,
            new StubWebResearchService([]),
            chatHistoryService: new AiChatHistoryService(Path.Combine(_tempDir, "hist_toggle")))
        {
            UsePdfContext = false,
            GetActivePdfContextAsync = () => Task.FromResult<string?>("PDF Page 10 Content")
        };

        aiVm.PromptText = "Hello";
        await aiVm.SendMessageAsync();

        Assert.Equal(2, aiVm.Messages.Count);
        Assert.False(aiVm.Messages[1].UsedPdfContext);
    }

    [Fact]
    public async Task TestImageDownload_Freepik()
    {
        string url = "https://img.freepik.com/premium-photo/portrait-dog-sitting-field_1048944-15320681.jpg";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Ask for jpeg / png:
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/gif,*/*;q=0.5");
        
        using var resp = await client.GetAsync(url);
        byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        Assert.True(bmp.PixelWidth > 0);
        Assert.True(bmp.PixelHeight > 0);
    }

    [Fact]
    public async Task TestImageSearch_WebResearchService_ReturnsValidUrls()
    {
        var service = new WebResearchService();
        var settings = new AiAssistantSettings();
        var results = await service.SearchImagesAsync("Taj Mahal", settings);
        Assert.NotEmpty(results);
        Assert.StartsWith("http", results[0].ImageUrl, StringComparison.OrdinalIgnoreCase);

        // Test downloading the image with MarkdownFlowRenderer's method
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/gif,image/webp,*/*;q=0.5");
        using var resp = await client.GetAsync(results[0].ImageUrl);
        byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void AiChatService_SystemPrompt_EnforcesCardsAndCharts()
    {
        var settings = new AiAssistantSettings();
        string prompt = AiChatService.BuildEffectiveSystemPrompt(settings);

        Assert.Contains("[VISUAL CARDS & DATA CHARTS USAGE (MANDATORY)]", prompt);
        Assert.Contains("```cards", prompt);
        Assert.Contains("```chart", prompt);
        Assert.Contains("[CLEAN ACADEMIC TONE & EMOJI RESTRICTION (MANDATORY)]", prompt);
        Assert.Contains("DO NOT use emojis in headings", prompt);
        Assert.Contains("[INTERNET IMAGES & VISUAL REFERENCE INSTRUCTION (MANDATORY)]", prompt);
        Assert.Contains("MANDATORY VISUAL IMAGE EMBEDDING ON EVERY TOPIC/CONCEPT", prompt);
        Assert.Contains("Corrosion", prompt);
        Assert.Contains("Atmospheric Composition", prompt);
    }

    [Fact]
    public void MarkdownFlowRenderer_RendersImageBlockWithCardAndAltText()
    {
        Exception? threadEx = null;
        int blockCount = 0;
        bool cardIsCollapsedInitially = false;
        var sta = new Thread(() =>
        {
            try
            {
                string md = "## Summary\n\n![A Golden Retriever](https://example.com/dog.jpg)\n\nNice photo!";
                var doc = MarkdownFlowRenderer.RenderToFlowDocument(md, isDarkTheme: false);
                blockCount = doc.Blocks.Count;

                var imgContainer = doc.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
                if (imgContainer?.Child is Border outerBorder && outerBorder.Child is StackPanel sp)
                {
                    var card = sp.Children.OfType<Border>().FirstOrDefault();
                    if (card != null)
                    {
                        cardIsCollapsedInitially = card.Visibility == System.Windows.Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.True(blockCount >= 2);
        Assert.True(cardIsCollapsedInitially, "Image card must be collapsed initially so no flat empty border renders.");
    }

    [Fact]
    public void MarkdownFlowRenderer_BrokenOrUnclosedCards_RendersAsNativeCards_NotRawCodeBlock()
    {
        Exception? threadEx = null;
        bool hasCodeCardsFallback = true;
        var sta = new Thread(() =>
        {
            try
            {
                // Exact pattern from user chat history: missing closing '}' on the root object
                string brokenCardsJson = """
                ```cards
                {"title":"Key Functions of Media in Democracy","columns":2,"cards":[{"title":"Informing the Public","badge":"Watchdog","color":"#3B82F6","items":["Keeping people informed.","Providing citizens data."]},{"title":"Voice of the Masses","badge":"Advocate","color":"#10B981","items":["Serving as a platform.","Raising critical issues."]}]
                ```
                """;

                var doc = MarkdownFlowRenderer.RenderToFlowDocument(brokenCardsJson, isDarkTheme: false);
                hasCodeCardsFallback = doc.Blocks.OfType<BlockUIContainer>()
                    .Any(bui => bui.Child is Border b && b.Child is StackPanel s && s.Children.OfType<TextBlock>().Any(tb => tb.Text == "CARDS"));
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.False(hasCodeCardsFallback, "Broken or unclosed cards JSON must render into native cards grid, never a raw 'CARDS' code block.");
    }

    [Fact]
    public void MarkdownFlowRenderer_UserImage1_CardsBlock_RendersAsNativeCards()
    {
        Exception? threadEx = null;
        bool hasCodeBlock = true;
        var sta = new Thread(() =>
        {
            try
            {
                string md = """
                ```cards
                {"title":"Key Functions of Media in Democracy","columns":2,"cards":[{"title":"Informing the
                Public","badge":"Watchdog","color":"#3B82F6","items":["Keeping people informed about global and national developments.","Providing citizens with the necessary data to make informed voting choices."]},{"title":"Voice of the
                Masses","badge":"Advocate","color":"#10B981","items":["Serving as a platform for public opinion and grievances.","Raising critical issues concerning the public domain.","Amplifying marginalized voices and societal concerns."]},{"title":"Accountability
                Mechanism","badge":"Check & Balance","color":"#EF4444","items":["Monitoring government actions and policies.","Exposing corruption and misuse of power.","Contributing to the resolution of public issues through scrutiny."]},{"title":"Fostering
                Debate","badge":"Catalyst","color":"#F59E0B","items":["Creating a public forum for diverse viewpoints.","Encouraging critical thinking and intellectual discourse.","Facilitating the debate on socio-economic policies."]}]}
                ```
                """;

                var doc = MarkdownFlowRenderer.RenderToFlowDocument(md, isDarkTheme: false);
                hasCodeBlock = doc.Blocks.OfType<BlockUIContainer>()
                    .Any(bui => bui.Child is Border b && b.Child is StackPanel s && s.Children.OfType<TextBlock>().Any(tb => tb.Text == "CARDS"));
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.False(hasCodeBlock, "Exact user cards string with multi-line titles must render as native cards, not code block.");
    }

    [Fact]
    public void AiAssistantViewModel_TalkMode_ToggleAndSettingsSync()
    {
        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "ai_talkmode_settings.json"));
        using var vm = new AiAssistantViewModel(
            settingsService,
            new AiChatService(),
            new StubWebResearchService([]),
            new StubVoiceReadoutService());

        Assert.False(vm.IsTalkMode);
        Assert.False(vm.Settings.TalkMode);

        vm.ToggleTalkModeCommand.Execute(null);

        Assert.True(vm.IsTalkMode);
        Assert.True(vm.Settings.TalkMode);
        Assert.Contains("Talk Mode ON", vm.StatusText);

        vm.ToggleTalkModeCommand.Execute(null);

        Assert.False(vm.IsTalkMode);
        Assert.False(vm.Settings.TalkMode);
        Assert.Contains("Talk Mode OFF", vm.StatusText);
    }

    [Theory]
    [InlineData("gpt-4o-audio-preview", true)]
    [InlineData("gpt-4o-realtime-preview", true)]
    [InlineData("gemini-2.0-flash-exp-omni", true)]
    [InlineData("lfm3-omni-2b", true)]
    [InlineData("lfm3-audio-2b", true)]
    [InlineData("claude-3-5-sonnet", false)]
    [InlineData("llama3.2", false)]
    public void AiChatService_IsOmniAudioModel_CorrectlyIdentifiesAudioModels(string modelName, bool expected)
    {
        bool actual = AiChatService.IsOmniAudioModel(modelName);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AiProviderDefaults_IncludesLiquidAiPreset()
    {
        var preset = AiProviderDefaults.GetPreset(AiProviderKind.LiquidAi);
        Assert.NotNull(preset);
        Assert.Equal("https://api.liquid.ai/openai/v1", preset.DefaultBaseUrl);
        Assert.Equal("lfm3-omni-2b", preset.DefaultModel);
        Assert.True(preset.RequiresApiKey);
        Assert.Contains("Liquid AI", preset.DisplayName);
    }

    [Fact]
    public void VoiceReadoutService_WrapPcmInWav_GeneratesValidRiffWavHeader()
    {
        byte[] rawPcm = new byte[3200];
        byte[] wav = VoiceReadoutService.WrapPcmInWav(rawPcm, 16000, 1, 16);

        Assert.Equal(44 + 3200, wav.Length);
        Assert.Equal((byte)'R', wav[0]);
        Assert.Equal((byte)'I', wav[1]);
        Assert.Equal((byte)'F', wav[2]);
        Assert.Equal((byte)'F', wav[3]);
        Assert.Equal((byte)'W', wav[8]);
        Assert.Equal((byte)'A', wav[9]);
        Assert.Equal((byte)'V', wav[10]);
        Assert.Equal((byte)'E', wav[11]);
    }

    private sealed class StubAudioRecordingService : IAudioRecordingService
    {
        public bool IsRecording { get; private set; }
        public double CurrentAudioLevel { get; set; }
        public event Action<double>? AudioLevelChanged;
        public event Action<byte[]>? RecordingCompleted;
        public event Action<string>? ErrorOccurred;

        public bool StartRecording()
        {
            IsRecording = true;
            AudioLevelChanged?.Invoke(0.75);
            return true;
        }

        public byte[] StopRecording()
        {
            IsRecording = false;
            byte[] wav = VoiceReadoutService.WrapPcmInWav(new byte[6400], 16000, 1, 16);
            RecordingCompleted?.Invoke(wav);
            return wav;
        }

        public void CancelRecording() => IsRecording = false;
        public void Dispose() { }
    }

    [Fact]
    public async Task AiAssistantViewModel_OmniTalkMode_HoldToTalkAndStateTransitions()
    {
        var settingsService = new AiSettingsService(Path.Combine(_tempDir, "ai_omni_talk_settings.json"));
        settingsService.Save(new AiAssistantSettings { Provider = AiProviderKind.LiquidAi, ApiKey = "fake-key", ModelName = "lfm3-omni-2b" });

        var handler = new StubHttpMessageHandler(req =>
        {
            string respJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Hello there! I am LFM3 Omni.\",\"audio\":{\"data\":\"\",\"transcript\":\"Hello there!\"}}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respJson, Encoding.UTF8, "application/json")
            };
        });

        var stubRecorder = new StubAudioRecordingService();
        var chatService = new AiChatService(new HttpClient(handler));

        using var vm = new AiAssistantViewModel(
            settingsService,
            chatService,
            new StubWebResearchService([]),
            new StubVoiceReadoutService(),
            audioRecordingService: stubRecorder);

        Assert.False(vm.IsOmniTalkView);
        Assert.Equal("Ready", vm.OmniTalkState);

        // Switch to Omni Talk View
        vm.ToggleOmniTalkView();
        Assert.True(vm.IsOmniTalkView);
        Assert.True(vm.IsTalkMode);

        // Start hold to talk
        vm.StartHoldToTalk();
        Assert.True(vm.IsHoldToTalkPressed);
        Assert.Equal("Listening", vm.OmniTalkState);

        // Release hold to talk
        vm.ReleaseHoldToTalk();
        Assert.False(vm.IsHoldToTalkPressed);

        // Execute direct audio turn
        byte[] wav = stubRecorder.StopRecording();
        await vm.SendAudioTurnAsync(wav);

        Assert.Equal("Ready", vm.OmniTalkState);
        Assert.Equal("Hello there! I am LFM3 Omni.", vm.LatestSpokenTranscript);
    }

    [Fact]
    public void MarkdownFlowRenderer_LinkToImage_RendersAsImageBlock_NotJustTextHyperlink()
    {
        Exception? threadEx = null;
        int imageBlocks = 0;
        int inlineImageContainers = 0;
        var sta = new Thread(() =>
        {
            try
            {
                // Standalone link without exclamation mark
                string md1 = "[The Taj Mahal at sunrise](https://ts2.mm.bing.net/th?id=taj.jpg)\n\nParagraph with inline link [Dog](https://example.com/dog.png) right here.";
                var doc = MarkdownFlowRenderer.RenderToFlowDocument(md1, isDarkTheme: false);

                // Standalone image block
                imageBlocks = doc.Blocks.OfType<BlockUIContainer>()
                    .Count(b => b.Child is Border br && br.Child is StackPanel sp && sp.Children.OfType<Border>().Any());

                // Inline image in paragraph
                var para = doc.Blocks.OfType<Paragraph>().FirstOrDefault();
                if (para != null)
                {
                    inlineImageContainers = para.Inlines.OfType<InlineUIContainer>().Count();
                }
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(threadEx);
        Assert.True(imageBlocks >= 1, "Standalone [alt](image.jpg) must render as a full image block.");
        Assert.True(inlineImageContainers >= 1, "Inline [alt](image.png) must render as an inline image container.");
    }

    [Fact]
    public void WebResearchService_BlockedHostDetection_AndCdnUrlSafety()
    {
        Assert.True(WebResearchService.IsBlockedImageHost("https://img.freepik.com/premium-photo/portrait-dog.jpg"));
        Assert.True(WebResearchService.IsBlockedImageHost("https://www.alamy.com/image/cat.jpg"));
        Assert.True(WebResearchService.IsBlockedImageHost("https://www.shutterstock.com/image-photo/nature.jpg"));
        Assert.True(WebResearchService.IsBlockedImageHost("https://i.pinimg.com/736x/ab/cd.jpg"));
        Assert.False(WebResearchService.IsBlockedImageHost("https://ts2.mm.bing.net/th?id=123"));
        Assert.False(WebResearchService.IsBlockedImageHost("https://upload.wikimedia.org/wikipedia/commons/test.jpg"));
        Assert.False(WebResearchService.IsBlockedImageHost("https://images.unsplash.com/photo-123"));
    }
}


