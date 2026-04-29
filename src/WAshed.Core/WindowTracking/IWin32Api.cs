namespace WAshed.Core.WindowTracking;

public interface IWin32Api
{
    IReadOnlyList<IntPtr> GetWindowHandlesForProcessName(string processName);
    bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Finds the first visible top-level WhatsApp window using the unified detection strategy.
    /// Returns <see cref="IntPtr.Zero"/> if not found.
    /// </summary>
    IntPtr FindWhatsAppWindowHandle();
}
