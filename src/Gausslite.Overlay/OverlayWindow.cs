// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;
using Gausslite.Core.Blur;
using Gausslite.Core.Diagnostics;
using Gausslite.Overlay.Interop;
using System.Collections.Generic;

namespace Gausslite.Overlay;

/// <summary>
/// Transparent, always-on-top, click-through WPF window that renders a
/// <see cref="D3DImage"/> fed by <see cref="IBlurRenderTarget"/> frames.
/// </summary>
/// <remarks>
/// <para>
/// Must be constructed and shown on the WPF UI (STA) thread.
/// <see cref="PresentFrame"/> may be called from any thread.
/// </para>
/// <para>
/// Per-monitor DPI awareness must be declared in the host executable's manifest
/// (PerMonitorV2) so that <see cref="Window.Left"/>, <see cref="Window.Top"/>,
/// <see cref="Window.Width"/>, and <see cref="Window.Height"/> (device-independent
/// pixels) align with the coordinates supplied by <c>WindowTracker</c>.
/// </para>
/// </remarks>
public sealed class OverlayWindow : IOverlayWindow
{
    internal const double OffscreenParkX = -32000;
    internal const double OffscreenParkY = -32000;

    private Window        _window = null!;
    private Grid          _contentRoot = null!;
    private Image         _image = null!;
    private Border        _placeholder = null!;
    private D3DImage      _d3dImage = null!;
    private readonly ID3DImageBridge _bridge;
    private readonly IWindowBoundsApplier _boundsApplier;
    private HwndSource? _hwndSource;
    private Rect? _requestedBounds;
    private bool _isParked = true;
    private bool _disposed;

    private int _presentFrameCount;
    private int _presentExceptionCount;
    private int _visualTreeLogged;

    // CompositionTarget.Rendering diagnostics — all accessed on UI thread only.
    private EventHandler? _compositionRenderingHandler;
    private int _compositionRenderingFiringsLeft;
    private const int MaxRenderingFirings = 5;

    public IntPtr WindowHandle => new WindowInteropHelper(_window).Handle;

    /// <summary>Creates an overlay with the default GPU bridge.</summary>
    public OverlayWindow() : this(null) { }

    /// <param name="bridge">Bridge override for internal/test use; must not be null.</param>
    internal OverlayWindow(ID3DImageBridge? bridge, IWindowBoundsApplier? boundsApplier = null)
    {
        _bridge = bridge ?? new D3DImageBridge();
        _boundsApplier = boundsApplier ?? new WindowBoundsApplier();
        CreateWindow();
    }

    private void CreateWindow()
    {
        _d3dImage = new D3DImage();
        _image    = new Image
        {
            Source              = _d3dImage,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Stretch             = Stretch.Fill,
        };

        // Light neutral gray — approximates the average tone of a heavily-blurred
        // bright UI (WhatsApp's mostly-white background dominates).  Replaces the
        // earlier dark slate (RGB 32,44,51) which was visually distinct enough that
        // the brief cold-start ShowPlaceholder→first-blurred-frame transition read as
        // a jarring dark "flash" before blur appeared.  Privacy contract preserved
        // (still fully opaque).
        _placeholder = new Border
        {
            Background          = new SolidColorBrush(Color.FromRgb(220, 222, 220)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            IsHitTestVisible    = false,
            Visibility          = Visibility.Visible,
        };

        _contentRoot = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        _contentRoot.Children.Add(_image);
        _contentRoot.Children.Add(_placeholder);

        _window = new Window
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowState       = WindowState.Normal,
            WindowStyle       = WindowStyle.None,
            ResizeMode        = ResizeMode.NoResize,
            SizeToContent     = SizeToContent.Manual,
            AllowsTransparency = true,
            Background        = Brushes.Transparent,
            Topmost           = true,
            ShowInTaskbar     = false,
            Left   = OffscreenParkX,
            Top    = OffscreenParkY,
            Width  = 1,
            Height = 1,
            Content = _contentRoot,
        };

        // SourceInitialized fires once, after the HWND has been created (on Show()).
        _window.SourceInitialized += OnSourceInitialized;
    }

    private const int WM_NCHITTEST = 0x0084;
    private static readonly IntPtr HTTRANSPARENT = new(-1);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd    = new WindowInteropHelper(_window).Handle;
        int exStyle = NativeWindow.GetWindowLong(hwnd, NativeWindow.GWL_EXSTYLE);

