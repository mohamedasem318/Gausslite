using System.Windows;
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
        _isBlurEnabled = true;

        _windowTracker.BoundsChanged += OnBoundsChanged;
        _windowTracker.Start();

        if (_captureItemFactory.TryCreateForWhatsApp(out var item))
            StartCapture(item!);

        BlurStateChanged?.Invoke(this, true);
    }

    public void DisableBlur()
    {
        if (!_isBlurEnabled) return;
        _isBlurEnabled = false;

        StopCapture();
        _windowTracker.BoundsChanged -= OnBoundsChanged;
        _windowTracker.Stop();

        BlurStateChanged?.Invoke(this, false);
    }

    private void StartCapture(GraphicsCaptureItem item)
    {
        if (_captureStarted) return;
        _captureStarted = true;
        _captureEngine.FrameArrived += OnFrameArrived;
        _captureEngine.Start(item);
        _overlayWindow.Show();
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
        _overlayWindow.SetBounds(bounds);

        // WhatsApp may not have been running when blur was enabled; try to start capture now.
        if (!_captureStarted && _captureItemFactory.TryCreateForWhatsApp(out var item))
            StartCapture(item!);
    }

    private void OnFrameArrived(object? sender, ICaptureFrame frame)
    {
        var blurred = _blurPipeline.BlurFrame(frame);
        _overlayWindow.PresentFrame(blurred);
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
