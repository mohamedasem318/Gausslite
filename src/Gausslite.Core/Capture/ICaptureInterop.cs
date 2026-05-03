// SPDX-License-Identifier: AGPL-3.0-or-later
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Capture;

/// <summary>
/// Seam for factory-level WinRT calls that cannot be mocked directly.
/// </summary>
public interface ICaptureInterop
{
    /// <summary>Creates a WinRT IDirect3DDevice from a DXGI device COM pointer.</summary>
    IDirect3DDevice CreateDirect3DDevice(IntPtr dxgiDevice);

    /// <summary>
    /// Creates a free-threaded <see cref="Direct3D11CaptureFramePool"/> so that
    /// FrameArrived fires on a thread-pool thread rather than the UI thread.
    /// </summary>
    ICaptureFramePool CreateFreeThreadedFramePool(IDirect3DDevice device, GraphicsCaptureItem item);

    /// <summary>Creates a capture session bound to <paramref name="pool"/> and <paramref name="item"/>.</summary>
    ICaptureSession CreateSession(ICaptureFramePool pool, GraphicsCaptureItem item);
}
