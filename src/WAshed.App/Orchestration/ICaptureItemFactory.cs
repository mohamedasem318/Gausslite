using Windows.Graphics.Capture;

namespace WAshed.App.Orchestration;

/// <summary>
/// Locates WhatsApp Desktop and creates a <see cref="GraphicsCaptureItem"/> for it.
/// Abstracted so <see cref="TrayOrchestrator"/> can be unit-tested without real WinRT objects.
/// </summary>
public interface ICaptureItemFactory
{
    /// <summary>
    /// Returns <see langword="true"/> and sets <paramref name="item"/> when WhatsApp Desktop
    /// is found and a capture item was successfully created.
    /// Returns <see langword="false"/> with <paramref name="item"/> set to <see langword="null"/>
    /// when WhatsApp is not running or item creation fails.
    /// </summary>
    bool TryCreateForWhatsApp(out GraphicsCaptureItem? item);
}
