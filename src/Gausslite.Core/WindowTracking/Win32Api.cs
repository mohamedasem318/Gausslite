using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Gausslite.Core.WindowTracking;

public sealed class Win32Api : IWin32Api
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

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

    bool IWin32Api.IsIconic(IntPtr hwnd) =>
        IsIconic(hwnd);

    bool IWin32Api.IsZoomed(IntPtr hwnd) =>
        IsZoomed(hwnd);

    bool IWin32Api.TryGetMonitorWorkArea(IntPtr hwnd, out RECT workArea)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            workArea = default;
            return false;
        }

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            workArea = default;
            return false;
        }

        workArea = info.rcWork;
        return true;
    }

    IntPtr IWin32Api.WindowFromPoint(POINT point) =>
        NativeMethods.WindowFromPoint(point);

    IntPtr IWin32Api.GetRootWindow(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);

    IntPtr IWin32Api.GetNextWindow(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);

    IntPtr IWin32Api.GetPreviousWindow(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV);

    bool IWin32Api.IsWindowVisible(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);

    uint IWin32Api.GetWindowProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    int IWin32Api.GetWindowExStyle(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? 0 : NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

    bool IWin32Api.InvalidateClientArea(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, false);

    public IntPtr FindWindowHandle(Func<string, string, string, bool> predicate)
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

            if (!predicate(procName, classSb.ToString(), title)) return true;

            result = hwnd;
            return false; // stop enumeration
        }, IntPtr.Zero);
        return result;
    }

    public IReadOnlyList<WindowInfo> EnumerateVisibleWindows()
    {
        var results = new List<WindowInfo>(64);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;

            string procName;
            try { using var proc = Process.GetProcessById((int)pid); procName = proc.ProcessName; }
            catch { return true; }

            var classSb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classSb, 256);

            string title = string.Empty;
            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len > 0)
            {
                var titleSb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, titleSb, len + 1);
                title = titleSb.ToString();
            }

            results.Add(new WindowInfo(hwnd, pid, procName, classSb.ToString(), title));
            return true;
        }, IntPtr.Zero);
        return results;
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

        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        public const uint GW_HWNDNEXT = 2;
        public const uint GW_HWNDPREV = 3;

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);

        public const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }
}
