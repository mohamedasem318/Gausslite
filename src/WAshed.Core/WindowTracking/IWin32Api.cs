namespace WAshed.Core.WindowTracking;

public interface IWin32Api
{
    IReadOnlyList<IntPtr> GetWindowHandlesForProcessName(string processName);
    bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    uint GetDpiForWindow(IntPtr hwnd);
}
