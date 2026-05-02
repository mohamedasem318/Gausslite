namespace Gausslite.Core.Blur;

/// <summary>
/// A persistent D3D11 staging texture (D3D11_USAGE_STAGING / D3D11_CPU_ACCESS_READ) used to
/// read GPU frame data back to the CPU.  Lifecycle managed by <see cref="BlurPipeline"/>
/// via <see cref="IBlurInterop.CreateStagingTexture"/>; reused across calls and reallocated
/// only when frame dimensions change.
/// </summary>
public interface IBlurStagingTexture : IDisposable
{
    float Width  { get; }
    float Height { get; }
}
