using WAshed.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace WAshed.Core.Blur;

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
}
