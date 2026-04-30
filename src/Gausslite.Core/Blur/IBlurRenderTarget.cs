namespace Gausslite.Core.Blur;

/// <summary>
/// Thin mockable wrapper over <c>CanvasRenderTarget</c>.
/// </summary>
public interface IBlurRenderTarget : IDisposable
{
    float Width { get; }
    float Height { get; }
}
