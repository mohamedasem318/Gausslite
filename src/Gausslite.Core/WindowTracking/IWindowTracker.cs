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

    bool IsTracking { get; }
    void SetOverlayWindowHandle(IntPtr hwnd);
    void Start();
    void Stop();
}
