using System.Windows;

namespace WAshed.Core.WindowTracking;

public interface IWindowTracker
{
    event EventHandler<Rect>? BoundsChanged;
    event EventHandler<bool>? MinimizedChanged;
    event EventHandler<bool>? OcclusionChanged;
    event EventHandler<bool>? WindowPresenceChanged;
    Rect? CurrentBounds { get; }
    bool IsWindowPresent { get; }
    bool IsMinimized { get; }
    bool IsOccluded { get; }
    bool IsTracking { get; }
    void SetOverlayWindowHandle(IntPtr hwnd);
    void Start();
    void Stop();
}
