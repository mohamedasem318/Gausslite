// SPDX-License-Identifier: AGPL-3.0-or-later
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Capture;

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

    public void Recreate(IDirect3DDevice device, SizeInt32 size)
    {
        _pool.Recreate(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            size);
    }

    public void Dispose() => _pool.Dispose();
}
