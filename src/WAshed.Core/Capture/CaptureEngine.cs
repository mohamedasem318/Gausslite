using WAshed.Core.Diagnostics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace WAshed.Core.Capture;

public sealed class CaptureEngine : ICaptureEngine, IDisposable
{
    private readonly ICaptureInterop _interop;
    private readonly IDirect3DDevice _device;

    private ICaptureFramePool? _pool;
    private ICaptureSession? _session;

    // Logs first WGC frame arrival once; 0 = not yet logged, 1 = logged.
    private int _firstWgcFrameLogged;

    public event EventHandler<ICaptureFrame>? FrameArrived;

    public bool IsCapturing { get; private set; }

    public CaptureEngine(ICaptureInterop interop, IDirect3DDevice device)
    {
        _interop = interop;
        _device = device;
    }

    public void Start(GraphicsCaptureItem item)
    {
        DiagLog.Info("CaptureEngine.Start: entry");

        if (IsCapturing)
            throw new InvalidOperationException("Capture is already running. Call Stop() before calling Start() again.");

        DiagLog.Info($"CaptureEngine.Start: GraphicsCaptureSession.IsSupported = {GraphicsCaptureSession.IsSupported()}");

        DiagLog.Info("CaptureEngine.Start: creating frame pool...");
        _pool = _interop.CreateFreeThreadedFramePool(_device, item);
        DiagLog.Info("CaptureEngine.Start: frame pool created");

        DiagLog.Info("CaptureEngine.Start: subscribing to FrameArrived...");
        _pool.FrameArrived += OnFrameArrived;

        DiagLog.Info("CaptureEngine.Start: starting capture session...");
        _session = _interop.CreateSession(_pool, item);
        _session.StartCapture();
        DiagLog.Info("CaptureEngine.Start: capture session started, returning");

        IsCapturing = true;
    }

    public void Stop()
    {
        if (!IsCapturing) return;

        IsCapturing = false;

        if (_pool is not null)
            _pool.FrameArrived -= OnFrameArrived;

        _session?.Dispose();
        _session = null;

        _pool?.Dispose();
        _pool = null;
    }

    private void OnFrameArrived(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _firstWgcFrameLogged, 1) == 0)
            DiagLog.Info("CaptureEngine: first WGC frame received.");

        var frame = _pool?.TryGetNextFrame();
        if (frame is null) return;

        try
        {
            FrameArrived?.Invoke(this, frame);
        }
        finally
        {
            frame.Dispose();
        }
    }

    public void Dispose() => Stop();
}
