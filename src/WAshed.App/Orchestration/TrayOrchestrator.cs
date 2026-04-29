using System.Windows;
using WAshed.App.Diagnostics;
using WAshed.App.Hotkey;
using WAshed.Core.Blur;
using WAshed.Core.Capture;
using WAshed.Core.WindowTracking;
using WAshed.Overlay;
using Windows.Graphics.Capture;

namespace WAshed.App.Orchestration;

/// <summary>
/// Wires together <see cref="IWindowTracker"/>, <see cref="ICaptureEngine"/>,
/// <see cref="IBlurPipeline"/>, and <see cref="IOverlayWindow"/>.
/// All public methods must be called from the WPF UI thread.
/// </summary>
public sealed class TrayOrchestrator : ITrayOrchestrator
{
    private readonly IWindowTracker _windowTracker;
    private readonly ICaptureEngine _captureEngine;
    private readonly IBlurPipeline _blurPipeline;
    private readonly IOverlayWindow _overlayWindow;
    private readonly IHotkeyService _hotkeyService;
    private readonly ICaptureItemFactory _captureItemFactory;

    private bool _isBlurEnabled;
    private bool _captureStarted;
    private bool _disposed;

    // Frame-arrival diagnostics — written from background thread, use Interlocked.
    private int _frameCount;
    private int _firstFrameLogged;   // 0 = not yet logged, 1 = logged
    private int _noOutputLogged;     // 0 = not yet logged, 1 = logged

    public event EventHandler<bool>? BlurStateChanged;

    public bool IsBlurEnabled => _isBlurEnabled;

    public TrayOrchestrator(
        IWindowTracker windowTracker,
        ICaptureEngine captureEngine,
        IBlurPipeline blurPipeline,
        IOverlayWindow overlayWindow,
        IHotkeyService hotkeyService,
        ICaptureItemFactory captureItemFactory)
    {
        _windowTracker = windowTracker;
        _captureEngine = captureEngine;
        _blurPipeline = blurPipeline;
        _overlayWindow = overlayWindow;
        _hotkeyService = hotkeyService;
        _captureItemFactory = captureItemFactory;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    public void ToggleBlur()
    {
        if (_isBlurEnabled) DisableBlur();
        else EnableBlur();
    }

    public void EnableBlur()
    {
        if (_isBlurEnabled) return;
        StartupLog.Info("EnableBlur: entry");
        _isBlurEnabled = true;

        StartupLog.Info("EnableBlur: starting WindowTracker...");
        _windowTracker.BoundsChanged += OnBoundsChanged;
        _windowTracker.Start();
        StartupLog.Info("EnableBlur: WindowTracker started");

        StartupLog.Info("EnableBlur: calling CaptureItemFactory.TryCreateForWhatsApp...");
        var success = _captureItemFactory.TryCreateForWhatsApp(out var item);
        StartupLog.Info($"EnableBlur: TryCreateForWhatsApp returned success={success}, itemNull={item is null}");

        if (!success)
        {
            StartupLog.Info("EnableBlur: ABORTING — no capture item available (WhatsApp not running yet; will retry via BoundsChanged)");
        }
        else
        {
            StartCapture(item!);
        }

        BlurStateChanged?.Invoke(this, true);
        StartupLog.Info("EnableBlur: complete");
    }

    public void DisableBlur()
    {
        StartupLog.Info("DisableBlur: entry");
        if (!_isBlurEnabled) return;
        _isBlurEnabled = false;

        StopCapture();
        _windowTracker.BoundsChanged -= OnBoundsChanged;
        _windowTracker.Stop();

        BlurStateChanged?.Invoke(this, false);
        StartupLog.Info("DisableBlur: complete");
    }

    private void StartCapture(GraphicsCaptureItem item)
    {
        if (_captureStarted) return;
        _captureStarted = true;

        StartupLog.Info("EnableBlur: starting CaptureEngine on captured item...");
        _captureEngine.FrameArrived += OnFrameArrived;
        _captureEngine.Start(item);
        StartupLog.Info("EnableBlur: CaptureEngine.Start returned");

        StartupLog.Info("EnableBlur: showing OverlayWindow...");
        _overlayWindow.Show();
        StartupLog.Info("EnableBlur: OverlayWindow.Show returned");
    }

    private void StopCapture()
    {
        if (!_captureStarted) return;
        _captureStarted = false;
        _captureEngine.FrameArrived -= OnFrameArrived;
        _captureEngine.Stop();
        _overlayWindow.Hide();
    }

    private void OnBoundsChanged(object? sender, Rect bounds)
    {
        StartupLog.Info($"EnableBlur: setting OverlayWindow bounds to {bounds}");
        _overlayWindow.SetBounds(bounds);

        // WhatsApp may not have been running when blur was enabled; try to start capture now.
        if (!_captureStarted && _captureItemFactory.TryCreateForWhatsApp(out var item))
            StartCapture(item!);
    }

    private void OnFrameArrived(object? sender, ICaptureFrame frame)
    {
        try
        {
            // Log first frame received.
            if (Interlocked.Exchange(ref _firstFrameLogged, 1) == 0)
            {
                var sz = frame.Frame.ContentSize;
                StartupLog.Info($"FrameArrived: first frame received, dimensions={sz.Width}x{sz.Height}");
            }

            // Log once per 60 frames (~1 s at 60 fps).
            int count = Interlocked.Increment(ref _frameCount);
            if (count % 60 == 0)
                StartupLog.Info($"FrameArrived: processed {count} frames so far");

            var blurred = _blurPipeline.BlurFrame(frame);

            if (blurred is null)
            {
                if (Interlocked.Exchange(ref _noOutputLogged, 1) == 0)
                    StartupLog.Warn("FrameArrived: blur pipeline returned no output");
                return;
            }

            _overlayWindow.PresentFrame(blurred);
        }
        catch (Exception ex)
        {
            StartupLog.Warn("FrameArrived: unhandled exception", ex);
            throw;
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e) => ToggleBlur();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;

        if (_isBlurEnabled)
        {
            StopCapture();
            _windowTracker.BoundsChanged -= OnBoundsChanged;
            _windowTracker.Stop();
            _isBlurEnabled = false;
        }

        // ICaptureEngine and IWindowTracker don't expose Dispose on their interfaces;
        // Stop() handles their internal resource cleanup.
        _blurPipeline.Dispose();
        _overlayWindow.Dispose();
    }
}
