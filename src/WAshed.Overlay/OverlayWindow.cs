using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;
using WAshed.Core.Blur;
using WAshed.Core.Diagnostics;
using WAshed.Overlay.Interop;

namespace WAshed.Overlay;

/// <summary>
/// Transparent, always-on-top, click-through WPF window that renders a
/// <see cref="D3DImage"/> fed by <see cref="IBlurRenderTarget"/> frames.
/// </summary>
/// <remarks>
/// <para>
/// Must be constructed and <see cref="Show"/>n on the WPF UI (STA) thread.
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
    private readonly Window        _window;
    private readonly Image         _image;
    private readonly D3DImage      _d3dImage;
    private readonly ID3DImageBridge _bridge;
    private bool _disposed;

    private int _presentFrameCount;
    private int _presentExceptionCount;
    private int _visualTreeLogged;

    /// <summary>Creates an overlay with the default GPU bridge.</summary>
    public OverlayWindow() : this(null) { }

    /// <param name="bridge">Bridge override for internal/test use; must not be null.</param>
    internal OverlayWindow(ID3DImageBridge? bridge)
    {
        _bridge = bridge ?? new D3DImageBridge();
        _d3dImage = new D3DImage();
        _image    = new Image
        {
            Source              = _d3dImage,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Stretch             = Stretch.Fill,
        };

        var contentRoot = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        contentRoot.Children.Add(_image);

        _window = new Window
        {
            WindowStyle       = WindowStyle.None,
            SizeToContent     = SizeToContent.Manual,
            AllowsTransparency = true,
            Background        = Brushes.Transparent,
            Topmost           = true,
            ShowInTaskbar     = false,
            // Start off-screen at zero size; caller drives position via SetBounds.
            Left   = 0,
            Top    = 0,
            Width  = 0,
            Height = 0,
            Content = contentRoot,
        };

        // SourceInitialized fires once, after the HWND has been created (on Show()).
        _window.SourceInitialized += OnSourceInitialized;
    }

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
    }

    /// <inheritdoc/>
    public void Show()
    {
        DiagLog.Info($"OverlayWindow.Show: entry, current Visibility={_window.Visibility}");
        _window.Show();
        var hwnd = new WindowInteropHelper(_window).Handle;
        DiagLog.Info($"OverlayWindow.Show: window shown, HWND=0x{hwnd:X}, IsVisible={_window.IsVisible}");

        if (Interlocked.Exchange(ref _visualTreeLogged, 1) == 0)
            DiagLog.Info($"OverlayWindow.Show: visual tree = {DescribeVisualTree()}");

        _window.Dispatcher.BeginInvoke(
            () => DiagLog.Info($"OverlayWindow.Show: post-show image size = {_image.ActualWidth:0.#}x{_image.ActualHeight:0.#}"),
            DispatcherPriority.Loaded);
    }

    /// <inheritdoc/>
    public void Hide() => _window.Hide();

    /// <inheritdoc/>
    public void SetBounds(Rect bounds)
    {
        DiagLog.Info($"OverlayWindow.SetBounds: setting to Left={bounds.Left}, Top={bounds.Top}, Width={bounds.Width}, Height={bounds.Height}");
        _window.Left   = bounds.Left;
        _window.Top    = bounds.Top;
        _window.Width  = bounds.Width;
        _window.Height = bounds.Height;
    }

    /// <inheritdoc/>
    public void PresentFrame(IBlurRenderTarget target)
    {
        int frameNumber = Interlocked.Increment(ref _presentFrameCount);
        bool shouldLog = frameNumber <= 5 || frameNumber % 30 == 0;

        // D3DImage.Lock/SetBackBuffer/Unlock must run on the UI thread.
        try
        {
            _window.Dispatcher.Invoke(() =>
            {
                if (shouldLog)
                {
                    DiagLog.Info($"PresentFrame #{frameNumber}: dims={target.Width}x{target.Height}");
                    DiagLog.Info($"PresentFrame #{frameNumber}: D3DImage.IsFrontBufferAvailable={_d3dImage.IsFrontBufferAvailable}, PixelWidth={_d3dImage.PixelWidth}, PixelHeight={_d3dImage.PixelHeight}");
                }

                _bridge.UpdateD3DImage(_d3dImage, target);
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
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bridge.Dispose();
        // BeginInvoke avoids deadlock when Dispose is called from the UI thread.
        _window.Dispatcher.BeginInvoke(_window.Close);
    }
}
