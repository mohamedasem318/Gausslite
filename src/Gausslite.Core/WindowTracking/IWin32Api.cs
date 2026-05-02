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

    /// <summary>
    /// Returns the window immediately above <paramref name="hwnd"/> in Z-order
    /// (<c>GetWindow(hwnd, GW_HWNDPREV)</c>), or <see cref="IntPtr.Zero"/> if none.
    /// </summary>
    IntPtr GetPreviousWindow(IntPtr hwnd);

    /// <summary>Returns true if <paramref name="hwnd"/> is visible (<c>IsWindowVisible</c>).</summary>
    bool IsWindowVisible(IntPtr hwnd);

    /// <summary>Returns the process ID of the thread that created <paramref name="hwnd"/>.</summary>
    uint GetWindowProcessId(IntPtr hwnd);

    /// <summary>Returns the extended window style (<c>GetWindowLong(hwnd, GWL_EXSTYLE)</c>).</summary>
    int GetWindowExStyle(IntPtr hwnd);
}
