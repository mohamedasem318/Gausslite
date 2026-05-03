// SPDX-License-Identifier: AGPL-3.0-or-later
using Windows.Graphics.Capture;
using Windows.Graphics;

namespace Gausslite.Core.Capture;

/// <summary>
/// Thin mockable wrapper over <c>Direct3D11CaptureFrame</c>.
/// The underlying WinRT frame is exposed via <see cref="Frame"/> for downstream consumers
/// (e.g., the blur pipeline) that need to access the captured surface.
/// </summary>
public interface ICaptureFrame : IDisposable
{
    Direct3D11CaptureFrame Frame { get; }

    /// <summary>The content size reported by Windows Graphics Capture for this frame.</summary>
    SizeInt32 ContentSize { get; }
}
