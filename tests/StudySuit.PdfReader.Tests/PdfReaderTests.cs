using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
        Assert.Equal("http://localhost:8080/v1/chat/completions", lastUrl);

        Assert.Equal("http://localhost:8080/v1", AiChatService.NormalizeBaseUrl("http://localhost:8080", AiProviderKind.LlamaCppLocal));
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

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService)
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

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService)
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

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService);
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

        using var aiVm = new AiAssistantViewModel(settingsService, new AiChatService(new HttpClient(handler)), new StubWebResearchService([]))
        {
            UsePdfContext = false,
            UseWebResearch = true
        };
        aiVm.UpdateSettings(new AiAssistantSettings
        {
            Provider = AiProviderKind.LlamaCppLocal,
            BaseUrl = "http://localhost:8080/v1",
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
            stubVoice);

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

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService)
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
        Assert.Single(aiVm.Messages); // only welcome message
        Assert.True(aiVm.Messages[0].IsWelcome);
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

        using var aiVm = new AiAssistantViewModel(settingsService, chatService, webService, stubVoiceService);
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

    private sealed class StubWebResearchService(IReadOnlyList<WebSourceReference> sources) : IWebResearchService
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<WebSourceReference>> SearchAsync(string query, AiAssistantSettings settings, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(sources);
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

        // Ensure NcertLibraryRoot has solid ApplicationBackgroundBrush
        Assert.Matches(@"<Grid\s+x:Name=""NcertLibraryRoot""[^>]*Background=""{DynamicResource ApplicationBackgroundBrush}""", xaml);

        // Ensure HomeDashboardRoot has solid ApplicationBackgroundBrush
        Assert.Matches(@"<Grid\s+x:Name=""HomeDashboardRoot""[^>]*Background=""{DynamicResource ApplicationBackgroundBrush}""", xaml);

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
}

