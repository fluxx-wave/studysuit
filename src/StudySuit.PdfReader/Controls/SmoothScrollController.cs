using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StudySuit.PdfReader.Controls;

/// <summary>
/// Frame-rate-independent exponential ease controller for smooth mouse-wheel and trackpad scrolling on WPF ScrollViewers.
/// </summary>
public sealed class SmoothScrollController : IDisposable
{
    private readonly ScrollViewer _scrollViewer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _targetVertical;
    private double _targetHorizontal;
    private long _lastTickMs;
    private bool _isAnimating;
    private bool _disposed;

    public SmoothScrollController(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;
        _targetVertical = scrollViewer.VerticalOffset;
        _targetHorizontal = scrollViewer.HorizontalOffset;

        // If the user clicks, touches, or begins touch manipulation directly, sync immediately
        _scrollViewer.PreviewMouseDown += OnScrollViewerPreviewMouseDown;
        _scrollViewer.PreviewTouchDown += OnScrollViewerPreviewTouchDown;
        _scrollViewer.ManipulationStarting += OnScrollViewerManipulationStarting;
    }

    private void OnScrollViewerPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        SyncAndStop();
    }

    private void OnScrollViewerPreviewTouchDown(object? sender, TouchEventArgs e)
    {
        SyncAndStop();
    }

    private void OnScrollViewerManipulationStarting(object? sender, ManipulationStartingEventArgs e)
    {
        SyncAndStop();
    }

    public void HandleMouseWheel(MouseWheelEventArgs e, bool horizontal = false, double wheelMultiplier = 0.95)
    {
        if (_disposed || e.Delta == 0)
        {
            return;
        }

        if (horizontal)
        {
            double scrollableW = _scrollViewer.ScrollableWidth;
            if (scrollableW <= 0 && _scrollViewer.ExtentWidth > _scrollViewer.ViewportWidth)
            {
                _scrollViewer.UpdateLayout();
                scrollableW = _scrollViewer.ScrollableWidth;
            }

            if (scrollableW > 0)
            {
                if (!_isAnimating)
                {
                    _targetHorizontal = _scrollViewer.HorizontalOffset;
                    _targetVertical = _scrollViewer.VerticalOffset;
                }

                _targetHorizontal = Math.Clamp(
                    _targetHorizontal - (e.Delta * wheelMultiplier),
                    0.0,
                    scrollableW);
                e.Handled = true;
                StartAnimation();
            }
        }
        else
        {
            double scrollableH = _scrollViewer.ScrollableHeight;
            // On fresh document load, query layout if height hasn't settled yet
            if (scrollableH <= 0 && _scrollViewer.ExtentHeight > _scrollViewer.ViewportHeight)
            {
                _scrollViewer.UpdateLayout();
                scrollableH = _scrollViewer.ScrollableHeight;
            }

            if (scrollableH > 0)
            {
                if (!_isAnimating)
                {
                    _targetVertical = _scrollViewer.VerticalOffset;
                    _targetHorizontal = _scrollViewer.HorizontalOffset;
                }

                _targetVertical = Math.Clamp(
                    _targetVertical - (e.Delta * wheelMultiplier),
                    0.0,
                    scrollableH);
                e.Handled = true;
                StartAnimation();
            }
        }
    }

    public void SyncAndStop()
    {
        StopAnimation();
        _targetVertical = _scrollViewer.VerticalOffset;
        _targetHorizontal = _scrollViewer.HorizontalOffset;
    }

    private void StartAnimation()
    {
        if (_isAnimating || _disposed)
        {
            return;
        }

        _lastTickMs = _clock.ElapsedMilliseconds;
        _isAnimating = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_isAnimating)
        {
            return;
        }

        _isAnimating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            StopAnimation();
            return;
        }

        // If user is holding left button down on scrollbar (dragging thumb) or actively touching/manipulating, yield to native scroll
        if ((Mouse.LeftButton == MouseButtonState.Pressed && Mouse.Captured is not null) ||
            _scrollViewer.AreAnyTouchesCaptured)
        {
            SyncAndStop();
            return;
        }

        long nowMs = _clock.ElapsedMilliseconds;
        double dt = Math.Clamp((nowMs - _lastTickMs) / 1000.0, 0.001, 0.05);
        _lastTickMs = nowMs;

        double alpha = 1.0 - Math.Exp(-14.5 * dt);

        double maxV = Math.Max(0.0, _scrollViewer.ScrollableHeight);
        double maxH = Math.Max(0.0, _scrollViewer.ScrollableWidth);
        _targetVertical = Math.Clamp(_targetVertical, 0.0, maxV);
        _targetHorizontal = Math.Clamp(_targetHorizontal, 0.0, maxH);

        double curV = _scrollViewer.VerticalOffset;
        double curH = _scrollViewer.HorizontalOffset;
        double diffV = _targetVertical - curV;
        double diffH = _targetHorizontal - curH;

        bool doneV = Math.Abs(diffV) < 0.6;
        bool doneH = Math.Abs(diffH) < 0.6;

        double nextV = doneV ? _targetVertical : curV + (diffV * alpha);
        double nextH = doneH ? _targetHorizontal : curH + (diffH * alpha);

        if (Math.Abs(nextV - curV) > 0.05 || doneV)
        {
            _scrollViewer.ScrollToVerticalOffset(nextV);
        }

        if (Math.Abs(nextH - curH) > 0.05 || doneH)
        {
            _scrollViewer.ScrollToHorizontalOffset(nextH);
        }

        if (doneV && doneH)
        {
            StopAnimation();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopAnimation();
        _scrollViewer.PreviewMouseDown -= OnScrollViewerPreviewMouseDown;
        _scrollViewer.PreviewTouchDown -= OnScrollViewerPreviewTouchDown;
        _scrollViewer.ManipulationStarting -= OnScrollViewerManipulationStarting;
    }
}
