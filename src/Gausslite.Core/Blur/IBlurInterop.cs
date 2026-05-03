// SPDX-License-Identifier: AGPL-3.0-or-later
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

    /// <summary>
    /// Draws a solid red 200×200 DIP rectangle at the top-left of <paramref name="renderTarget"/>.
    /// Called only when <c>BlurPipeline.DiagnosticOverlayEnabled</c> is <c>true</c>.
    /// </summary>
    void DrawDiagnosticOverlay(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget);

    /// <summary>
    /// Flushes pending D3D11 commands to the GPU command queue so that a subsequent D3D9Ex
    /// read of the shared render-target texture sees the result of the latest Win2D draw.
    /// Must be called after all Win2D drawing sessions complete on the on-demand (UI-thread)
    /// re-render path, before <c>D3DImageBridge.UpdateD3DImage</c> accesses the surface.
    /// </summary>
    void FlushDevice(IBlurCanvasDevice canvasDevice);

    /// <summary>
    /// Allocates a D3D11 staging texture (D3D11_USAGE_STAGING / D3D11_CPU_ACCESS_READ) sized
    /// to <paramref name="width"/>×<paramref name="height"/> pixels.  The caller is responsible
    /// for disposal; <see cref="BlurPipeline"/> reuses the instance across calls.
    /// </summary>
    IBlurStagingTexture CreateStagingTexture(IBlurCanvasDevice canvasDevice, float width, float height);

    /// <summary>
    /// Copies <paramref name="cachedFrame"/> to <paramref name="staging"/> via CopyResource,
    /// then Maps the staging texture and returns the raw BGRA pixel bytes.
    /// Flushes pending GPU commands before the copy to avoid stale data.
    /// </summary>
    bool TryReadBgra(
        IBlurCanvasDevice canvasDevice,
        ICachedFrame cachedFrame,
        IBlurStagingTexture staging,
        out byte[] bgraPixels,
        out int width,
        out int height,
        out int stride);
}
