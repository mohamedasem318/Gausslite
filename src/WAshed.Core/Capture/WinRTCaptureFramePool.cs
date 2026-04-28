using Windows.Foundation;
using Windows.Graphics.Capture;

namespace WAshed.Core.Capture;

internal sealed class WinRTCaptureFramePool : ICaptureFramePool
{
    private readonly Direct3D11CaptureFramePool _pool;

    // Bridged event handler — kept so we can remove it from the native pool cleanly.
    private EventHandler? _subscribers;
    private TypedEventHandler<Direct3D11CaptureFramePool, object>? _nativeHandler;

    internal WinRTCaptureFramePool(Direct3D11CaptureFramePool pool) => _pool = pool;

    internal Direct3D11CaptureFramePool NativePool => _pool;

    public event EventHandler? FrameArrived
    {
        add
        {
            if (_subscribers == null)
            {
                _nativeHandler = (_, _) => _subscribers?.Invoke(this, EventArgs.Empty);
                _pool.FrameArrived += _nativeHandler;
            }
            _subscribers += value;
        }
        remove
        {
            _subscribers -= value;
            if (_subscribers == null && _nativeHandler != null)
            {
                _pool.FrameArrived -= _nativeHandler;
                _nativeHandler = null;
            }
        }
    }

    public ICaptureFrame? TryGetNextFrame()
    {
        var frame = _pool.TryGetNextFrame();
        return frame is null ? null : new WinRTCaptureFrame(frame);
    }

    public void Dispose() => _pool.Dispose();
}
