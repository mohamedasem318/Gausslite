using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Gausslite.App.Diagnostics;
using Gausslite.App.Hotkey;
using Gausslite.App.Orchestration;
using Gausslite.App.Tray;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.App;

public partial class App : Application
{
    private TrayIconHost? _trayIconHost;
    private ITrayOrchestrator? _orchestrator;
    private IHotkeyService? _hotkeyService;

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupLog.Info("OnStartup begin");
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception, fatal: true);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception, fatal: false);
            args.Handled = true;
        };

        // --- Composition root ---
        // The only place new ConcreteType() is allowed for module types.

        // Create a shared D3D11 device (BGRA support required by Win2D).
        // Both CaptureEngine and BlurPipeline use this device so captured textures
        // can be fed directly into Win2D drawing sessions without a cross-device copy.
        IDirect3DDevice d3dDevice = CreateD3D11Device();

        IWindowTracker windowTracker = new WindowTracker(new Win32Api());

        var captureInterop = new WinRTCaptureInterop();
        ICaptureEngine captureEngine = new CaptureEngine(captureInterop, d3dDevice);

        var blurInterop = new Win2DBlurInterop();
        IBlurPipeline blurPipeline = new BlurPipeline(blurInterop);
        blurPipeline.Initialize(d3dDevice);

        IOverlayWindow overlayWindow = new OverlayWindow();
        ICaptureItemFactory captureItemFactory = new CaptureItemFactory(new Win32Api());

        _hotkeyService = new HotkeyService();

        try
        {
            StartupLog.Info("Constructing TrayOrchestrator...");
            _orchestrator = new TrayOrchestrator(
                windowTracker,
                captureEngine,
                blurPipeline,
                overlayWindow,
                _hotkeyService,
                captureItemFactory);
            StartupLog.Info("TrayOrchestrator constructed.");
        }
        catch (Exception ex)
        {
            StartupLog.Warn("TrayOrchestrator construction threw.", ex);
            throw;
        }

        try
        {
            StartupLog.Info("Constructing TrayIconHost...");
            _trayIconHost = new TrayIconHost(_orchestrator);
            StartupLog.Info("TrayIconHost constructed.");
        }
        catch (Exception ex)
        {
            StartupLog.Warn("TrayIconHost construction threw.", ex);
            throw;
        }

        try
        {
            StartupLog.Info("Initializing tray icon...");
            _trayIconHost.Initialize();
            StartupLog.Info("Tray icon Initialize() returned.");
        }
        catch (Exception ex)
        {
            StartupLog.Warn("TrayIconHost.Initialize() threw.", ex);
            throw;
        }

        StartupLog.Info("OnStartup complete.");
    }

    private static void LogCrash(Exception? ex, bool fatal)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "gausslite-crash.log");
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL" : "DISPATCHER")} unhandled exception");
        for (var e = ex; e is not null; e = e.InnerException)
            lines.AppendLine($"  {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e.StackTrace}");

        var entry = lines.ToString();
        Debug.WriteLine($"[Gausslite] {entry}");
        try { File.AppendAllText(logPath, entry); }
        catch { /* never throw from crash logger */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconHost?.Dispose();
        _orchestrator?.Dispose();
        _hotkeyService?.Dispose();
        base.OnExit(e);
    }

    // ── D3D11 device factory ──────────────────────────────────────────────────

    private static IDirect3DDevice CreateD3D11Device()
    {
        // D3D11_CREATE_DEVICE_BGRA_SUPPORT (0x20) is mandatory for Win2D.
        const int  D3D_DRIVER_TYPE_HARDWARE        = 1;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        const uint D3D11_SDK_VERSION               = 7;

        int hr = D3D11CreateDevice(
            pAdapter:         IntPtr.Zero,
            DriverType:       D3D_DRIVER_TYPE_HARDWARE,
            Software:         IntPtr.Zero,
            Flags:            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            pFeatureLevels:   IntPtr.Zero,
            FeatureLevels:    0,
            SDKVersion:       D3D11_SDK_VERSION,
            ppDevice:         out IntPtr devicePtr,
            pFeatureLevel:    IntPtr.Zero,
            ppImmediateContext: IntPtr.Zero);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            // D3D11 device also implements IDXGIDevice; QI for it to pass to the WinRT wrapper.
            var dxgiGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(devicePtr, ref dxgiGuid, out IntPtr dxgiDevicePtr);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                return new WinRTCaptureInterop().CreateDirect3DDevice(dxgiDevicePtr);
            }
            finally
            {
                Marshal.Release(dxgiDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(devicePtr);
        }
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,
        IntPtr Software,
        uint Flags,
        IntPtr pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        IntPtr pFeatureLevel,
        IntPtr ppImmediateContext);

    // -------------------------------------------------------------------------
    // Null-object stubs — kept as internal test fakes; NOT used in production.
    // -------------------------------------------------------------------------

    internal sealed class NullCaptureEngine : ICaptureEngine
    {
        public event EventHandler<ICaptureFrame>? FrameArrived { add { } remove { } }
        public bool IsCapturing => false;
        public void Start(GraphicsCaptureItem item) { }
        public void Stop() { }
    }

    internal sealed class NullBlurPipeline : IBlurPipeline
    {
        public float BlurRadius { get; set; } = 20f;
        public void Initialize(IDirect3DDevice device) { }
        public IBlurRenderTarget BlurFrame(ICaptureFrame frame)
            => throw new NotSupportedException("NullBlurPipeline does not process frames.");
        public void Dispose() { }
    }
}
