using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WAshed.Core.WindowTracking;

public sealed class Win32Api : IWin32Api
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public IReadOnlyList<IntPtr> GetWindowHandlesForProcessName(string processName)
    {
        var handles = new List<IntPtr>();
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            using (proc)
            {
                var hwnd = proc.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                    handles.Add(hwnd);
            }
        }
        return handles;
    }

    bool IWin32Api.GetWindowRect(IntPtr hwnd, out RECT lpRect) =>
        GetWindowRect(hwnd, out lpRect);

    uint IWin32Api.GetDpiForWindow(IntPtr hwnd) =>
        GetDpiForWindow(hwnd);
}
