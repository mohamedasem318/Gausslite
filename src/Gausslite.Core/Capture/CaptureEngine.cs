using Gausslite.Core.Diagnostics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Capture;

public sealed class CaptureEngine : ICaptureEngine, IDisposable
{
    private readonly ICaptureInterop _interop;
    private readonly IDirect3DDevice _device;

    private ICaptureFramePool? _pool;
    private ICaptureSession? _session;
    private SizeInt32? _lastContentSize;
    private int _frameCount;

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

        try
        {
            DiagLog.Info($"CaptureEngine.Start: GraphicsCaptureSession.IsSupported = {GraphicsCaptureSession.IsSupported()}");
        }
        catch (Exception ex)
        {
            DiagLog.Warn("CaptureEngine.Start: GraphicsCaptureSession.IsSupported threw while logging support status", ex);
        }

        DiagLog.Info("CaptureEngine.Start: creating frame pool...");
        _pool = _interop.CreateFreeThreadedFramePool(_device, item);
        _lastContentSize = item is null ? null : item.Size;
        DiagLog.Info("CaptureEngine.Start: frame pool created");

        DiagLog.Info("CaptureEngine.Start: subscribing to FrameArrived...");
        _pool.FrameArrived += OnFrameArrived;

        DiagLog.Info("CaptureEngine.Start: starting capture session...");
        _session = _interop.CreateSession(_pool, item!);
        _session.IsBorderRequired = false;
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
        _lastContentSize = null;
        _frameCount = 0;
        _firstWgcFrameLogged = 0;
    }

    private void OnFrameArrived(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _firstWgcFrameLogged, 1) == 0)
            DiagLog.Info("CaptureEngine: first WGC frame received.");

        var frame = _pool?.TryGetNextFrame();
        if (frame is null) return;

        int frameNumber = Interlocked.Increment(ref _frameCount);
        var contentSize = frame.ContentSize;
        DiagLog.Info($"CaptureEngine.FrameArrived #{frameNumber}: incoming WGC ContentSize={contentSize.Width}x{contentSize.Height}");

        try
        {
            if (DidContentSizeChange(contentSize))
            {
                DiagLog.Info(
                    "CaptureEngine.FrameArrived " +
                    $"#{frameNumber}: ContentSize changed from {DescribeSize(_lastContentSize)} " +
                    $"to {contentSize.Width}x{contentSize.Height}; recreating frame pool and dropping transition frame");

                _pool?.Recreate(_device, contentSize);
                _lastContentSize = contentSize;
                return;
            }

            _lastContentSize ??= contentSize;
            FrameArrived?.Invoke(this, frame);
        }
        finally
        {
            frame.Dispose();
        }
    }

    private bool DidContentSizeChange(SizeInt32 contentSize)
    {
        return _lastContentSize.HasValue
            && (_lastContentSize.Value.Width != contentSize.Width
                || _lastContentSize.Value.Height != contentSize.Height);
    }

    private static string DescribeSize(SizeInt32? size)
    {
        return size.HasValue ? $"{size.Value.Width}x{size.Value.Height}" : "<unknown>";
    }

    public void Dispose() => Stop();
}
