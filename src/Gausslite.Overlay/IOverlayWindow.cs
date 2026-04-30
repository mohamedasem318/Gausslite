using System.Windows;
using Gausslite.Core.Blur;

namespace Gausslite.Overlay;

public interface IOverlayWindow : IDisposable
{
    /// <summary>Top-level overlay HWND, or <see cref="IntPtr.Zero"/> before the window is created.</summary>
    IntPtr WindowHandle { get; }

    /// <summary>Creates the overlay HWND visible in WPF but parked off-screen with <paramref name="initialBounds"/> size.</summary>
    void ShowOffscreen(Rect initialBounds);

    /// <summary>Moves the overlay on-screen to cover <paramref name="bounds"/>.</summary>
    void MoveToBounds(Rect bounds);

    /// <summary>Moves the overlay off-screen without destroying its HWND.</summary>
    void MoveOffscreen();

    /// <summary>Destroys the current overlay HWND and prepares a fresh hidden window for the next setup.</summary>
    void Destroy();

    /// <summary>
    /// Covers the overlay with an opaque placeholder until the next frame is presented.
    /// Must be called on the WPF UI thread.
    /// </summary>
    void ShowPlaceholder();

    /// <summary>
    /// Pushes <paramref name="target"/> to the hosted <c>D3DImage</c>.
    /// Safe to call from any thread; dispatches to the UI thread internally.
    /// </summary>
    void PresentFrame(IBlurRenderTarget target);
}
