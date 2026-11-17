using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StudySuit.PdfReader.Services;

namespace StudySuit.PdfReader.Controls;

/// <summary>
/// Read-only rich Markdown viewer that renders headings, tables, code blocks, LaTeX math, charts, and images
/// while forwarding scroll wheel events smoothly to the enclosing chat ScrollViewer.
/// </summary>
public sealed class MarkdownMessageView : RichTextBox
{
    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(
            nameof(MarkdownText),
            typeof(string),
            typeof(MarkdownMessageView),
            new PropertyMetadata(string.Empty, OnContentPropertyChanged));

    public static readonly DependencyProperty IsDarkThemeProperty =
        DependencyProperty.Register(
            nameof(IsDarkTheme),
            typeof(bool),
            typeof(MarkdownMessageView),
            new PropertyMetadata(true, OnContentPropertyChanged));

    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.Register(
            nameof(IsStreaming),
            typeof(bool),
            typeof(MarkdownMessageView),
            new PropertyMetadata(false, OnContentPropertyChanged));

    private readonly DispatcherTimer _renderThrottleTimer;
    private bool _pendingRender;

    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public bool IsDarkTheme
    {
        get => (bool)GetValue(IsDarkThemeProperty);
        set => SetValue(IsDarkThemeProperty, value);
    }

    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    public MarkdownMessageView()
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = true;
        IsReadOnlyCaretVisible = false;

        _renderThrottleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _renderThrottleTimer.Tick += (_, _) =>
        {
            _renderThrottleTimer.Stop();
            if (_pendingRender)
            {
                _pendingRender = false;
                RebuildDocument();
            }
        };

        PreviewMouseWheel += OnPreviewMouseWheel;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && !string.IsNullOrEmpty(MarkdownText))
        {
            if (MarkdownText.Contains("[3D Model:", StringComparison.OrdinalIgnoreCase) ||
                MarkdownText.Contains("sketchfab.com", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleRender();
            }
        }
    }

    private static void OnContentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownMessageView view)
        {
            view.ScheduleRender();
        }
    }

    private void ScheduleRender()
    {
        if (!_renderThrottleTimer.IsEnabled)
        {
            RebuildDocument();
            _renderThrottleTimer.Start();
        }
        else
        {
            _pendingRender = true;
        }
    }

    public void RebuildDocument()
    {
        var doc = MarkdownFlowRenderer.RenderToFlowDocument(
            MarkdownText ?? string.Empty,
            IsDarkTheme,
            OpenExternalLink,
            IsStreaming);
        if (ActualWidth > 0)
        {
            doc.PageWidth = ActualWidth;
        }
        Document = doc;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!double.IsInfinity(constraint.Width) && constraint.Width > 0 && Document != null)
        {
            if (Math.Abs(Document.PageWidth - constraint.Width) > 1)
            {
                Document.PageWidth = constraint.Width;
            }
        }
        return base.MeasureOverride(constraint);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (Document != null && sizeInfo.NewSize.Width > 0)
        {
            Document.PageWidth = sizeInfo.NewSize.Width;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        // Forward wheel scrolling to parent ScrollViewer so chat bubbles never trap vertical scroll.
        e.Handled = true;
        var parent = VisualTreeHelper.GetParent(this) as UIElement;
        while (parent is not null && parent is not ScrollViewer)
        {
            parent = VisualTreeHelper.GetParent(parent) as UIElement;
        }

        if (parent is ScrollViewer sv)
        {
            sv.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sender
            });
        }
    }

    private static void OpenExternalLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
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
}

