using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

public sealed class BlurPipeline : IBlurPipeline
{
    public const float DefaultBlurRadius = 20.0f;

    private readonly IBlurInterop _interop;
    private bool _initialized;
    private bool _disposed;
    private IBlurCanvasDevice? _canvasDevice;
    private IBlurRenderTarget? _renderTarget;

    public float BlurRadius { get; set; } = DefaultBlurRadius;

    public BlurPipeline(IBlurInterop interop)
    {
        _interop = interop;
    }

    public void Initialize(IDirect3DDevice device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _canvasDevice = _interop.CreateCanvasDevice(device);
        _initialized = true;
    }

    public IBlurRenderTarget BlurFrame(ICaptureFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            throw new InvalidOperationException("BlurPipeline has not been initialized. Call Initialize() first.");

        var (width, height) = _interop.GetFrameSize(frame);

        if (_renderTarget is null || _renderTarget.Width != width || _renderTarget.Height != height)
        {
            _renderTarget?.Dispose();
            _renderTarget = _interop.CreateRenderTarget(_canvasDevice!, width, height);
        }

        _interop.DrawBlur(_canvasDevice!, _renderTarget, frame, BlurRadius);
        return _renderTarget;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderTarget?.Dispose();
        _canvasDevice?.Dispose();
        _renderTarget = null;
        _canvasDevice = null;
    }
}
