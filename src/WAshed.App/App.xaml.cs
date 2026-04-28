using System.Windows;
using WAshed.App.Hotkey;
using WAshed.App.Orchestration;
using WAshed.App.Tray;
using WAshed.Core.Blur;
using WAshed.Core.Capture;
using WAshed.Core.WindowTracking;
using WAshed.Overlay;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace WAshed.App;

public partial class App : Application
{
    private TrayIconHost? _trayIconHost;
    private ITrayOrchestrator? _orchestrator;
    private IHotkeyService? _hotkeyService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // --- Composition root ---
        // The only place new ConcreteType() is allowed for module types.
        //
        // TODO (next session): replace NullCaptureEngine and NullBlurPipeline with real
        // Win2D-backed implementations once the concrete IBlurRenderTarget wrapper
        // (CanvasRenderTarget + DXGI_RESOURCE_MISC_SHARED) is built.

        IWindowTracker windowTracker = new WindowTracker(new Win32Api());
        ICaptureEngine captureEngine = new NullCaptureEngine();
        IBlurPipeline blurPipeline = new NullBlurPipeline();
        IOverlayWindow overlayWindow = new OverlayWindow();
        ICaptureItemFactory captureItemFactory = new CaptureItemFactory(new Win32Api());

        _hotkeyService = new HotkeyService();

        _orchestrator = new TrayOrchestrator(
            windowTracker,
            captureEngine,
            blurPipeline,
            overlayWindow,
            _hotkeyService,
            captureItemFactory);

        _trayIconHost = new TrayIconHost(_orchestrator);
        _trayIconHost.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconHost?.Dispose();
        _orchestrator?.Dispose();
        _hotkeyService?.Dispose();
        base.OnExit(e);
    }

    // -------------------------------------------------------------------------
    // Null-object stubs — keep here until real GPU pipeline is implemented.
    // -------------------------------------------------------------------------

    private sealed class NullCaptureEngine : ICaptureEngine
    {
        // Explicit add/remove avoids CS0067 "event never used".
        public event EventHandler<ICaptureFrame>? FrameArrived { add { } remove { } }
        public bool IsCapturing => false;
        public void Start(GraphicsCaptureItem item) { }
        public void Stop() { }
    }

    private sealed class NullBlurPipeline : IBlurPipeline
    {
        public float BlurRadius { get; set; } = 20f;

        public void Initialize(IDirect3DDevice device) { }

        public IBlurRenderTarget BlurFrame(ICaptureFrame frame)
            => throw new NotSupportedException("GPU blur pipeline not yet implemented — build Win2D wrapper first.");

        public void Dispose() { }
    }
}
