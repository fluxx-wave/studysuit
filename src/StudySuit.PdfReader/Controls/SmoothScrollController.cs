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
    private double _lastAppliedVertical;
    private double _lastAppliedHorizontal;
    private long _lastTickMs;
    private bool _isAnimating;
    private bool _disposed;

    public SmoothScrollController(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;
        _targetVertical = scrollViewer.VerticalOffset;
        _targetHorizontal = scrollViewer.HorizontalOffset;
        _lastAppliedVertical = _targetVertical;
        _lastAppliedHorizontal = _targetHorizontal;
    }

    public void HandleMouseWheel(MouseWheelEventArgs e, bool horizontal = false, double wheelMultiplier = 0.95)
    {
        if (_disposed || e.Delta == 0)
        {
            return;
        }

        if (horizontal && _scrollViewer.ScrollableWidth > 0)
        {
            if (!_isAnimating || Math.Abs(_scrollViewer.HorizontalOffset - _lastAppliedHorizontal) > 2.5)
            {
                _targetHorizontal = _scrollViewer.HorizontalOffset;
                _targetVertical = _scrollViewer.VerticalOffset;
            }

            _targetHorizontal = Math.Clamp(
                _targetHorizontal - (e.Delta * wheelMultiplier),
                0.0,
                _scrollViewer.ScrollableWidth);
            e.Handled = true;
            StartAnimation();
        }
        else if (!horizontal && _scrollViewer.ScrollableHeight > 0)
        {
            if (!_isAnimating || Math.Abs(_scrollViewer.VerticalOffset - _lastAppliedVertical) > 2.5)
            {
                _targetVertical = _scrollViewer.VerticalOffset;
                _targetHorizontal = _scrollViewer.HorizontalOffset;
            }

            _targetVertical = Math.Clamp(
                _targetVertical - (e.Delta * wheelMultiplier),
                0.0,
                _scrollViewer.ScrollableHeight);
            e.Handled = true;
            StartAnimation();
        }
    }

    public void SyncAndStop()
    {
        StopAnimation();
        _targetVertical = _scrollViewer.VerticalOffset;
        _targetHorizontal = _scrollViewer.HorizontalOffset;
        _lastAppliedVertical = _targetVertical;
        _lastAppliedHorizontal = _targetHorizontal;
    }

    private void StartAnimation()
    {
        if (_isAnimating || _disposed)
        {
            return;
        }

        _lastAppliedVertical = _scrollViewer.VerticalOffset;
        _lastAppliedHorizontal = _scrollViewer.HorizontalOffset;
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

        // If user dragged the scrollbar thumb externally, yield immediately to native drag.
        if (Math.Abs(_scrollViewer.VerticalOffset - _lastAppliedVertical) > 4.0 ||
            Math.Abs(_scrollViewer.HorizontalOffset - _lastAppliedHorizontal) > 4.0)
        {
            SyncAndStop();
            return;
        }

        long nowMs = _clock.ElapsedMilliseconds;
        double dt = Math.Clamp((nowMs - _lastTickMs) / 1000.0, 0.001, 0.05);
        _lastTickMs = nowMs;

        double alpha = 1.0 - Math.Exp(-14.5 * dt);

        _targetVertical = Math.Clamp(_targetVertical, 0.0, _scrollViewer.ScrollableHeight);
        _targetHorizontal = Math.Clamp(_targetHorizontal, 0.0, _scrollViewer.ScrollableWidth);

        double curV = _scrollViewer.VerticalOffset;
        double curH = _scrollViewer.HorizontalOffset;
        double diffV = _targetVertical - curV;
        double diffH = _targetHorizontal - curH;

        bool doneV = Math.Abs(diffV) < 0.6;
        bool doneH = Math.Abs(diffH) < 0.6;

        double nextV = doneV ? _targetVertical : curV + (diffV * alpha);
        double nextH = doneH ? _targetHorizontal : curH + (diffH * alpha);

        _lastAppliedVertical = nextV;
        _lastAppliedHorizontal = nextH;

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
    }
}

