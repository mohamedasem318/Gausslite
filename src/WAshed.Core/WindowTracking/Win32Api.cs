using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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

    public IntPtr FindWhatsAppWindowHandle()
    {
        IntPtr result = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var titleSb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, titleSb, len + 1);
            string title = titleSb.ToString();

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;

            string procName;
            try { using var proc = Process.GetProcessById((int)pid); procName = proc.ProcessName; }
            catch { return true; }

            var classSb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classSb, 256);

            if (!IsWhatsAppWindow(procName, classSb.ToString(), title)) return true;

            result = hwnd;
            return false; // stop enumeration
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Returns true if the given window belongs to WhatsApp Desktop (any install variant).
    /// Rejects WebView2 child windows (msedgewebview2 process).
    /// </summary>
    internal static bool IsWhatsAppWindow(string processName, string className, string title)
    {
        if (string.IsNullOrEmpty(title)) return false;
        if (processName.Contains("msedgewebview", StringComparison.OrdinalIgnoreCase)) return false;
        if (processName.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase)) return true;
        if (className.Equals("WinUIDesktopWin32WindowClass", StringComparison.Ordinal) &&
            title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
    }
}
