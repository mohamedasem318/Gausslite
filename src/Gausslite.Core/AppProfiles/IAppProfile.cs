namespace Gausslite.Core.AppProfiles;

public interface IAppProfile
{
    /// <summary>Human-readable name used in diagnostic log strings.</summary>
    string Name { get; }

    /// <summary>
    /// Returns true if the given window belongs to this app.
    /// Called once per top-level window during enumeration; must be fast and allocation-free.
    /// </summary>
    bool IsAppWindow(string processName, string className, string title);

    /// <summary>
    /// Returns the HWND of the first visible top-level window that matches this profile,
    /// or <see cref="IntPtr.Zero"/> if the app is not running.
    /// </summary>
    IntPtr FindWindowHandle();
}
