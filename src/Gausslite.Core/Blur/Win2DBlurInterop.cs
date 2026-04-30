using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

/// <summary>
/// Concrete <see cref="IBlurInterop"/> backed by Win2D (Microsoft.Graphics.Win2D).
/// Handles CanvasDevice lifetime, render-target creation, and Gaussian blur rendering.
/// </summary>
public sealed class Win2DBlurInterop : IBlurInterop
{
    /// <inheritdoc/>
    public IBlurCanvasDevice CreateCanvasDevice(IDirect3DDevice device)
    {
        // CreateFromDirect3D11Device wraps the existing D3D11 device rather than allocating a new one,
        // ensuring Win2D and the CaptureEngine share the same GPU device and can exchange surfaces.
        var canvasDevice = CanvasDevice.CreateFromDirect3D11Device(device);
        return new Win2DCanvasDeviceWrapper(canvasDevice, device);
    }

    /// <inheritdoc/>
    public IBlurRenderTarget CreateRenderTarget(IBlurCanvasDevice canvasDevice, float width, float height)
    {
        var wrapper = (Win2DCanvasDeviceWrapper)canvasDevice;
        return new Win2DBlurRenderTarget(wrapper.CanvasDevice, wrapper.Direct3DDevice, width, height);
    }

    /// <inheritdoc/>
    public void DrawBlur(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget, ICaptureFrame frame, float radius)
    {
        var device = ((Win2DCanvasDeviceWrapper)canvasDevice).CanvasDevice;
        var rt     = (Win2DBlurRenderTarget)renderTarget;

        // Create a temporary CanvasBitmap from the captured frame surface.
        // CanvasBitmap.CreateFromDirect3D11Surface wraps the IDirect3DSurface without copying pixels.
        using var frameBitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Frame.Surface);

        var blurEffect = new GaussianBlurEffect
        {
            Source     = frameBitmap,
            BlurAmount = radius,
        };

        using var session = rt.CanvasRenderTarget.CreateDrawingSession();
        session.DrawImage(blurEffect);
    }

    /// <inheritdoc/>
    public (float Width, float Height) GetFrameSize(ICaptureFrame frame)
    {
        var size = frame.ContentSize;
        return ((float)size.Width, (float)size.Height);
    }
}
