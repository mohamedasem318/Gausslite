namespace Gausslite.Core.Blur;

/// <summary>
/// A persistent GPU copy of a captured frame's surface, retained for on-demand re-render
/// when blur parameters change between naturally arriving frames.
/// </summary>
public interface ICachedFrame : IDisposable
{
    float Width { get; }
    float Height { get; }
}
