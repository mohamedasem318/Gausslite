// SPDX-License-Identifier: AGPL-3.0-or-later
using Windows.Graphics.Capture;

namespace Gausslite.Core.Capture;

/// <summary>
/// Wraps a Windows.Graphics.Capture session and delivers per-frame textures.
/// </summary>
public interface ICaptureEngine
{
    /// <summary>
    /// Fired on a thread-pool thread (free-threaded frame pool).
    /// Subscribers must complete their work synchronously or copy the texture out
    /// before returning — the engine disposes the frame after all handlers return.
    /// UI subscribers must marshal back to the UI thread.
    /// </summary>
    event EventHandler<ICaptureFrame>? FrameArrived;

    /// <summary><see langword="true"/> between a successful <see cref="Start"/> and the next <see cref="Stop"/>.</summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Starts the capture session for <paramref name="item"/>.
    /// The caller is responsible for HWND→item conversion.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if capture is already running.</exception>
    void Start(GraphicsCaptureItem item);

    /// <summary>Stops the session and disposes the pool. No-op if not capturing.</summary>
    void Stop();
}
