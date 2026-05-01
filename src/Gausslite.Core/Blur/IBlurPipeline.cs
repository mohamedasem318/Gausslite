using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

/// <summary>
/// Receives captured frames and produces a Gaussian-blurred render target.
/// </summary>
public interface IBlurPipeline : IDisposable
{
    /// <summary>One-time setup. Must be called before <see cref="BlurFrame"/>.</summary>
    void Initialize(IDirect3DDevice device);

    /// <summary>
    /// Blurs <paramref name="frame"/> and returns the render target holding the result.
    /// The caller must not retain the returned target past the next <see cref="BlurFrame"/> call.
    /// </summary>
    IBlurRenderTarget BlurFrame(ICaptureFrame frame);

    /// <summary>Gaussian blur radius in DIPs. Configurable at runtime.</summary>
    float BlurRadius { get; set; }

    /// <summary>
    /// Re-renders the most recently cached input frame at the current <see cref="BlurRadius"/>.
    /// Returns the render target if a cached frame was available; returns <see langword="null"/> if
    /// no frame has been captured yet (the next real frame will use the new radius automatically).
    /// The caller must present the result immediately and not retain it past the next <see cref="BlurFrame"/> call.
    /// </summary>
    IBlurRenderTarget? TryRenderCurrentFrame();
}
