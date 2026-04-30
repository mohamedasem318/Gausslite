namespace Gausslite.Core.WindowTracking;

public interface IWin32Api
{
    IReadOnlyList<IntPtr> GetWindowHandlesForProcessName(string processName);
    bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    uint GetDpiForWindow(IntPtr hwnd);
    bool IsIconic(IntPtr hwnd);
    bool IsZoomed(IntPtr hwnd);
    bool TryGetMonitorWorkArea(IntPtr hwnd, out RECT workArea);
    IntPtr WindowFromPoint(POINT point);
    IntPtr GetRootWindow(IntPtr hwnd);
    IntPtr GetNextWindow(IntPtr hwnd);

    /// <summary>
    /// Enumerates visible top-level windows and returns the first HWND for which
    /// <paramref name="predicate"/> returns true, or <see cref="IntPtr.Zero"/> if none match.
    /// </summary>
    IntPtr FindWindowHandle(Func<string, string, string, bool> predicate);
}
