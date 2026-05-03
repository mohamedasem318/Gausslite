// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

/// <summary>
/// Concrete <see cref="IBlurCanvasDevice"/> that wraps a Win2D <see cref="CanvasDevice"/>.
/// Also holds the originating <see cref="IDirect3DDevice"/> so that
/// <see cref="Win2DBlurRenderTarget"/> can reach the underlying ID3D11Device for shared-texture creation.
/// </summary>
internal sealed class Win2DCanvasDeviceWrapper : IBlurCanvasDevice
{
    internal CanvasDevice CanvasDevice { get; }
    internal IDirect3DDevice Direct3DDevice { get; }

    internal Win2DCanvasDeviceWrapper(CanvasDevice canvasDevice, IDirect3DDevice d3dDevice)
    {
        CanvasDevice = canvasDevice;
        Direct3DDevice = d3dDevice;
    }

    public void Dispose() => CanvasDevice.Dispose();
}
