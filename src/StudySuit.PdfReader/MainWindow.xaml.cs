using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using StudySuit.PdfReader.Controls;
using StudySuit.PdfReader.Models;
using StudySuit.PdfReader.Services;
using StudySuit.PdfReader.ViewModels;
using StudySuit.PdfReader.Views;
using Wpf.Ui.Controls;

namespace StudySuit.PdfReader;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _noteAutoSaveTimer;
    private ScrollViewer? _pageScrollViewer;
    private SmoothScrollController? _pdfSmoothScroller;
    private SmoothScrollController? _aiChatSmoothScroller;
    private SmoothScrollController? _feedSmoothScroller;
    private DocumentTabViewModel? _wiredTab;
    private StudyNoteEntry? _loadedNote;
    private bool _isLoadingNoteDocument;
    private bool _isProgrammaticScroll;
    private bool _isUpdatingZoomSlider;

    // Interactive drawing / word-brush / selection state
    private Grid? _activeSurface;
    private PdfPageViewModel? _activeSurfacePage;
    private Point _dragStartPoint;
    private Point _lastBrushPoint;
    private Point _latestDragPoint;
    private AnnotationTool _activeDragTool;
    private bool _isPointerDragging;
    private bool _isWordPreviewBusy;
    private readonly List<Point> _inkCapturePoints = [];
    private readonly HashSet<int> _paintedWordIndices = [];
    private readonly IOcrService _ocrService = new WindowsOcrService();

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        AdjustWindowToDisplayViewport();

        _noteAutoSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _noteAutoSaveTimer.Tick += (_, _) =>
        {
            _noteAutoSaveTimer.Stop();
            SaveCurrentNoteDocument();
        };

        _viewModel.PromptForPasswordAsync = PromptForPasswordAsync;
        _viewModel.ShowMessageAction = ShowInfoMessage;
        _viewModel.ReminderTriggered += () => SystemSounds.Asterisk.Play();
        _viewModel.OpenAiSettingsRequested += OpenAiSettingsDialog;
        _viewModel.AiAssistant.MessageAdded += () =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => AiChatScrollViewer.ScrollToEnd());
        };
        _viewModel.AiAssistant.InsertIntoNotesRequested += AppendAiTextToActiveNote;
        _viewModel.FocusSearchInputAction = () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                FloatingSearchBox.Focus();
                FloatingSearchBox.SelectAll();
            });
        };

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Notes.SelectedNoteSwitched += Notes_SelectedNoteSwitched;
        _viewModel.Notes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NotesViewModel.SelectedInkColorHex) or
                nameof(NotesViewModel.IsSketchHighlighter) or
                nameof(NotesViewModel.IsSketchEraser) or
                nameof(NotesViewModel.SketchStrokeWidth))
            {
                UpdateSketchCanvasAttributes();
            }
        };

        Loaded += MainWindow_Loaded;
        Closing += (_, _) =>
        {
            SaveCurrentNoteDocument();
            _pdfSmoothScroller?.Dispose();
            _aiChatSmoothScroller?.Dispose();
            _feedSmoothScroller?.Dispose();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AdjustWindowToDisplayViewport();
    }

    private void AdjustWindowToDisplayViewport()
    {
        try
        {
            double workLeft = 0;
            double workTop = 0;
            double workWidth = SystemParameters.WorkArea.Width;
            double workHeight = SystemParameters.WorkArea.Height;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                var hMonitor = MonitorFromWindow(helper.Handle, MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        var dpi = VisualTreeHelper.GetDpi(this);
                        double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                        double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

                        workLeft = mi.rcWork.left / dpiX;
                        workTop = mi.rcWork.top / dpiY;
                        workWidth = (mi.rcWork.right - mi.rcWork.left) / dpiX;
                        workHeight = (mi.rcWork.bottom - mi.rcWork.top) / dpiY;
                    }
                }
            }

            if (workWidth <= 0 || workHeight <= 0)
            {
                workWidth = SystemParameters.WorkArea.Width > 0 ? SystemParameters.WorkArea.Width : 1280;
                workHeight = SystemParameters.WorkArea.Height > 0 ? SystemParameters.WorkArea.Height : 720;
            }

            // Always ensure the normal (restored) window size comfortably fits the screen with margins
            double targetWidth = Math.Clamp(workWidth * 0.90, 540, 1280);
            double targetHeight = Math.Clamp(workHeight * 0.88, 460, 800);

            // Never let normal size exceed the work area (leave ample margin for dragging and system chrome)
            targetWidth = Math.Min(targetWidth, Math.Max(540, workWidth - 40));
            targetHeight = Math.Min(targetHeight, Math.Max(460, workHeight - 40));

            Width = targetWidth;
            Height = targetHeight;
            Left = workLeft + Math.Max(0, (workWidth - targetWidth) / 2.0);
            Top = workTop + Math.Max(0, (workHeight - targetHeight) / 2.0);
        }
        catch
        {
            // Graceful fallback to SystemParameters.WorkArea
            double fallbackW = SystemParameters.WorkArea.Width;
            double fallbackH = SystemParameters.WorkArea.Height;
            if (fallbackW > 0 && fallbackH > 0)
            {
                Width = Math.Clamp(fallbackW * 0.90, 540, Math.Max(540, fallbackW - 40));
                Height = Math.Clamp(fallbackH * 0.88, 460, Math.Max(460, fallbackH - 40));
                Left = Math.Max(0, (fallbackW - Width) / 2.0);
                Top = Math.Max(0, (fallbackH - Height) / 2.0);
            }
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    public void AnimateThemeTransition()
    {
        var fade = new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        RootLayoutGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void OpenAiSettingsDialog()
    {
        var dialog = new AiSettingsDialog(
            _viewModel.AiAssistant.Settings,
            _viewModel.AiChatService,
            _viewModel.VoiceReadoutService,
            _viewModel.LlamaAutoSetupService)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.AiAssistant.UpdateSettings(dialog.UpdatedSettings);
        }
    }

    private void AppendAiTextToActiveNote(string text)
    {
        if (NoteRichEditor.Document is null)
        {
            NoteRichEditor.Document = new FlowDocument();
        }

        NoteRichEditor.Document.Blocks.Add(new Paragraph(new Run(text))
        {
            Margin = new Thickness(0, 6, 0, 6)
        });
        SaveCurrentNoteDocument();
        _viewModel.GlobalStatusText = $"Appended AI note to \"{_viewModel.Notes.SelectedNote?.Title}\"";
    }

    private void AiPromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            e.Handled = true;
            if (_viewModel.AiAssistant.SendMessageCommand.CanExecute(null))
            {
                _viewModel.AiAssistant.SendMessageCommand.Execute(null);
            }
        }
    }

    private void AiChatItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AiChatScrollViewer is null || e.HeightChanged == false)
        {
            return;
        }

        // Keep the latest streamed response scrolled into view when the user is near the bottom
        if (_viewModel.AiAssistant.IsBusy &&
            AiChatScrollViewer.VerticalOffset >= Math.Max(0, AiChatScrollViewer.ScrollableHeight - 80))
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => AiChatScrollViewer.ScrollToEnd());
        }
    }

    private void OpenWebSourceUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe &&
            fe.Tag is string url &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }

    private void AnimatedSurface_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element || e.NewValue is not true)
        {
            return;
        }

        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, fade);

        if (element.RenderTransform is TranslateTransform tt)
        {
            double startY = ReferenceEquals(element, FloatingSearchBorder) || ReferenceEquals(element, ReminderBannerBorder)
                ? -10.0
                : 10.0;
            var slide = new DoubleAnimation(startY, 0.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            tt.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    private void EnsurePageScrollViewer()
    {
        if (_pageScrollViewer is not null && _pdfSmoothScroller is not null)
        {
            return;
        }

        _pageScrollViewer = FindVisualChild<ScrollViewer>(PdfPageList);
        if (_pageScrollViewer is not null)
        {
            _pageScrollViewer.ScrollChanged -= PageScrollViewer_ScrollChanged;
            _pageScrollViewer.ScrollChanged += PageScrollViewer_ScrollChanged;
            _pdfSmoothScroller?.Dispose();
            _pdfSmoothScroller = new SmoothScrollController(_pageScrollViewer);
        }
    }

    private void PdfPageList_Loaded(object sender, RoutedEventArgs e)
    {
        EnsurePageScrollViewer();
    }

    private void AbortActivePointerInteraction()
    {
        if (_isPointerDragging)
        {
            _isPointerDragging = false;
            if (_activeSurface is not null)
            {
                HideAllPreviewShapes(_activeSurface);
                _activeSurface.ReleaseAllTouchCaptures();
                _activeSurface.ReleaseMouseCapture();
                _activeSurface.ReleaseStylusCapture();
            }
            _activeSurface = null;
            _activeSurfacePage?.ClearLiveWordSelection();
            _activeSurfacePage = null;
            _inkCapturePoints.Clear();
            _paintedWordIndices.Clear();
        }
    }

    private void PdfPageList_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
    {
        EnsurePageScrollViewer();
        e.ManipulationContainer = _pageScrollViewer ?? (IInputElement)sender;
        e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
        e.Handled = true;
    }

    private void PdfPageList_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
        if (_wiredTab is null) return;

        EnsurePageScrollViewer();

        // If more than 1 finger is active and an annotation tool was dragging, abort the single-finger stroke
        if (_isPointerDragging && (e.Manipulators.Count() >= 2 || Math.Abs(e.DeltaManipulation.Scale.Length - Math.Sqrt(2)) > 0.01))
        {
            AbortActivePointerInteraction();
        }

        // 1. Pinch-to-zoom (Scaling):
        double scaleFactor = (e.DeltaManipulation.Scale.X + e.DeltaManipulation.Scale.Y) / 2.0;
        if (Math.Abs(scaleFactor - 1.0) > 0.002)
        {
            double currentZoom = _wiredTab.ZoomPercent;
            double newZoom = Math.Clamp(currentZoom * scaleFactor, 25.0, 500.0);

            if (Math.Abs(newZoom - currentZoom) >= 0.25)
            {
                Point originInViewer = e.ManipulationOrigin;
                double oldHOffset = _pageScrollViewer?.HorizontalOffset ?? 0;
                double oldVOffset = _pageScrollViewer?.VerticalOffset ?? 0;

                _wiredTab.SetCustomZoom(newZoom);

                if (_pageScrollViewer is not null)
                {
                    double zoomRatio = newZoom / currentZoom;
                    double newHOffset = (oldHOffset + originInViewer.X) * zoomRatio - originInViewer.X;
                    double newVOffset = (oldVOffset + originInViewer.Y) * zoomRatio - originInViewer.Y;

                    _pageScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newHOffset));
                    _pageScrollViewer.ScrollToVerticalOffset(Math.Max(0, newVOffset));
                }
            }
            e.Handled = true;
            return;
        }

        // 2. Touch Pan (Translation):
        if (_pageScrollViewer is not null && (Math.Abs(e.DeltaManipulation.Translation.X) > 0.5 || Math.Abs(e.DeltaManipulation.Translation.Y) > 0.5))
        {
            _pageScrollViewer.ScrollToHorizontalOffset(_pageScrollViewer.HorizontalOffset - e.DeltaManipulation.Translation.X);
            _pageScrollViewer.ScrollToVerticalOffset(_pageScrollViewer.VerticalOffset - e.DeltaManipulation.Translation.Y);
            e.Handled = true;
        }
    }

    private void PdfPageList_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
    {
        e.TranslationBehavior.DesiredDeceleration = 0.002;
        e.ExpansionBehavior.DesiredDeceleration = 0.001;
        e.Handled = true;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EnsurePageScrollViewer();

        _aiChatSmoothScroller = new SmoothScrollController(AiChatScrollViewer);
        if (NewsFeedScrollViewer is not null)
        {
            _feedSmoothScroller = new SmoothScrollController(NewsFeedScrollViewer);
        }

        WireSelectedTab(_viewModel.SelectedTab);
        LoadNoteIntoEditor(_viewModel.Notes.SelectedNote);
        UpdateSketchCanvasAttributes();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            WireSelectedTab(_viewModel.SelectedTab);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsFullScreen))
        {
            WindowState = _viewModel.IsFullScreen ? WindowState.Maximized : WindowState.Normal;
        }
        else if (e.PropertyName == nameof(MainViewModel.ShowNotesStudio) && !_viewModel.ShowNotesStudio)
        {
            SaveCurrentNoteDocument();
        }
    }

    // ==================== NOTE-TAKING STUDIO ====================
    private void Notes_SelectedNoteSwitched(StudyNoteEntry? nextNote)
    {
        SaveCurrentNoteDocument();
        LoadNoteIntoEditor(nextNote);
    }

    private void LoadNoteIntoEditor(StudyNoteEntry? note)
    {
        _isLoadingNoteDocument = true;
        try
        {
            _loadedNote = note;
            var doc = new FlowDocument();
            if (note is not null)
            {
                byte[]? pkg = _viewModel.Notes.LoadNotePackage(note.Id);
                if (pkg is { Length: > 0 })
                {
                    using var ms = new MemoryStream(pkg);
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    range.Load(ms, DataFormats.XamlPackage);
                }
            }

            NoteRichEditor.Document = doc;
        }
        catch
        {
            NoteRichEditor.Document = new FlowDocument();
        }
        finally
        {
            _isLoadingNoteDocument = false;
        }
    }

    private void SaveCurrentNoteDocument()
    {
        if (_isLoadingNoteDocument || _loadedNote is null || NoteRichEditor.Document is null)
        {
            return;
        }

        try
        {
            var range = new TextRange(NoteRichEditor.Document.ContentStart, NoteRichEditor.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            string plain = range.Text.Replace("\r", " ").Replace("\n", " ").Trim();
            _viewModel.Notes.SaveNoteContent(_loadedNote, ms.ToArray(), plain);
        }
        catch
        {
        }
    }

    private void NoteRichEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingNoteDocument || !IsLoaded)
        {
            return;
        }

        _noteAutoSaveTimer.Stop();
        _noteAutoSaveTimer.Start();
    }

    private void SaveNoteButton_Click(object sender, RoutedEventArgs e) => SaveCurrentNoteDocument();

    private void NoteTitleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveCurrentNoteDocument();
        _viewModel.Notes.SaveTitleChange();
    }

    private void NoteFontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || NoteFontBox.SelectedItem is not NoteFontOption fontOpt)
        {
            return;
        }

        var family = new FontFamily(fontOpt.FontFamilyName);
        if (!NoteRichEditor.Selection.IsEmpty)
        {
            NoteRichEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
        }
        else
        {
            NoteRichEditor.FontFamily = family;
        }

        NoteRichEditor.Focus();
    }

    private void NoteFontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || NoteFontSizeBox.SelectedItem is not double size)
        {
            return;
        }

        if (!NoteRichEditor.Selection.IsEmpty)
        {
            NoteRichEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        }
        else
        {
            NoteRichEditor.FontSize = size;
        }

        NoteRichEditor.Focus();
    }

    private void NoteTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string hex)
        {
            var brush = CreateBrushFromHex(hex);
            NoteRichEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            NoteRichEditor.Focus();
        }
    }

    private void InsertPictureInNote_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Insert Picture into Note",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(dialog.FileName, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();

                InsertBitmapIntoRichEditor(bmp, maxDisplayWidth: 520);
            }
            catch (Exception ex)
            {
                ShowInfoMessage("Insert Picture Failed", ex.Message);
            }
        }
    }

    private void InsertEquationInNote_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EquationEditorDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.RenderedEquation is { } eqBitmap)
        {
            InsertBitmapIntoRichEditor(eqBitmap, maxDisplayWidth: 540, tooltip: dialog.FormulaText);
        }
    }

    private void InsertChartInNote_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChartBuilderDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.RenderedChart is { } chartBitmap)
        {
            InsertBitmapIntoRichEditor(chartBitmap, maxDisplayWidth: 620, tooltip: "Study Chart / Graph");
        }
    }

    private void SketchToolOption_Click(object sender, RoutedEventArgs e) => UpdateSketchCanvasAttributes();

    private void UpdateSketchCanvasAttributes()
    {
        if (NoteSketchCanvas is null)
        {
            return;
        }

        if (_viewModel.Notes.IsSketchEraser)
        {
            NoteSketchCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            return;
        }

        NoteSketchCanvas.EditingMode = InkCanvasEditingMode.Ink;
        Color inkColor = Colors.RoyalBlue;
        try
        {
            inkColor = (Color)ColorConverter.ConvertFromString(_viewModel.Notes.SelectedInkColorHex);
        }
        catch
        {
        }

        NoteSketchCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = inkColor,
            Width = _viewModel.Notes.IsSketchHighlighter ? 16 : _viewModel.Notes.SketchStrokeWidth,
            Height = _viewModel.Notes.IsSketchHighlighter ? 16 : _viewModel.Notes.SketchStrokeWidth,
            IsHighlighter = _viewModel.Notes.IsSketchHighlighter,
            FitToCurve = true
        };
    }

    private void ClearSketchPad_Click(object sender, RoutedEventArgs e) => NoteSketchCanvas.Strokes.Clear();

    private void InsertSketchIntoNote_Click(object sender, RoutedEventArgs e)
    {
        if (NoteSketchCanvas.Strokes.Count == 0)
        {
            ShowInfoMessage("Empty Sketch", "Draw or handwrite something on the Sketch Pad before inserting.");
            return;
        }

        int w = Math.Max(300, (int)Math.Ceiling(NoteSketchCanvas.ActualWidth));
        int h = Math.Max(150, (int)Math.Ceiling(NoteSketchCanvas.ActualHeight));

        const double scale = 2.0;
        var rtb = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(NoteSketchCanvas);
        rtb.Freeze();

        InsertBitmapIntoRichEditor(rtb, maxDisplayWidth: 580, tooltip: "Handwriting Sketch");
        NoteSketchCanvas.Strokes.Clear();
    }

    private void InsertBitmapIntoRichEditor(BitmapSource bitmap, double maxDisplayWidth, string? tooltip = null)
    {
        double displayW = Math.Min(maxDisplayWidth, bitmap.Width);
        var image = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = displayW,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 6, 0, 6),
            ToolTip = tooltip
        };

        var container = new InlineUIContainer(image, NoteRichEditor.CaretPosition);
        NoteRichEditor.CaretPosition = container.ElementEnd;
        NoteRichEditor.Focus();
        SaveCurrentNoteDocument();
    }

    // ==================== PDF VIEWER & ANNOTATIONS ====================
    private void WireSelectedTab(DocumentTabViewModel? tab)
    {
        if (_wiredTab is not null)
        {
            _wiredTab.ScrollToPageRequested -= OnScrollToPageRequested;
            _wiredTab.PropertyChanged -= Tab_PropertyChanged;
        }

        _wiredTab = tab;

        if (_wiredTab is not null)
        {
            EnsurePageScrollViewer();
            _wiredTab.ScrollToPageRequested += OnScrollToPageRequested;
            _wiredTab.PropertyChanged += Tab_PropertyChanged;
            UpdateViewportMetrics();
            if (_wiredTab.CurrentPageNumber > 1)
            {
                OnScrollToPageRequested(_wiredTab.CurrentPageNumber);
            }
            else
            {
                _pageScrollViewer?.ScrollToTop();
            }
        }
    }

    private void Tab_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.ZoomPercent))
        {
            _isUpdatingZoomSlider = true;
            Dispatcher.BeginInvoke(() => _isUpdatingZoomSlider = false);
        }
    }

    private void DocumentTab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DocumentTabViewModel tab)
        {
            _viewModel.SelectTab(tab);
        }
    }

    private void NewTodoInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.AddTodoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void NewEventInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.AddStudyEventCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnScrollToPageRequested(int pageNumber)
    {
        if (_wiredTab is null || pageNumber < 1 || pageNumber > _wiredTab.Pages.Count)
        {
            return;
        }

        var pageVm = _wiredTab.Pages[pageNumber - 1];
        _pdfSmoothScroller?.SyncAndStop();

        if (pageNumber == 1 && _pageScrollViewer is not null)
        {
            _isProgrammaticScroll = true;
            try
            {
                _pageScrollViewer.ScrollToTop();
            }
            finally
            {
                _isProgrammaticScroll = false;
            }
            return;
        }

        _isProgrammaticScroll = true;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                PdfPageList.UpdateLayout();
                PdfPageList.ScrollIntoView(pageVm);
            }
            finally
            {
                _isProgrammaticScroll = false;
            }
        });
    }

    private void PageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll || _wiredTab is null || _wiredTab.ViewMode != PdfViewMode.Continuous)
        {
            return;
        }

        if (_pageScrollViewer is null || _wiredTab.VisiblePages.Count == 0)
        {
            return;
        }

        double viewportMidY = _pageScrollViewer.ActualHeight * 0.4;
        int bestPage = _wiredTab.CurrentPageNumber;
        double bestDistance = double.MaxValue;

        foreach (var page in _wiredTab.VisiblePages)
        {
            if (PdfPageList.ItemContainerGenerator.ContainerFromItem(page) is not FrameworkElement container)
            {
                continue;
            }

            Point topLeft = container.TranslatePoint(new Point(0, 0), _pageScrollViewer);
            double centerY = topLeft.Y + (container.ActualHeight / 2.0);
            double distance = Math.Abs(centerY - viewportMidY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPage = page.PageNumber;
            }
        }

        _wiredTab.SetVisiblePageFromScroll(bestPage);
    }

    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        EnsurePageScrollViewer();
        UpdateViewportMetrics();
    }

    private void UpdateViewportMetrics()
    {
        if (_wiredTab is null || PdfPageList.ActualWidth <= 0 || PdfPageList.ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        _wiredTab.UpdateViewportMetrics(PdfPageList.ActualWidth, PdfPageList.ActualHeight, dpi.DpiScaleX);
    }

    private void PdfPageList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_wiredTab is null)
        {
            return;
        }

        EnsurePageScrollViewer();

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            _pdfSmoothScroller?.SyncAndStop();
            double delta = e.Delta > 0 ? 12.0 : -12.0;
            _wiredTab.SetCustomZoom(_wiredTab.ZoomPercent + delta);
            return;
        }

        bool horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        _pdfSmoothScroller?.HandleMouseWheel(e, horizontal, wheelMultiplier: 0.95);
    }

    private void AiChatScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _aiChatSmoothScroller?.HandleMouseWheel(e, horizontal: false, wheelMultiplier: 0.75);
    }

    private void NewsFeedScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _feedSmoothScroller?.HandleMouseWheel(e, horizontal: false, wheelMultiplier: 0.90);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingZoomSlider || _wiredTab is null || !IsLoaded)
        {
            return;
        }

        if (Math.Abs(e.NewValue - _wiredTab.ZoomPercent) > 0.5)
        {
            _wiredTab.SetCustomZoom(e.NewValue);
        }
    }

    private void NightLightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        bool shouldEnable = e.NewValue > 0.5;
        if (_viewModel.IsNightLightEnabled != shouldEnable)
        {
            _viewModel.IsNightLightEnabled = shouldEnable;
        }
    }

    private void PageInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _wiredTab is not null)
        {
            _wiredTab.CommitPageNumberInput();
            Keyboard.ClearFocus();
        }
    }

    private void PageInputBox_LostFocus(object sender, RoutedEventArgs e) => _wiredTab?.CommitPageNumberInput();

    private void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is PdfPageViewModel page && _wiredTab is not null)
        {
            _wiredTab.GoToPage(page.PageNumber);
            lb.SelectedItem = null;
        }
    }

    private void BookmarkTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is PdfBookmarkItem bookmark && bookmark.PageNumber is int page && _wiredTab is not null)
        {
            _wiredTab.GoToPage(page);
        }
    }

    private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is SearchMatch match && _wiredTab is not null)
        {
            int idx = _wiredTab.SearchResults.IndexOf(match);
            if (idx >= 0)
            {
                _wiredTab.SelectSearchMatch(idx);
            }
        }
    }

    private void AnnotationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is PdfAnnotation ann && _wiredTab is not null)
        {
            _wiredTab.JumpToAnnotation(ann);
        }
    }

    private void EditAnnotationNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id)
        {
            OpenNoteEditorForAnnotation(id);
            e.Handled = true;
        }
    }

    private void AnnotationVisual_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_wiredTab is null || sender is not FrameworkElement fe || fe.Tag is not string id)
        {
            return;
        }

        if (_wiredTab.ActiveTool == AnnotationTool.Eraser)
        {
            _wiredTab.DeleteAnnotationById(id);
            e.Handled = true;
        }
        else if (_wiredTab.ActiveTool == AnnotationTool.Pan && e.ClickCount >= 2)
        {
            OpenNoteEditorForAnnotation(id);
            e.Handled = true;
        }
    }

    private void AnnotationPin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_wiredTab is null || sender is not FrameworkElement fe || fe.Tag is not string id)
        {
            return;
        }

        if (_wiredTab.ActiveTool == AnnotationTool.Eraser)
        {
            _wiredTab.DeleteAnnotationById(id);
            e.Handled = true;
        }
        else if (_wiredTab.ActiveTool == AnnotationTool.Pan)
        {
            OpenNoteEditorForAnnotation(id);
            e.Handled = true;
        }
    }

    private void OpenNoteEditorForAnnotation(string annotationId)
    {
        if (_wiredTab is null)
        {
            return;
        }

        var ann = _wiredTab.GetAnnotationById(annotationId);
        if (ann is null)
        {
            return;
        }

        string initial = ann.Type == AnnotationType.TextBox ? ann.Text : ann.NoteComment;
        string title = ann.Type == AnnotationType.TextBox ? "Edit Text Callout" : $"Note on {ann.TypeDisplay}";
        string subtitle = string.IsNullOrWhiteSpace(ann.Text) || ann.Type == AnnotationType.TextBox
            ? $"Page {ann.PageNumber}:"
            : $"Excerpt: \"{Truncate(ann.Text, 80)}\"";

        var dialog = new NoteEditorDialog(title, subtitle, initial, allowDelete: true)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            if (dialog.DeleteRequested)
            {
                _wiredTab.DeleteAnnotationById(annotationId);
            }
            else
            {
                _wiredTab.UpdateAnnotationText(annotationId, dialog.EnteredText);
            }
        }
    }

    private void FloatingSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (_wiredTab is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && _wiredTab.HasSearchResults)
            {
                _wiredTab.PreviousSearchMatchCommand.Execute(null);
            }
            else if (_wiredTab.HasSearchResults && !string.IsNullOrWhiteSpace(_wiredTab.SearchQuery))
            {
                _wiredTab.NextSearchMatchCommand.Execute(null);
            }
            else
            {
                _wiredTab.SearchCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsSearchBarVisible = false;
            _wiredTab.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SidebarSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _wiredTab is not null)
        {
            _wiredTab.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void CloseSearchBar_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsSearchBarVisible = false;
        _wiredTab?.ClearSearchCommand.Execute(null);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => string.Equals(System.IO.Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (string file in files)
            {
                if (string.Equals(System.IO.Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await _viewModel.OpenFileAsync(file);
                }
            }
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool inReader = _viewModel.ShowDocumentReader && _wiredTab is not null;

        if (ctrl && e.Key == Key.S && _viewModel.ShowNotesStudio)
        {
            e.Handled = true;
            SaveCurrentNoteDocument();
            return;
        }

        if (ctrl && e.Key == Key.O)
        {
            e.Handled = true;
            await _viewModel.OpenFileDialogAsync();
            return;
        }

        if (ctrl && e.Key == Key.W && inReader)
        {
            e.Handled = true;
            _viewModel.CloseTabCommand.Execute(_wiredTab);
            return;
        }

        if (ctrl && e.Key == Key.F && inReader)
        {
            e.Handled = true;
            _viewModel.IsSearchBarVisible = true;
            _viewModel.FocusSearchInputAction?.Invoke();
            return;
        }

        if (ctrl && e.Key == Key.Z && inReader && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            e.Handled = true;
            _wiredTab!.UndoCommand.Execute(null);
            return;
        }

        if (ctrl && e.Key == Key.Y && inReader && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            e.Handled = true;
            _wiredTab!.RedoCommand.Execute(null);
            return;
        }

        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add) && inReader)
        {
            e.Handled = true;
            _wiredTab!.ZoomInCommand.Execute(null);
            return;
        }

        if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract) && inReader)
        {
            e.Handled = true;
            _wiredTab!.ZoomOutCommand.Execute(null);
            return;
        }

        if (ctrl && e.Key == Key.D0 && inReader)
        {
            e.Handled = true;
            _wiredTab!.SetActualSizeCommand.Execute(null);
            return;
        }

        if (e.Key == Key.F11 && inReader)
        {
            e.Handled = true;
            _viewModel.ToggleFullScreenCommand.Execute(null);
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_viewModel.IsPropertiesOpen)
            {
                _viewModel.IsPropertiesOpen = false;
                e.Handled = true;
            }
            else if (_viewModel.IsSearchBarVisible)
            {
                _viewModel.IsSearchBarVisible = false;
                _wiredTab?.ClearSearchCommand.Execute(null);
                e.Handled = true;
            }
            else if (_viewModel.IsFullScreen)
            {
                _viewModel.IsFullScreen = false;
                e.Handled = true;
            }
            else if (inReader && _wiredTab!.ActiveTool != AnnotationTool.Pan)
            {
                _wiredTab.ActiveTool = AnnotationTool.Pan;
                e.Handled = true;
            }

            return;
        }

        if (inReader && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            if (_wiredTab!.IsSinglePageMode && (e.Key == Key.Right || e.Key == Key.PageDown))
            {
                e.Handled = true;
                _wiredTab.NextPageCommand.Execute(null);
            }
            else if (_wiredTab.IsSinglePageMode && (e.Key == Key.Left || e.Key == Key.PageUp))
            {
                e.Handled = true;
                _wiredTab.PreviousPageCommand.Execute(null);
            }
        }
    }

    // MS Paint-Style Word Brush Painting & Annotation Engine
    private void PageSurface_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Grid surface)
        {
            UpdateSurfaceAltCursor(surface);
        }
    }

    private void UpdateSurfaceAltCursor(Grid surface)
    {
        if (_wiredTab is not null &&
            _wiredTab.ActiveTool == AnnotationTool.Pan &&
            (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            surface.Cursor = Cursors.IBeam;
        }
        else
        {
            surface.ClearValue(CursorProperty);
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AiSidebarColumn is null)
        {
            return;
        }

        double width = e.NewSize.Width;
        if (DataContext is MainViewModel vm && vm.ShowAiSidebar)
        {
            if (width < 850)
            {
                double maxSidebar = Math.Max(260, width * 0.45);
                AiSidebarColumn.MaxWidth = Math.Min(420, maxSidebar);
                AiSidebarColumn.Width = new GridLength(Math.Min(420, maxSidebar));

                if (width < 780 && vm.IsSidebarOpen)
                {
                    vm.IsSidebarOpen = false;
                }
            }
            else
            {
                AiSidebarColumn.MaxWidth = double.PositiveInfinity;
                AiSidebarColumn.Width = new GridLength(420);
            }
        }
    }

    private void PageSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid surface || surface.DataContext is not PdfPageViewModel pageVm)
        {
            return;
        }

        Point pos = e.GetPosition(surface);
        if (StartPointerInteraction(surface, pageVm, pos))
        {
            surface.CaptureMouse();
            e.Handled = true;
        }
    }

    private void PageSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDragging && sender is Grid hoverSurface)
        {
            UpdateSurfaceAltCursor(hoverSurface);
        }

        if (_isPointerDragging && _activeSurface is not null)
        {
            ContinuePointerInteraction(e.GetPosition(_activeSurface));
        }
    }

    private void PageSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            var surface = _activeSurface;
            surface.ReleaseMouseCapture();
            FinishPointerInteraction(e.GetPosition(surface));
            e.Handled = true;
        }
    }

    private void PageSurface_PreviewTouchDown(object sender, TouchEventArgs e)
    {
        if (sender is not Grid surface || surface.DataContext is not PdfPageViewModel pageVm)
        {
            return;
        }

        // Multi-touch detection: if another touch is already active, this is pinch-to-zoom/pan gesture.
        // Abort single-finger drawing stroke so the multi-touch manipulation takes over smoothly.
        if (_isPointerDragging || surface.TouchesCaptured.Count() > 0 || surface.TouchesOver.Count() > 1)
        {
            AbortActivePointerInteraction();
            return;
        }

        Point pos = e.GetTouchPoint(surface).Position;
        if (StartPointerInteraction(surface, pageVm, pos))
        {
            surface.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }
    }

    private void PageSurface_PreviewTouchMove(object sender, TouchEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            Point pos = e.GetTouchPoint(_activeSurface).Position;
            ContinuePointerInteraction(pos);
            e.Handled = true;
        }
    }

    private void PageSurface_PreviewTouchUp(object sender, TouchEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            var surface = _activeSurface;
            surface.ReleaseTouchCapture(e.TouchDevice);
            Point pos = e.GetTouchPoint(surface).Position;
            FinishPointerInteraction(pos);
            e.Handled = true;
        }
    }

    private void PageSurface_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (sender is not Grid surface || surface.DataContext is not PdfPageViewModel pageVm)
        {
            return;
        }

        Point pos = e.GetPosition(surface);
        if (StartPointerInteraction(surface, pageVm, pos))
        {
            surface.CaptureStylus();
            e.Handled = true;
        }
    }

    private void PageSurface_PreviewStylusMove(object sender, StylusEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            Point pos = e.GetPosition(_activeSurface);
            ContinuePointerInteraction(pos);
            e.Handled = true;
        }
    }

    private void PageSurface_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            var surface = _activeSurface;
            surface.ReleaseStylusCapture();
            Point pos = e.GetPosition(surface);
            FinishPointerInteraction(pos);
            e.Handled = true;
        }
    }

    private void PageSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            FinishPointerInteraction(_latestDragPoint);
        }
    }

    private void PageSurface_LostTouchCapture(object sender, TouchEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            FinishPointerInteraction(_latestDragPoint);
        }
    }

    private void PageSurface_LostStylusCapture(object sender, StylusEventArgs e)
    {
        if (_isPointerDragging && _activeSurface is not null)
        {
            FinishPointerInteraction(_latestDragPoint);
        }
    }

    private bool StartPointerInteraction(Grid surface, PdfPageViewModel pageVm, Point pos)
    {
        if (_wiredTab is null)
        {
            return false;
        }

        bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        AnnotationTool tool = altHeld && _wiredTab.ActiveTool == AnnotationTool.Pan
            ? AnnotationTool.SelectText
            : _wiredTab.ActiveTool;

        if (tool is AnnotationTool.Pan or AnnotationTool.Eraser)
        {
            return false;
        }

        if (tool == AnnotationTool.StickyNote)
        {
            var pt = ToNormalizedPoint(pos, surface);
            var dialog = new NoteEditorDialog("Add Sticky Note", $"Add a note on page {pageVm.PageNumber}:")
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.EnteredText))
            {
                _wiredTab.AddStickyNote(pageVm.PageNumber, pt, dialog.EnteredText);
            }

            return false;
        }

        if (tool == AnnotationTool.TextBox)
        {
            var pt = ToNormalizedPoint(pos, surface);
            var dialog = new NoteEditorDialog("Add Text Callout", $"Enter text to place on page {pageVm.PageNumber}:")
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.EnteredText))
            {
                _wiredTab.AddTextBox(pageVm.PageNumber, pt, dialog.EnteredText);
            }

            return false;
        }

        _activeSurface = surface;
        _activeSurfacePage = pageVm;
        _activeDragTool = tool;
        _dragStartPoint = pos;
        _lastBrushPoint = pos;
        _latestDragPoint = pos;
        _isPointerDragging = true;
        _inkCapturePoints.Clear();
        _paintedWordIndices.Clear();

        var brush = CreateBrushFromHex(_wiredTab.SelectedColorHex);
        double zoomScale = _wiredTab.ZoomPercent / 100.0;

        if (IsWordBrushTool(tool))
        {
            _inkCapturePoints.Add(pos);
            if (FindVisualChildByName<Polyline>(surface, "PreviewInk") is { } poly)
            {
                poly.Points = [pos];
                poly.Stroke = brush;
                poly.Opacity = 0.42;
                poly.StrokeThickness = Math.Max(10.0, _wiredTab.SelectedStrokeWidth * 4.0) * zoomScale;
            }

            _ = Task.Run(async () =>
            {
                bool hitWords = await _wiredTab.PaintBrushStepAsync(
                    pageVm.PageNumber,
                    ToNormalizedPoint(pos, surface),
                    ToNormalizedPoint(pos, surface),
                    _paintedWordIndices,
                    brush,
                    AnnotationType.Highlight);

                if (!hitWords)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (FindVisualChildByName<Polyline>(surface, "PreviewInk") is { } fallbackPoly)
                        {
                            fallbackPoly.Visibility = Visibility.Visible;
                        }
                    });
                }
            });

            return true;
        }

        if (tool == AnnotationTool.SelectText)
        {
            _ = RefreshWordSelectionPreviewAsync(surface, pageVm, tool);
            return true;
        }

        double strokeW = Math.Max(1.5, _wiredTab.SelectedStrokeWidth * zoomScale);

        if (tool is AnnotationTool.Ink or AnnotationTool.Brush or AnnotationTool.Underline or AnnotationTool.Strikethrough or AnnotationTool.CircleToSearch)
        {
            _inkCapturePoints.Add(pos);
            if (FindVisualChildByName<Polyline>(surface, "PreviewInk") is { } poly)
            {
                poly.Points = [pos];
                poly.Stroke = tool == AnnotationTool.CircleToSearch
                    ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))
                    : brush;
                poly.Opacity = tool == AnnotationTool.Brush ? 0.42 : 0.95;
                poly.StrokeThickness = tool == AnnotationTool.CircleToSearch
                    ? 3.2
                    : (tool == AnnotationTool.Brush
                        ? Math.Max(10.0, _wiredTab.SelectedStrokeWidth * 4.0) * zoomScale
                        : (tool is AnnotationTool.Underline or AnnotationTool.Strikethrough)
                            ? Math.Max(2.5, _wiredTab.SelectedStrokeWidth * zoomScale)
                            : strokeW);
                poly.Visibility = Visibility.Visible;
            }
        }
        else if (tool == AnnotationTool.Arrow)
        {
            if (FindVisualChildByName<Line>(surface, "PreviewLine") is { } line)
            {
                line.X1 = pos.X;
                line.Y1 = pos.Y;
                line.X2 = pos.X;
                line.Y2 = pos.Y;
                line.Stroke = brush;
                line.StrokeThickness = strokeW;
                line.Visibility = Visibility.Visible;
            }
        }
        else if (tool == AnnotationTool.Ellipse)
        {
            if (FindVisualChildByName<Ellipse>(surface, "PreviewEllipse") is { } ellipse)
            {
                Canvas.SetLeft(ellipse, pos.X);
                Canvas.SetTop(ellipse, pos.Y);
                ellipse.Width = 0;
                ellipse.Height = 0;
                ellipse.Stroke = brush;
                ellipse.StrokeThickness = strokeW;
                ellipse.Visibility = Visibility.Visible;
            }
        }
        else if (tool == AnnotationTool.Rectangle)
        {
            if (FindVisualChildByName<Rectangle>(surface, "PreviewRect") is { } rect)
            {
                Canvas.SetLeft(rect, pos.X);
                Canvas.SetTop(rect, pos.Y);
                rect.Width = 0;
                rect.Height = 0;
                rect.Stroke = brush;
                rect.StrokeThickness = strokeW;
                rect.Visibility = Visibility.Visible;
            }
        }

        return true;
    }

    private void ContinuePointerInteraction(Point rawPos)
    {
        if (!_isPointerDragging || _activeSurface is null || _activeSurfacePage is null || _wiredTab is null)
        {
            return;
        }

        Point current = ClampToSurface(rawPos, _activeSurface);
        _latestDragPoint = current;

        if (IsWordBrushTool(_activeDragTool))
        {
            if (_inkCapturePoints.Count == 0 ||
                (Math.Abs(current.X - _inkCapturePoints[^1].X) >= 2 || Math.Abs(current.Y - _inkCapturePoints[^1].Y) >= 2))
            {
                _inkCapturePoints.Add(current);
                if (FindVisualChildByName<Polyline>(_activeSurface, "PreviewInk") is { } poly)
                {
                    poly.Points.Add(current);
                }
            }

            if (!_isWordPreviewBusy)
            {
                _isWordPreviewBusy = true;
                var prev = _lastBrushPoint;
                _lastBrushPoint = current;
                var surface = _activeSurface;
                var pageVm = _activeSurfacePage;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        bool hasWords = await _wiredTab.PaintBrushStepAsync(
                            pageVm.PageNumber,
                            ToNormalizedPoint(prev, surface),
                            ToNormalizedPoint(current, surface),
                            _paintedWordIndices,
                            CreateBrushFromHex(_wiredTab.SelectedColorHex),
                            AnnotationType.Highlight);

                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (FindVisualChildByName<Polyline>(surface, "PreviewInk") is { } poly)
                            {
                                poly.Visibility = hasWords ? Visibility.Collapsed : Visibility.Visible;
                            }
                        });
                    }
                    finally
                    {
                        _isWordPreviewBusy = false;
                    }
                });
            }

            return;
        }

        if (_activeDragTool == AnnotationTool.SelectText)
        {
            if (!_isWordPreviewBusy)
            {
                _ = RefreshWordSelectionPreviewAsync(_activeSurface, _activeSurfacePage, _activeDragTool);
            }
            return;
        }

        if (_activeDragTool is AnnotationTool.Ink or AnnotationTool.Brush or AnnotationTool.Underline or AnnotationTool.Strikethrough or AnnotationTool.CircleToSearch)
        {
            if (_inkCapturePoints.Count == 0 ||
                (Math.Abs(current.X - _inkCapturePoints[^1].X) >= 2 || Math.Abs(current.Y - _inkCapturePoints[^1].Y) >= 2))
            {
                _inkCapturePoints.Add(current);
                if (FindVisualChildByName<Polyline>(_activeSurface, "PreviewInk") is { } poly)
                {
                    poly.Points.Add(current);
                }
            }
            return;
        }

        if (_activeDragTool == AnnotationTool.Arrow)
        {
            if (FindVisualChildByName<Line>(_activeSurface, "PreviewLine") is { } line)
            {
                line.X2 = current.X;
                line.Y2 = current.Y;
            }
            return;
        }

        double x = Math.Min(_dragStartPoint.X, current.X);
        double y = Math.Min(_dragStartPoint.Y, current.Y);
        double w = Math.Abs(current.X - _dragStartPoint.X);
        double h = Math.Abs(current.Y - _dragStartPoint.Y);

        if (_activeDragTool == AnnotationTool.Ellipse)
        {
            if (FindVisualChildByName<Ellipse>(_activeSurface, "PreviewEllipse") is { } ellipse)
            {
                Canvas.SetLeft(ellipse, x);
                Canvas.SetTop(ellipse, y);
                ellipse.Width = w;
                ellipse.Height = h;
            }
        }
        else if (_activeDragTool == AnnotationTool.Rectangle &&
                 FindVisualChildByName<Rectangle>(_activeSurface, "PreviewRect") is { } rect)
        {
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = w;
            rect.Height = h;
        }
    }

    private async void FinishPointerInteraction(Point rawPos)
    {
        if (!_isPointerDragging || _activeSurface is null || _activeSurfacePage is null || _wiredTab is null)
        {
            return;
        }

        var surface = _activeSurface;
        var pageVm = _activeSurfacePage;
        var tool = _activeDragTool;
        Point endPoint = ClampToSurface(rawPos, surface);

        _isPointerDragging = false;
        _activeSurface = null;
        _activeSurfacePage = null;
        HideAllPreviewShapes(surface);

        double surfaceW = surface.ActualWidth;
        double surfaceH = surface.ActualHeight;
        if (surfaceW <= 0 || surfaceH <= 0)
        {
            pageVm.ClearLiveWordSelection();
            return;
        }

        if (IsWordBrushTool(tool))
        {
            // Flush final sweep step before committing
            await _wiredTab.PaintBrushStepAsync(
                pageVm.PageNumber,
                ToNormalizedPoint(_lastBrushPoint, surface),
                ToNormalizedPoint(endPoint, surface),
                _paintedWordIndices,
                CreateBrushFromHex(_wiredTab.SelectedColorHex),
                AnnotationType.Highlight);

            var strokeNorm = _inkCapturePoints
                .Select(p => new NormalizedPoint(Math.Clamp(p.X / surfaceW, 0, 1), Math.Clamp(p.Y / surfaceH, 0, 1)))
                .ToList();

            await _wiredTab.CommitWordBrushAnnotationAsync(pageVm.PageNumber, _paintedWordIndices, strokeNorm, AnnotationType.Highlight);
            _inkCapturePoints.Clear();
            _paintedWordIndices.Clear();
            return;
        }

        if (tool == AnnotationTool.SelectText)
        {
            string copied = await _wiredTab.CommitWordSelectionCopyAsync(
                pageVm.PageNumber,
                ToNormalizedPoint(_dragStartPoint, surface),
                ToNormalizedPoint(endPoint, surface));

            if (string.IsNullOrWhiteSpace(copied))
            {
                _wiredTab.StatusMessage = "No selectable words found";
                return;
            }

            Clipboard.SetText(copied);
            _wiredTab.StatusMessage = $"Copied: \"{Truncate(copied, 60)}\"";
            return;
        }

        if (tool == AnnotationTool.CircleToSearch)
        {
            if (_inkCapturePoints.Count >= 3)
            {
                var normPoints = _inkCapturePoints
                    .Select(p => new NormalizedPoint(Math.Clamp(p.X / surfaceW, 0, 1), Math.Clamp(p.Y / surfaceH, 0, 1)))
                    .ToList();

                var pStart = normPoints[0];
                var pEnd = normPoints[^1];
                double dx = pEnd.X - pStart.X;
                double dy = pEnd.Y - pStart.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > 0.001)
                {
                    normPoints.Add(new NormalizedPoint(pStart.X, pStart.Y));
                }

                double minX = normPoints.Min(p => p.X);
                double maxX = normPoints.Max(p => p.X);
                double minY = normPoints.Min(p => p.Y);
                double maxY = normPoints.Max(p => p.Y);
                double w = maxX - minX;
                double h = maxY - minY;

                if (w >= 0.005 && h >= 0.005)
                {
                    var normRect = new NormalizedRect(minX, minY, w, h);
                    await HandleCircleToSearchSelectionAsync(pageVm, normPoints, normRect);
                }
            }

            _inkCapturePoints.Clear();
            return;
        }

        if (tool is AnnotationTool.Ink or AnnotationTool.Brush or AnnotationTool.Underline or AnnotationTool.Strikethrough)
        {
            if (_inkCapturePoints.Count >= 2)
            {
                var normPts = _inkCapturePoints
                    .Select(p => new NormalizedPoint(Math.Clamp(p.X / surfaceW, 0, 1), Math.Clamp(p.Y / surfaceH, 0, 1)))
                    .ToList();

                if (tool == AnnotationTool.Underline)
                {
                    _wiredTab.AddUnderlineStrokeAnnotation(pageVm.PageNumber, normPts);
                }
                else if (tool == AnnotationTool.Strikethrough)
                {
                    _wiredTab.AddStrikethroughStrokeAnnotation(pageVm.PageNumber, normPts);
                }
                else if (tool == AnnotationTool.Brush)
                {
                    _wiredTab.AddBrushAnnotation(pageVm.PageNumber, normPts);
                }
                else
                {
                    _wiredTab.AddInkAnnotation(pageVm.PageNumber, normPts);
                }
            }

            _inkCapturePoints.Clear();
            return;
        }

        if (tool == AnnotationTool.Arrow)
        {
            double dist = double.Hypot(endPoint.X - _dragStartPoint.X, endPoint.Y - _dragStartPoint.Y);
            if (dist >= 8)
            {
                _wiredTab.AddArrowAnnotation(
                    pageVm.PageNumber,
                    ToNormalizedPoint(_dragStartPoint, surface),
                    ToNormalizedPoint(endPoint, surface));
            }
            return;
        }

        double left = Math.Min(_dragStartPoint.X, endPoint.X);
        double top = Math.Min(_dragStartPoint.Y, endPoint.Y);
        double width = Math.Abs(endPoint.X - _dragStartPoint.X);
        double height = Math.Abs(endPoint.Y - _dragStartPoint.Y);
        if (width < 5 || height < 5)
        {
            return;
        }

        var normalized = new NormalizedRect(left / surfaceW, top / surfaceH, width / surfaceW, height / surfaceH);
        if (tool == AnnotationTool.Rectangle)
        {
            await _wiredTab.AddRegionAnnotationAsync(pageVm.PageNumber, normalized, AnnotationType.Rectangle);
        }
        else if (tool == AnnotationTool.Ellipse)
        {
            await _wiredTab.AddRegionAnnotationAsync(pageVm.PageNumber, normalized, AnnotationType.Ellipse);
        }
    }

    private async Task RefreshWordSelectionPreviewAsync(Grid surface, PdfPageViewModel pageVm, AnnotationTool tool)
    {
        if (_wiredTab is null || _isWordPreviewBusy)
        {
            return;
        }

        _isWordPreviewBusy = true;
        try
        {
            Brush previewBrush = tool == AnnotationTool.SelectText
                ? CreateBrushFromHex("#FF0078D4")
                : CreateBrushFromHex(_wiredTab.SelectedColorHex);

            await _wiredTab.UpdateLiveWordSelectionAsync(
                pageVm.PageNumber,
                ToNormalizedPoint(_dragStartPoint, surface),
                ToNormalizedPoint(_latestDragPoint, surface),
                previewBrush);
        }
        finally
        {
            _isWordPreviewBusy = false;
        }
    }

    private static bool IsWordBrushTool(AnnotationTool tool) =>
        tool == AnnotationTool.Highlight;

    private static void HideAllPreviewShapes(Grid surface)
    {
        if (FindVisualChildByName<Rectangle>(surface, "PreviewRect") is { } rect)
        {
            rect.Visibility = Visibility.Collapsed;
        }

        if (FindVisualChildByName<Ellipse>(surface, "PreviewEllipse") is { } ellipse)
        {
            ellipse.Visibility = Visibility.Collapsed;
        }

        if (FindVisualChildByName<Line>(surface, "PreviewLine") is { } line)
        {
            line.Visibility = Visibility.Collapsed;
        }

        if (FindVisualChildByName<Polyline>(surface, "PreviewInk") is { } poly)
        {
            poly.Visibility = Visibility.Collapsed;
            poly.Points.Clear();
            poly.Opacity = 1.0;
        }
    }

    private static Point ClampToSurface(Point p, Grid surface) =>
        new(Math.Clamp(p.X, 0, surface.ActualWidth), Math.Clamp(p.Y, 0, surface.ActualHeight));

    private static NormalizedPoint ToNormalizedPoint(Point p, Grid surface) =>
        new(
            surface.ActualWidth > 0 ? Math.Clamp(p.X / surface.ActualWidth, 0, 1) : 0,
            surface.ActualHeight > 0 ? Math.Clamp(p.Y / surface.ActualHeight, 0, 1) : 0);

    private static Brush CreateBrushFromHex(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gold;
        }
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length > maxLen ? $"{text[..maxLen]}…" : text;

    private static int CountWords(string s) =>
        string.IsNullOrWhiteSpace(s) ? 0 : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // ==================== CIRCLE TO SEARCH WITH AI ====================
    private string _circleSelectionText = string.Empty;
    private int _circleSelectionPageNumber;

    private async Task HandleCircleToSearchSelectionAsync(
        PdfPageViewModel pageVm,
        IReadOnlyList<NormalizedPoint> normPolygon,
        NormalizedRect normRect)
    {
        if (_wiredTab is null) return;

        _circleSelectionPageNumber = pageVm.PageNumber;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. PRIMARY & EXCLUSIVE: Run scanline-masked OCR directly on the pixels under the circled area.
        // Every pixel outside the user's freehand circle is masked with pure solid white (#FFFFFF).
        // Text outside the circle physically does not exist in the raster buffer passed to OCR.
        string ocrText = string.Empty;
        bool usedOcr = false;

        if (_ocrService.IsSupported)
        {
            ocrText = await _wiredTab.ExtractTextWithOcrAsync(
                pageVm.PageNumber,
                normRect,
                _ocrService,
                pageVm.RenderedImage,
                normPolygon);

            _circleSelectionText = ocrText?.Trim() ?? string.Empty;
            usedOcr = true;
        }
        else
        {
            // Fallback only if OCR engine is completely unsupported on this platform (e.g. non-Windows OS)
            var (vectorText, _) = await _wiredTab.ExtractTextInPolygonWithRectsAsync(pageVm.PageNumber, normPolygon);
            _circleSelectionText = vectorText?.Trim() ?? string.Empty;
        }
        sw.Stop();

        // Highlight selection with a friendly cyan glow:
        var highlightRects = new List<NormalizedRect> { normRect };

        pageVm.UpdateLiveWordSelection(
            highlightRects,
            _wiredTab.RotationDegrees,
            new SolidColorBrush(Color.FromArgb(0x40, 0x38, 0xBD, 0xF8)));

        string engineBadge = usedOcr ? $" • OCR ({_ocrService.RecognizerLanguage})" : " • Vector Text";
        CircleToSearchPageBadge.Text = $" • Page {pageVm.PageNumber}{engineBadge}";

        if (!string.IsNullOrWhiteSpace(_circleSelectionText))
        {
            CircleToSearchSnippetText.Text = $"\"{Truncate(_circleSelectionText, 180)}\"";
            CircleToSearchSnippetText.FontStyle = FontStyles.Normal;
            _viewModel.GlobalStatusText = $"Circle to Search: captured {CountWords(_circleSelectionText)} words in {sw.ElapsedMilliseconds}ms ({(_ocrService.IsSupported ? _ocrService.RecognizerLanguage : "Vector")})";
        }
        else
        {
            CircleToSearchSnippetText.Text = "Circled visual diagram or region (no plain text detected)";
            CircleToSearchSnippetText.FontStyle = FontStyles.Italic;
            _viewModel.GlobalStatusText = $"Circle to Search: visual region on page {pageVm.PageNumber} in {sw.ElapsedMilliseconds}ms";
        }

        CircleToSearchCard.Visibility = Visibility.Visible;
        CircleToSearchCustomPromptBox.Focus();
    }

    private void CloseCircleToSearch_Click(object sender, RoutedEventArgs e)
    {
        CircleToSearchCard.Visibility = Visibility.Collapsed;
        foreach (var page in _wiredTab?.Pages ?? Enumerable.Empty<PdfPageViewModel>())
        {
            page.ClearLiveWordSelection();
        }
    }

    private async void CircleAction_Explain_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteCircleAiQueryAsync("Explain this clearly with key takeaways, formulas, and concepts for my study preparation.");
    }

    private async void CircleAction_Solve_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteCircleAiQueryAsync("Solve this step-by-step with clear derivations, mathematical formulas, and calculation steps.");
    }

    private async void CircleAction_WebSearch_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteCircleAiQueryAsync("Search the web for up-to-date facts, current developments, and verified references regarding this topic.");
    }

    private async void CircleAction_Practice_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteCircleAiQueryAsync("Generate 2 exam practice questions (with answers and explanations) based directly on this topic.");
    }

    private void CircleAction_Copy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_circleSelectionText))
        {
            Clipboard.SetText(_circleSelectionText);
            _viewModel.GlobalStatusText = $"Copied circled text: \"{Truncate(_circleSelectionText, 60)}\"";
        }
        else
        {
            _viewModel.GlobalStatusText = "No plain text found in circled region";
        }
        CloseCircleToSearch_Click(sender, e);
    }

    private async void CircleAction_CustomAsk_Click(object sender, RoutedEventArgs e)
    {
        string custom = CircleToSearchCustomPromptBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(custom))
        {
            custom = "Explain this selection in detail.";
        }
        CircleToSearchCustomPromptBox.Text = string.Empty;
        await ExecuteCircleAiQueryAsync(custom);
    }

    private void CircleToSearchCustomPromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CircleAction_CustomAsk_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CloseCircleToSearch_Click(sender, e);
        }
    }

    private async Task ExecuteCircleAiQueryAsync(string userInstruction)
    {
        CircleToSearchCard.Visibility = Visibility.Collapsed;
        _viewModel.IsAiSidebarOpen = true;

        string docTitle = _wiredTab?.DocumentInfo.Title ?? _wiredTab?.DocumentInfo.FileName ?? "Document";
        string fullPrompt;
        string sessionTitle;

        if (!string.IsNullOrWhiteSpace(_circleSelectionText))
        {
            string shortSnippet = _circleSelectionText.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (shortSnippet.Length > 36) shortSnippet = shortSnippet[..33] + "…";
            sessionTitle = $"🔍 {shortSnippet} (p.{_circleSelectionPageNumber})";
            fullPrompt = $"[Circled on Page {_circleSelectionPageNumber} of '{docTitle}']:\n\"{_circleSelectionText}\"\n\n{userInstruction}";
        }
        else
        {
            sessionTitle = $"🔍 Page {_circleSelectionPageNumber} Diagram";
            fullPrompt = $"[Circled diagram/region on Page {_circleSelectionPageNumber} of '{docTitle}']:\n{userInstruction}";
        }

        await _viewModel.AiAssistant.StartCircleToSearchSessionAsync(sessionTitle, fullPrompt);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_wiredTab is null)
        {
            return;
        }

        var printDialog = new PrintDialog
        {
            UserPageRangeEnabled = true,
            MinPage = 1,
            MaxPage = (uint)_wiredTab.PageCount
        };

        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        int startPage = 1;
        int endPage = _wiredTab.PageCount;
        if (printDialog.PageRangeSelection == PageRangeSelection.UserPages)
        {
            startPage = Math.Clamp(printDialog.PageRange.PageFrom, 1, _wiredTab.PageCount);
            endPage = Math.Clamp(printDialog.PageRange.PageTo, startPage, _wiredTab.PageCount);
        }

        var paginator = new PdfPrintPaginator(
            _viewModel.PdfService,
            _wiredTab.FilePath,
            startPage,
            endPage,
            _wiredTab.RotationDegrees,
            new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight));

        printDialog.PrintDocument(paginator, _wiredTab.DocumentInfo.FileName);
    }

    private Task<string?> PromptForPasswordAsync(string fileName, bool incorrectPreviousAttempt)
    {
        var dialog = new PasswordDialog(fileName, incorrectPreviousAttempt)
        {
            Owner = this
        };

        bool? result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.EnteredPassword : null);
    }

    private void ShowInfoMessage(string title, string message)
    {
        System.Windows.MessageBox.Show(this, message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            T? nested = FindVisualChildByName<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class PdfPrintPaginator : DocumentPaginator
    {
        private readonly IPdfService _pdfService;
        private readonly string _filePath;
        private readonly int _startPage;
        private readonly int _endPage;
        private readonly int _rotationDegrees;
        private Size _pageSize;

        public PdfPrintPaginator(
            IPdfService pdfService,
            string filePath,
            int startPage,
            int endPage,
            int rotationDegrees,
            Size pageSize)
        {
            _pdfService = pdfService;
            _filePath = filePath;
            _startPage = startPage;
            _endPage = endPage;
            _rotationDegrees = rotationDegrees;
            _pageSize = pageSize;
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => (_endPage - _startPage) + 1;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            int pdfPage = _startPage + pageNumber;
            var bitmap = _pdfService
                .RenderPageAsync(_filePath, pdfPage, dpi: 200, _rotationDegrees)
                .GetAwaiter()
                .GetResult();

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double scale = Math.Min(_pageSize.Width / bitmap.Width, _pageSize.Height / bitmap.Height);
                double drawW = bitmap.Width * scale;
                double drawH = bitmap.Height * scale;
                double offsetX = (_pageSize.Width - drawW) / 2.0;
                double offsetY = (_pageSize.Height - drawH) / 2.0;
                dc.DrawImage(bitmap, new Rect(offsetX, offsetY, drawW, drawH));
            }

            return new DocumentPage(visual, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
        }
    }
}
