using Microsoft.Graphics.Canvas;

namespace Gausslite.Core.Blur;

internal sealed class Win2DCachedFrame : ICachedFrame
{
    internal CanvasRenderTarget CanvasRenderTarget { get; }
    public float Width { get; }
    public float Height { get; }
    private bool _disposed;

    internal Win2DCachedFrame(CanvasRenderTarget crt, float width, float height)
    {
        CanvasRenderTarget = crt;
        Width = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CanvasRenderTarget.Dispose();
    }
}
