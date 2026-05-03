// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Diagnostics;
using WinRT;

namespace Gausslite.App.Orchestration;

/// <summary>
/// Creates a <see cref="GraphicsCaptureItem"/> for the app identified by the active
/// <see cref="IAppProfile"/> via <c>IGraphicsCaptureItemInterop</c> /
/// <c>RoGetActivationFactory</c> P/Invoke.
/// </summary>
internal sealed class CaptureItemFactory : ICaptureItemFactory
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly IAppProfile _profile;

    // Written exactly once (first call per process lifetime) — volatile for visibility without locking.
    private static volatile bool _diagLogged;

    public CaptureItemFactory(IAppProfile profile) => _profile = profile;

    public bool TryCreateForProfile(out GraphicsCaptureItem? item)
    {
        item = null;

        if (!GraphicsCaptureSession.IsSupported())
        {
            Debug.WriteLine("[CaptureItemFactory] Windows.Graphics.Capture not supported on this system.");
            return false;
        }

        bool logThisCall = !_diagLogged;
        if (logThisCall) _diagLogged = true;

        IntPtr hwnd = FindProfileWindow(logThisCall, out string foundProc, out string foundClass);

        if (hwnd == IntPtr.Zero)
            return false;

        // ── Activate IGraphicsCaptureItemInterop ─────────────────────────────────────────────────────
        //
        // .NET 6+ removed WindowsRuntimeMarshal.GetActivationFactory, so we call
        // RoGetActivationFactory directly via P/Invoke. WindowsCreateString allocates the
        // HSTRING for the runtime class name; WindowsDeleteString frees it in the finally.

        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = NativeMethods.WindowsCreateString(runtimeClass, (uint)runtimeClass.Length, out IntPtr hstring);
        if (hr < 0)
        {
            Debug.WriteLine($"[CaptureItemFactory] WindowsCreateString failed: 0x{hr:X8}");
            return false;
        }

        IntPtr factoryPtr = IntPtr.Zero;
        IntPtr itemAbi = IntPtr.Zero;

        try
        {
            var interopIid = IID_IGraphicsCaptureItemInterop;
            hr = NativeMethods.RoGetActivationFactory(hstring, ref interopIid, out factoryPtr);
            if (hr < 0)
            {
                Debug.WriteLine($"[CaptureItemFactory] RoGetActivationFactory failed: 0x{hr:X8}");
                return false;
            }

            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);

            var itemIid = IID_IGraphicsCaptureItem;
            hr = interop.CreateForWindow(hwnd, ref itemIid, out itemAbi);
            if (hr < 0)
            {
                DiagLog.Warn($"CaptureItemFactory: CreateForWindow failed (process={foundProc}, class={foundClass}): HRESULT=0x{hr:X8}");
                return false;
            }

            item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemAbi);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CaptureItemFactory] Unexpected exception: {ex.Message}");
            return false;
        }
        finally
        {
            if (itemAbi != IntPtr.Zero) Marshal.Release(itemAbi);
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            NativeMethods.WindowsDeleteString(hstring);
        }
    }

    // ── Unified window detection ──────────────────────────────────────────────

    private IntPtr FindProfileWindow(bool log, out string foundProc, out string foundClass)
    {
        IntPtr result = IntPtr.Zero;
        foundProc = "";
        foundClass = "";
        int examined = 0;

        if (log) DiagLog.Info($"CaptureItemFactory: enumerating top-level windows for {_profile.Name}...");

        string matchedProc = "";
        string matchedClass = "";

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
            try { using var proc = System.Diagnostics.Process.GetProcessById((int)pid); procName = proc.ProcessName; }
            catch { return true; }

            var classSb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classSb, 256);
            string className = classSb.ToString();

            bool match = _profile.IsAppWindow(procName, className, title);

            if (log && examined < 20)
                DiagLog.Info($"CaptureItemFactory: examined HWND=0x{hwnd:X}, process={procName}, class={className}, title={title}, match={match}");
            examined++;

            if (match)
            {
                if (log) DiagLog.Info($"CaptureItemFactory: matched HWND=0x{hwnd:X}, process={procName}, class={className}");
                matchedProc = procName;
                matchedClass = className;
                result = hwnd;
                return false; // stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        if (log && result == IntPtr.Zero)
            DiagLog.Info($"CaptureItemFactory: no {_profile.Name} match found among {examined} visible top-level windows");

        foundProc = matchedProc;
        foundClass = matchedClass;
        return result;
    }

    // ── COM interface declaration ─────────────────────────────────────────────

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr ppv);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr ppv);
    }

    // ── P/Invokes ─────────────────────────────────────────────────────────────

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

        [DllImport("combase.dll", PreserveSig = true)]
        public static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            ref Guid iid,
            out IntPtr factory);

        [DllImport("combase.dll", PreserveSig = true)]
        public static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            uint length,
            out IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = true)]
        public static extern int WindowsDeleteString(IntPtr hstring);
    }
}
