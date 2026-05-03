// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows.Interop;
using Gausslite.Core.Blur;

namespace Gausslite.Overlay.Interop;

/// <summary>
/// Bridges a Win2D <see cref="IBlurRenderTarget"/> to a WPF <see cref="D3DImage"/>
/// via the D3D9Ex shared-surface pattern. Implementations own the D3D9Ex device lifetime.
/// </summary>
internal interface ID3DImageBridge : IDisposable
{
    /// <summary>
    /// Imports <paramref name="blurTarget"/>'s D3D11 texture into D3D9Ex and sets it
    /// as the <see cref="D3DImage"/> back buffer.  Does nothing if <paramref name="blurTarget"/>
    /// does not implement <see cref="INativeBlurRenderTarget"/> or the shared handle is unavailable.
    /// Must be called on the WPF UI thread.
    /// </summary>
    void UpdateD3DImage(D3DImage d3dImage, IBlurRenderTarget blurTarget);
}
