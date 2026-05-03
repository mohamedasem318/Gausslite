// DiscordProbe — Discord-specific recon for v0.3.0 screen-share detection.
//
// ShareProbe only enumerates *top-level* windows. Discord is Electron/Chromium —
// its share-control UI is rendered inside the main window, so EnumWindows alone
// won't see it.  This tool recursively walks every CHILD window under each Discord
// top-level window and dumps the lot, so we can see if there's a share-only child
// window worth keying a signature off.
//
// Usage:
//   .\DiscordProbe.exe > before-discord-children.txt
//   <start sharing>
//   .\DiscordProbe.exe > during-discord-children.txt
//
// Diff. Look for new child windows whose class or title contains share-related
// strings ("stop", "sharing", "screen", "stream", "go live").

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Gausslite.Tools.DiscordProbe;

internal static class Program
{
    private static readonly string[] TargetProcessNames = { "Discord" };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Header
        Console.WriteLine(
            "depth\thwnd\tparentHwnd\tpid\tprocessName\tisVisible\tclassName\ttitle\tstyleHex\texStyleHex");

        var topLevels = new List<IntPtr>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (Array.Exists(TargetProcessNames, n => string.Equals(n, proc.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    topLevels.Add(hwnd);
            }
            catch { /* ignore */ }
            return true;
        }, IntPtr.Zero);

        foreach (var top in topLevels)
        {
            DumpWindow(top, parent: IntPtr.Zero, depth: 0);
            EnumerateChildren(top, depth: 1);
        }

        return 0;
    }

    private static void EnumerateChildren(IntPtr parent, int depth)
    {
        // Hard-cap recursion depth so we never blow up on a deep widget tree.
        if (depth > 12) return;

        var children = new List<IntPtr>();
        NativeMethods.EnumChildWindows(parent, (hwnd, _) =>
        {
            // EnumChildWindows recurses into descendants.  We want only the immediate
            // children at this depth so we can recurse manually and report a sensible
            // tree-depth column.  Filter to children whose direct parent is `parent`.
            var actualParent = NativeMethods.GetParent(hwnd);
            if (actualParent == parent)
                children.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        foreach (var c in children)
        {
            DumpWindow(c, parent, depth);
            EnumerateChildren(c, depth + 1);
        }
    }

    private static void DumpWindow(IntPtr hwnd, IntPtr parent, int depth)
    {
        bool visible = NativeMethods.IsWindowVisible(hwnd);

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        string procName = "?";
        if (pid != 0)
        {
            try { using var proc = Process.GetProcessById((int)pid); procName = proc.ProcessName; }
            catch { }
        }

        var classBuf = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, classBuf, 256);

        string title = string.Empty;
        int titleLen = NativeMethods.GetWindowTextLength(hwnd);
        if (titleLen > 0)
        {
            var titleBuf = new StringBuilder(titleLen + 1);
            NativeMethods.GetWindowText(hwnd, titleBuf, titleLen + 1);
            title = titleBuf.ToString();
        }

        int style   = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        Console.WriteLine(string.Join('\t',
            depth.ToString(CultureInfo.InvariantCulture),
            "0x" + hwnd.ToString("X"),
            "0x" + parent.ToString("X"),
            pid.ToString(CultureInfo.InvariantCulture),
            procName,
            visible ? "1" : "0",
            classBuf.ToString(),
            Sanitize(title),
            "0x" + style.ToString("X"),
            "0x" + exStyle.ToString("X")));
    }

    private static string Sanitize(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

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

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    }
}
