// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

internal sealed class Win2DCachedFrame : ICachedFrame
{
    internal CanvasRenderTarget CanvasRenderTarget { get; }
    // Stored so Win2DBlurInterop.TryReadBgra can QI for ID3D11Texture2D via IDirect3DDxgiInterfaceAccess.
    internal IDirect3DSurface? Surface { get; }
    public float Width  { get; }
    public float Height { get; }
    private bool _disposed;

    internal Win2DCachedFrame(CanvasRenderTarget crt, IDirect3DSurface? surface, float width, float height)
    {
        CanvasRenderTarget = crt;
        Surface = surface;
        Width  = width;
        Height = height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CanvasRenderTarget.Dispose();
        // Surface is a WinRT managed wrapper; ref released when GC collects it.
    }
}
