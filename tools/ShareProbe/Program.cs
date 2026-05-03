// SPDX-License-Identifier: AGPL-3.0-or-later
// ShareProbe — recon utility for v0.3.0 screen-share detection.
//
// Enumerates every visible top-level window and dumps a tab-separated row per
// window: hwnd, pid, processName, className, title, isVisible, ownerHwnd, exStyle.
//
// Recon protocol per app (Zoom / Teams / Discord / Google Meet in Chrome or Edge):
//   1) Open the app but DO NOT start a share.
//      .\ShareProbe.exe > before.txt
//   2) Start a screen share.
//      .\ShareProbe.exe > during.txt
//   3) Stop the share.
//      .\ShareProbe.exe > after.txt
//
// The diff (during.txt minus before.txt) reveals the share-only windows.
// Paste the during.txt + before.txt back to me and I'll extract the signature.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Gausslite.Tools.ShareProbe;

internal static class Program
{
    private const int MaxClassNameChars = 256;
    private const int MaxTitleChars = 512;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Header. TSV so it pastes cleanly into the chat.
        Console.WriteLine(
            "hwnd\tpid\tprocessName\tisVisible\tisIconic\tclassName\ttitle\townerHwnd\texStyleHex");

        var rows = new List<Row>();

        bool ok = NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                rows.Add(BuildRow(hwnd));
            }
            catch
            {
                // Ignore individual-window failures — recon should not abort on them.
            }
            return true;
        }, IntPtr.Zero);

        if (!ok)
        {
            Console.Error.WriteLine("EnumWindows failed.");
            return 1;
        }

        // Sort by processName then title for reproducible diffs.
        rows.Sort((a, b) =>
        {
            int c = string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = string.Compare(a.ClassName, b.ClassName, StringComparison.Ordinal);
            if (c != 0) return c;
            return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
        });

        foreach (var r in rows)
        {
            Console.WriteLine(string.Join('\t',
                "0x" + r.Hwnd.ToString("X"),
                r.Pid.ToString(CultureInfo.InvariantCulture),
                r.ProcessName,
                r.IsVisible ? "1" : "0",
                r.IsIconic ? "1" : "0",
                r.ClassName,
                Sanitize(r.Title),
                "0x" + r.OwnerHwnd.ToString("X"),
                "0x" + r.ExStyle.ToString("X")));
        }

        return 0;
    }

    private static Row BuildRow(IntPtr hwnd)
    {
        bool visible = NativeMethods.IsWindowVisible(hwnd);
        bool iconic = NativeMethods.IsIconic(hwnd);

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

        string processName = "?";
        if (pid != 0)
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch
            {
                // Process exited or access denied — leave as "?".
            }
        }

        var classBuf = new StringBuilder(MaxClassNameChars);
        NativeMethods.GetClassName(hwnd, classBuf, MaxClassNameChars);

        var titleBuf = new StringBuilder(MaxTitleChars);
        int titleLen = NativeMethods.GetWindowTextLength(hwnd);
        if (titleLen > 0)
            NativeMethods.GetWindowText(hwnd, titleBuf, Math.Min(titleLen + 1, MaxTitleChars));

        IntPtr owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

        return new Row
        {
            Hwnd = hwnd,
            Pid = pid,
            ProcessName = processName,
            ClassName = classBuf.ToString(),
            Title = titleBuf.ToString(),
            IsVisible = visible,
            IsIconic = iconic,
            OwnerHwnd = owner,
            ExStyle = exStyle,
        };
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Replace tabs and newlines so the TSV stays one-row-per-window.
        return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private struct Row
    {
        public IntPtr Hwnd;
        public uint Pid;
        public string ProcessName;
        public string ClassName;
        public string Title;
        public bool IsVisible;
        public bool IsIconic;
        public IntPtr OwnerHwnd;
        public int ExStyle;
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

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        public const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);

        public const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    }
}
