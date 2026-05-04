// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;

namespace Gausslite.Core.WindowTracking;

public interface IWindowTracker
{
    event EventHandler<Rect>? BoundsChanged;
    event EventHandler<bool>? MinimizedChanged;

    /// <summary>
    /// Fires whenever the visible region of the tracked window changes while the window is present.
    /// The payload is the new visible region in screen DIP coordinates — empty means fully occluded,
    /// a single rect equal to the window bounds means fully visible, partial rects mean clipped.
    /// </summary>
    event EventHandler<IReadOnlyList<Rect>>? VisibleRegionChanged;

    event EventHandler<bool>? WindowPresenceChanged;
    Rect? CurrentBounds { get; }
    bool IsWindowPresent { get; }
    bool IsMinimized { get; }

    /// <summary>
    /// The current visible region in screen DIP coordinates.
    /// <c>null</c> when the window is not present or tracking is stopped.
    /// Empty means fully occluded. Non-empty means visible (partially or fully).
    /// </summary>
    IReadOnlyList<Rect>? VisibleRegion { get; }

    /// <summary>
    /// True when WhatsApp has zero pixels actually visible to the user — i.e. genuinely
    /// hidden behind another application's window (Edge fullscreen, another fullscreen
    /// app, virtual-desktop switch, etc.), not just clipped by many small overlays.
    /// Determined by sampling several points within the tracked window's rect using
    /// <c>WindowFromPoint</c>, which Windows uses to answer "what window is visually
    /// on top here?" — it correctly skips <c>WS_EX_TRANSPARENT</c> click-through
    /// overlays (e.g. Zoom's annotation layer or share-host wrapper during a real share),
    /// so transparent fullscreen overlays from sharing apps don't cause false positives.
    /// Used by the orchestrator to gate the share-active overlay-keep-visible override
    /// so blurred content doesn't leak on top of an unrelated foreground app.
    /// </summary>
    bool IsLikelyFullyHidden { get; }

    bool IsTracking { get; }
    void SetOverlayWindowHandle(IntPtr hwnd);
    void Start();
    void Stop();

    /// <summary>
    /// Asks the OS to invalidate the tracked window's client area, which causes the
    /// window's owning process to repaint and — for WGC-captured windows — produces a
    /// fresh capture frame at the current size.  Used by the orchestrator after a
    /// bounds change to nudge the captured app into emitting a frame even when its
    /// content is otherwise static (the alternative is the user has to hover the
    /// cursor over the window to provoke a repaint).  No-op if the tracked window is
    /// not currently present.
    /// </summary>
    void RequestRepaintOfTrackedWindow();
}
