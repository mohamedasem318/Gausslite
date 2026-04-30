using Windows.Graphics.Capture;

namespace Gausslite.App.Orchestration;

/// <summary>
/// Creates a <see cref="GraphicsCaptureItem"/> for the app identified by the active profile.
/// Abstracted so <see cref="TrayOrchestrator"/> can be unit-tested without real WinRT objects.
/// </summary>
public interface ICaptureItemFactory
{
    /// <summary>
    /// Returns <see langword="true"/> and sets <paramref name="item"/> when the profile's app
    /// is found and a capture item was successfully created.
    /// Returns <see langword="false"/> with <paramref name="item"/> set to <see langword="null"/>
    /// when the app is not running or item creation fails.
    /// </summary>
    bool TryCreateForProfile(out GraphicsCaptureItem? item);
}
