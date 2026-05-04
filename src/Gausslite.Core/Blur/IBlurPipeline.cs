// SPDX-License-Identifier: AGPL-3.0-or-later
using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Blur;

/// <summary>
/// Receives captured frames and produces a Gaussian-blurred render target.
/// </summary>
public interface IBlurPipeline : IDisposable
{
    /// <summary>One-time setup. Must be called before <see cref="BlurFrame"/>.</summary>
    void Initialize(IDirect3DDevice device);

    /// <summary>
    /// Blurs <paramref name="frame"/> and returns the render target holding the result.
    /// The caller must not retain the returned target past the next <see cref="BlurFrame"/> call.
    /// </summary>
    IBlurRenderTarget BlurFrame(ICaptureFrame frame);

    /// <summary>Gaussian blur radius in DIPs. Configurable at runtime.</summary>
    float BlurRadius { get; set; }

    /// <summary>
    /// Re-renders the most recently cached input frame at the current <see cref="BlurRadius"/>.
    /// Returns the render target if a cached frame was available; returns <see langword="null"/> if
    /// no frame has been captured yet (the next real frame will use the new radius automatically).
    /// The caller must present the result immediately and not retain it past the next <see cref="BlurFrame"/> call.
    /// </summary>
    IBlurRenderTarget? TryRenderCurrentFrame();

    /// <summary>
    /// Reads the most recently cached input frame as a CPU-side BGRA byte buffer.
    /// Returns <see langword="false"/> if no frame has been captured yet or if the GPU readback fails.
    /// <para>
    /// Must be called from the WPF UI thread.  Map/Unmap on the immediate context is acceptable
    /// at this cadence (once on first frame, then on each WhatsApp resize).
    /// </para>
    /// </summary>
    bool TryReadLatestFrameAsBgra(out byte[] bgraPixels, out int width, out int height, out int stride);

    /// <summary>
    /// Discards the cached input frame and staging texture so subsequent
    /// <see cref="TryReadLatestFrameAsBgra"/> calls return <see langword="false"/> until a
    /// fresh frame has been blurred. Call this when a capture session is torn down so the
    /// next session's region-detection runs only on its own frames — never on stale pixels
    /// from a previous session, which can mislabel chat-list / conversation panes when the
    /// user restarts WhatsApp in a different UI direction (LTR ↔ RTL).
    /// </summary>
    void ClearCachedFrame();
}
