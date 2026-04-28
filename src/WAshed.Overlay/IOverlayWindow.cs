using System.Windows;
using WAshed.Core.Blur;

namespace WAshed.Overlay;

public interface IOverlayWindow : IDisposable
{
    /// <summary>Makes the overlay window visible.</summary>
    void Show();

    /// <summary>Hides the overlay window without destroying it.</summary>
    void Hide();

    /// <summary>
    /// Repositions and resizes the overlay to cover <paramref name="bounds"/>.
    /// Must be called on the WPF UI thread.
    /// </summary>
    void SetBounds(Rect bounds);

    /// <summary>
    /// Pushes <paramref name="target"/> to the hosted <c>D3DImage</c>.
    /// Safe to call from any thread; dispatches to the UI thread internally.
    /// </summary>
    void PresentFrame(IBlurRenderTarget target);
}
