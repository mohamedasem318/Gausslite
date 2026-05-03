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

    /// <summary>
    /// Invalidates the entire client area of <paramref name="hwnd"/>, marking it for
    /// repaint.  Used to nudge a foreign-process window into emitting a fresh paint —
    /// which causes WGC to deliver a fresh capture frame at the current size.  No-op
    /// when <paramref name="hwnd"/> is <see cref="IntPtr.Zero"/>.  Returns true if the
    /// underlying <c>InvalidateRect</c> call succeeded.
    /// </summary>
    bool InvalidateClientArea(IntPtr hwnd);

    /// <summary>
    /// Enumerates every visible top-level window on the desktop and returns
    /// <see cref="WindowInfo"/> records describing each.  Used by the screen-share
    /// detector to scan for known share-control window signatures on each poll.
    /// Windows whose process can't be opened (already exited, access denied) are
    /// skipped silently.
    /// </summary>
    IReadOnlyList<WindowInfo> EnumerateVisibleWindows();
}
