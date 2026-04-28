using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace WAshed.Core.Capture;

public sealed class CaptureEngine : ICaptureEngine, IDisposable
{
    private readonly ICaptureInterop _interop;
    private readonly IDirect3DDevice _device;

    private ICaptureFramePool? _pool;
    private ICaptureSession? _session;

    public event EventHandler<ICaptureFrame>? FrameArrived;

    public bool IsCapturing { get; private set; }

    public CaptureEngine(ICaptureInterop interop, IDirect3DDevice device)
    {
        _interop = interop;
        _device = device;
    }

    public void Start(GraphicsCaptureItem item)
    {
        if (IsCapturing)
            throw new InvalidOperationException("Capture is already running. Call Stop() before calling Start() again.");

        _pool = _interop.CreateFreeThreadedFramePool(_device, item);
        _pool.FrameArrived += OnFrameArrived;

        _session = _interop.CreateSession(_pool, item);
        _session.StartCapture();

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
