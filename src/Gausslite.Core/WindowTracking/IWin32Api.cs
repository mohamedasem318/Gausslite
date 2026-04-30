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
    /// Finds the first visible top-level WhatsApp window using the unified detection strategy.
    /// Returns <see cref="IntPtr.Zero"/> if not found.
    /// </summary>
    IntPtr FindWhatsAppWindowHandle();
}
