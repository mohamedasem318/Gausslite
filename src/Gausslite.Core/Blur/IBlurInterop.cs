using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

/// <summary>
/// Seam for Win2D factory-level calls that cannot be mocked directly.
/// </summary>
public interface IBlurInterop
{
    /// <summary>Creates a <c>CanvasDevice</c> wrapping the given D3D11 device.</summary>
    IBlurCanvasDevice CreateCanvasDevice(IDirect3DDevice device);

    /// <summary>Allocates a <c>CanvasRenderTarget</c> of the given pixel dimensions.</summary>
    IBlurRenderTarget CreateRenderTarget(IBlurCanvasDevice canvasDevice, float width, float height);

    /// <summary>
    /// Draws <paramref name="frame"/> to <paramref name="renderTarget"/> through a
    /// <c>GaussianBlurEffect</c> with the given <paramref name="radius"/> in DIPs.
    /// </summary>
    void DrawBlur(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget, ICaptureFrame frame, float radius);

    /// <summary>Returns the pixel dimensions of the captured surface in <paramref name="frame"/>.</summary>
    (float Width, float Height) GetFrameSize(ICaptureFrame frame);

    /// <summary>Allocates a persistent GPU texture to hold a cached copy of a captured frame.</summary>
    ICachedFrame CreateCachedFrame(IBlurCanvasDevice canvasDevice, float width, float height);

    /// <summary>Copies the pixels of <paramref name="frame"/> into <paramref name="cachedFrame"/>.</summary>
    void UpdateCachedFrame(IBlurCanvasDevice canvasDevice, ICachedFrame cachedFrame, ICaptureFrame frame);

    /// <summary>
    /// Draws <paramref name="cachedFrame"/> to <paramref name="renderTarget"/> through a
    /// <c>GaussianBlurEffect</c> with the given <paramref name="radius"/> in DIPs.
    /// </summary>
    void DrawBlurFromCache(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget, ICachedFrame cachedFrame, float radius);
}