        // WS_EX_LAYERED  — required companion for WS_EX_TRANSPARENT
        // WS_EX_TRANSPARENT — passes all mouse/keyboard input to windows below
        // WS_EX_TOOLWINDOW  — hides from taskbar and Alt-Tab switcher
        exStyle |= NativeWindow.WS_EX_LAYERED
                |  NativeWindow.WS_EX_TRANSPARENT
                |  NativeWindow.WS_EX_TOOLWINDOW;

        NativeWindow.SetWindowLong(hwnd, NativeWindow.GWL_EXSTYLE, exStyle);

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProcHook);
        DiagLog.Info("OverlayWindow: installed WM_NCHITTEST -> HTTRANSPARENT hook for non-client click-through");

        if (_isParked)
            ApplyBounds(GetParkedBounds(), "SourceInitialized offscreen park");
        else if (_requestedBounds.HasValue)
            ApplyBounds(_requestedBounds.Value, "SourceInitialized");
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            handled = true;
            return HTTRANSPARENT;
        }

        return IntPtr.Zero;
    }

    private void EnsureVisible()
    {
        if (!_window.IsVisible)
            _window.Show();
        else
            _window.Visibility = Visibility.Visible;
    }

    /// <inheritdoc/>
    public void ShowOffscreen(Rect initialBounds)
    {
        DiagLog.Info($"OverlayWindow.ShowOffscreen: entry, current Visibility={_window.Visibility}");
        ShowPlaceholder();
        _requestedBounds = initialBounds;
        _isParked = true;
        var parkedBounds = GetParkedBounds();
        ApplyWpfBoundsOnly(parkedBounds);
        EnsureVisible();
        ApplyBounds(parkedBounds, "ShowOffscreen post-HWND");

        var hwnd = WindowHandle;
        DiagLog.Info(
            "OverlayWindow.ShowOffscreen: " +
            $"HWND=0x{hwnd:X}, IsVisible={_window.IsVisible}, Visibility={_window.Visibility}, " +
            $"ParkX={parkedBounds.Left}, ParkY={parkedBounds.Top}");

        if (Interlocked.Exchange(ref _visualTreeLogged, 1) == 0)
            DiagLog.Info($"OverlayWindow.ShowOffscreen: visual tree = {DescribeVisualTree()}");

        LogActualSizeAfterApply("ShowOffscreen loaded");
    }

    /// <inheritdoc/>
    public void MoveToBounds(Rect bounds)
    {
        DiagLog.Info(
            "OverlayWindow.MoveToBounds: " +
            $"Left={bounds.Left}, Top={bounds.Top}, Width={bounds.Width}, Height={bounds.Height}");
        _requestedBounds = bounds;
        _isParked = false;
        EnsureVisible();
        ApplyBounds(bounds, "MoveToBounds");
    }

    /// <inheritdoc/>
    public void MoveOffscreen()
    {
        var parkedBounds = GetParkedBounds();
        DiagLog.Info(
            "OverlayWindow.MoveOffscreen: " +
            $"ParkX={parkedBounds.Left}, ParkY={parkedBounds.Top}, Width={parkedBounds.Width}, Height={parkedBounds.Height}");
        ShowPlaceholder();
        _isParked = true;
        EnsureVisible();
        ApplyBounds(parkedBounds, "MoveOffscreen");
    }

    /// <inheritdoc/>
    public void Destroy()
    {
        if (_disposed) return;

        DiagLog.Info($"OverlayWindow.Destroy: entry, HWND=0x{WindowHandle:X}, Visibility={_window.Visibility}");
        _hwndSource?.RemoveHook(WndProcHook);
        _hwndSource = null;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Close();
        CreateWindow();
        _requestedBounds = null;
        _isParked = true;
        DiagLog.Info("OverlayWindow.Destroy: recreated offscreen overlay window for next setup");
    }

    /// <inheritdoc/>
    public void ShowPlaceholder()
    {
        DiagLog.Info("OverlayWindow.ShowPlaceholder: showing opaque placeholder until next frame");
        _placeholder.Visibility = Visibility.Visible;
    }

    private void ApplyBounds(Rect bounds, string reason)
    {
        DiagLog.Info($"OverlayWindow.ApplyBounds ({reason}): Left={bounds.Left}, Top={bounds.Top}, Width={bounds.Width}, Height={bounds.Height}");
        _boundsApplier.Apply(_window, bounds, reason);
        LogActualSizeAfterApply(reason);
    }

    private void ApplyWpfBoundsOnly(Rect bounds)
    {
        _window.SizeToContent = SizeToContent.Manual;
        _window.MaxWidth = double.PositiveInfinity;
        _window.MaxHeight = double.PositiveInfinity;
        _window.Left = bounds.Left;
        _window.Top = bounds.Top;
        _window.Width = bounds.Width;
        _window.Height = bounds.Height;
    }

    private Rect GetParkedBounds()
    {
        var sizeSource = _requestedBounds ?? new Rect(0, 0, 1, 1);
        var width = Math.Max(1, sizeSource.Width);
        var height = Math.Max(1, sizeSource.Height);
        return new Rect(OffscreenParkX, OffscreenParkY, width, height);
    }

    private void LogActualSizeAfterApply(string reason)
    {
        _window.Dispatcher.BeginInvoke(
            () => DiagLog.Info(
                $"OverlayWindow.ApplyBounds ({reason}): actual size after apply = " +
                $"ActualWidth={_window.ActualWidth:0.#}, ActualHeight={_window.ActualHeight:0.#}, " +
                $"Width={_window.Width:0.#}, Height={_window.Height:0.#}, " +
                $"Grid={_contentRoot.ActualWidth:0.#}x{_contentRoot.ActualHeight:0.#}, " +
                $"Image={_image.ActualWidth:0.#}x{_image.ActualHeight:0.#}"),
            DispatcherPriority.Loaded);
    }

    /// <inheritdoc/>
    public void PresentFrame(IBlurRenderTarget target)
    {
        int frameNumber = Interlocked.Increment(ref _presentFrameCount);

        // CheckAccess() is true only when PresentFrame is called from the WPF UI thread,
        // which happens exclusively on the SetIntensity on-demand re-render path. Natural
        // capture frames always arrive from a background thread (CheckAccess() == false).
        bool isOnDemand = _window.Dispatcher.CheckAccess();
        bool shouldLog = frameNumber <= 5 || frameNumber % 30 == 0 || isOnDemand;

        // D3DImage.Lock/SetBackBuffer/Unlock must run on the UI thread.
        try
        {
            _window.Dispatcher.Invoke(() =>
            {
                long invokeEnteredAt = Stopwatch.GetTimestamp();

                if (shouldLog)
                {
                    string kind = isOnDemand ? "ON-DEMAND" : "capture";
                    DiagLog.Info($"PresentFrame #{frameNumber} [{kind}]: dims={target.Width}x{target.Height}");
                    DiagLog.Info($"PresentFrame #{frameNumber}: D3DImage.IsFrontBufferAvailable={_d3dImage.IsFrontBufferAvailable}, PixelWidth={_d3dImage.PixelWidth}, PixelHeight={_d3dImage.PixelHeight}");
                    DiagLog.Info($"PresentFrame #{frameNumber}: Image.ActualSize={_image.ActualWidth:0.#}x{_image.ActualHeight:0.#}, IsMeasureValid={_image.IsMeasureValid}, IsArrangeValid={_image.IsArrangeValid}");
                    DiagLog.Info($"PresentFrame #{frameNumber}: Window.ActualSize={_window.ActualWidth:0.#}x{_window.ActualHeight:0.#}, IsVisible={_window.IsVisible}");
                }

                _bridge.UpdateD3DImage(_d3dImage, target);

                // Without an explicit InvalidateVisual, WPF's render thread only picks up
                // the D3DImage dirty region at its next scheduled pass. For the natural
                // capture path (60fps) that pass is always pending, so the update appears
                // within one VBlank. For on-demand re-renders triggered between capture
                // frames, no render pass is scheduled and the new pixels would sit invisible
                // until the next natural frame arrives (~1 second). InvalidateVisual
                // schedules a render pass at DispatcherPriority.Render so any caller —
                // including the tray-menu intensity preset path — gets an immediate repaint.
                _image.InvalidateVisual();
                if (shouldLog)
                    DiagLog.Info($"PresentFrame #{frameNumber}: InvalidateVisual called on Image element");

                if (isOnDemand)
                    SubscribeCompositionRendering(frameNumber, invokeEnteredAt);

                if (_placeholder.Visibility != Visibility.Collapsed)
                {
                    _placeholder.Visibility = Visibility.Collapsed;
                    DiagLog.Info($"PresentFrame #{frameNumber}: hid opaque placeholder after frame present");
                }
            });
        }
        catch (Exception ex)
        {
            int exceptionCount = Interlocked.Increment(ref _presentExceptionCount);
            DiagLog.Warn($"PresentFrame #{frameNumber}: EXCEPTION {ex.GetType().FullName}: {ex.Message} (exception #{exceptionCount})");
            DiagLog.Warn($"PresentFrame #{frameNumber}: stack: {ex.StackTrace}");
            // Do NOT re-throw — one bad frame must not crash the app.
        }
    }

    // Called from inside Dispatcher.Invoke on the UI thread. Hooks CompositionTarget.Rendering
    // and logs the first MaxRenderingFirings ticks after an on-demand InvalidateVisual so we
    // can measure how long the WPF compositor takes to schedule the render pass.
    private void SubscribeCompositionRendering(int frameNumber, long baseTimestamp)
    {
        if (_compositionRenderingHandler is not null)
        {
            CompositionTarget.Rendering -= _compositionRenderingHandler;
            _compositionRenderingHandler = null;
            DiagLog.Info($"PresentFrame #{frameNumber}: replaced previous CompositionTarget.Rendering subscription");
        }

        _compositionRenderingFiringsLeft = MaxRenderingFirings;

        _compositionRenderingHandler = (_, _) =>
        {
            int left = --_compositionRenderingFiringsLeft;
            int firingNumber = MaxRenderingFirings - left;
            double elapsed = (Stopwatch.GetTimestamp() - baseTimestamp) * 1000.0 / Stopwatch.Frequency;
            DiagLog.Info($"CompositionTarget.Rendering #{firingNumber} (after ON-DEMAND frame #{frameNumber}): {elapsed:F3} ms since InvalidateVisual");

            if (left <= 0)
            {
                CompositionTarget.Rendering -= _compositionRenderingHandler;
                _compositionRenderingHandler = null;
                DiagLog.Info($"CompositionTarget.Rendering: unsubscribed after {MaxRenderingFirings} firings");
            }
        };

        CompositionTarget.Rendering += _compositionRenderingHandler;
        DiagLog.Info($"PresentFrame #{frameNumber}: CompositionTarget.Rendering subscribed — will log next {MaxRenderingFirings} firings");
    }

    private string DescribeVisualTree()
    {
        var sb = new StringBuilder(capacity: 500);
        AppendElement(_window.Content as DependencyObject, sb);

        if (sb.Length > 500)
            return sb.ToString()[..497] + "...";

        return sb.ToString();
    }

    private static void AppendElement(DependencyObject? element, StringBuilder sb)
    {
        if (element is null || sb.Length >= 500) return;

        if (sb.Length > 0) sb.Append(" > ");
        sb.Append(element.GetType().Name);

        if (element is FrameworkElement frameworkElement)
            sb.Append($"({frameworkElement.ActualWidth:0.#}x{frameworkElement.ActualHeight:0.#})");

        if (element is Image image)
            sb.Append($" Source={image.Source?.GetType().Name ?? "null"}");

        int childCount = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < childCount && sb.Length < 500; i++)
            AppendElement(VisualTreeHelper.GetChild(element, i), sb);
    }

    /// <inheritdoc/>
    public void SetClip(IReadOnlyList<Rect>? visibleRects)
    {
        if (visibleRects is null || visibleRects.Count == 0)
        {
            _contentRoot.Clip = null;
            DiagLog.Info("OverlayWindow.SetClip: clip cleared");
            return;
        }

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var r in visibleRects)
            group.Children.Add(new RectangleGeometry(r));
        group.Freeze();

        _contentRoot.Clip = group;
        DiagLog.Info($"OverlayWindow.SetClip: clip set to {visibleRects.Count} rect(s)");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bridge.Dispose();
        _hwndSource?.RemoveHook(WndProcHook);
        _hwndSource = null;
        _window.SourceInitialized -= OnSourceInitialized;
        // BeginInvoke avoids deadlock when Dispose is called from the UI thread.
        _window.Dispatcher.BeginInvoke(_window.Close);
    }
}
