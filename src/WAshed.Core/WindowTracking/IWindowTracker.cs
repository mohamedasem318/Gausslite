using System.Windows;

namespace WAshed.Core.WindowTracking;

public interface IWindowTracker
{
    event EventHandler<Rect>? BoundsChanged;
    Rect? CurrentBounds { get; }
    bool IsTracking { get; }
    void Start();
    void Stop();
}
