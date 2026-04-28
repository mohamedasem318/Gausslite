using Windows.Graphics.DirectX.Direct3D11;

namespace WAshed.Core.Blur;

/// <summary>
/// Implemented alongside <see cref="IBlurRenderTarget"/> by concrete Win2D render-target
/// wrappers to expose the underlying DirectX surface for GPU-accelerated WPF D3DImage interop.
/// </summary>
/// <remarks>
/// The <c>CanvasRenderTarget</c> backing this surface must be created with
/// <c>DXGI_RESOURCE_MISC_SHARED</c> so the D3D11 texture can be shared with the D3D9Ex device.
/// </remarks>
public interface INativeBlurRenderTarget
{
    IDirect3DSurface GetDirect3DSurface();
}
