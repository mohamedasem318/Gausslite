using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using Gausslite.Core.Diagnostics;
using Gausslite.Core.WindowTracking;
using WinRT;

namespace Gausslite.App.Orchestration;

/// <summary>
/// Locates WhatsApp Desktop's HWND and creates a <see cref="GraphicsCaptureItem"/> for it
/// via <c>IGraphicsCaptureItemInterop</c> / <c>RoGetActivationFactory</c> P/Invoke.
/// Supports the WinUI 3 Microsoft Store build (WhatsApp.Root process) and classic Win32
/// installs. WebView2 child windows are explicitly excluded.
/// </summary>
internal sealed class CaptureItemFactory : ICaptureItemFactory
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly IWin32Api _win32Api;

    // Written exactly once (first call per process lifetime) — volatile for visibility without locking.
    private static volatile bool _diagLogged;

    public CaptureItemFactory(IWin32Api win32Api) => _win32Api = win32Api;

    public bool TryCreateForWhatsApp(out GraphicsCaptureItem? item)
    {
        item = null;

        if (!GraphicsCaptureSession.IsSupported())
        {
            Debug.WriteLine("[CaptureItemFactory] Windows.Graphics.Capture not supported on this system.");
            return false;
        }

        bool logThisCall = !_diagLogged;
        if (logThisCall) _diagLogged = true;

        IntPtr hwnd = FindWhatsAppWindow(logThisCall, out string foundProc, out string foundClass);

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

    private static IntPtr FindWhatsAppWindow(bool log, out string foundProc, out string foundClass)
    {
        IntPtr result = IntPtr.Zero;
        foundProc = "";
        foundClass = "";
        int examined = 0;

        if (log) DiagLog.Info("CaptureItemFactory: enumerating top-level windows...");

        // Capture out-param targets for use inside the lambda (lambdas can't assign out params directly).
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
            try { using var proc = Process.GetProcessById((int)pid); procName = proc.ProcessName; }
            catch { return true; }

            var classSb = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classSb, 256);
            string className = classSb.ToString();

            bool match = IsWhatsAppWindow(procName, className, title);

            if (log && examined < 20)
            {
                string reason = ReasonFor(procName, className, title, match);
                DiagLog.Info($"CaptureItemFactory: examined HWND=0x{hwnd:X}, process={procName}, class={className}, title={title}, match={match}, reason={reason}");
            }
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
            DiagLog.Info($"CaptureItemFactory: no match found among {examined} visible top-level windows");

        foundProc = matchedProc;
        foundClass = matchedClass;
        return result;
    }

    private static string ReasonFor(string processName, string className, string title, bool match)
    {
        if (!match)
        {
            if (processName.Contains("msedgewebview", StringComparison.OrdinalIgnoreCase))
                return "WebView2 excluded";
            return "no match";
        }
        if (processName.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase))
            return "process name starts with WhatsApp";
        if (className.Equals("WinUIDesktopWin32WindowClass", StringComparison.Ordinal))
            return "WinUI3 class + WhatsApp title";
        return "matched";
    }

    // ── Pure predicate (internal for unit testing) ────────────────────────────

    /// <summary>
    /// Delegates to the single authoritative predicate in <see cref="Win32Api"/>.
    /// Exposed <c>internal</c> so <c>CaptureItemFactoryTests</c> can call it directly.
    /// </summary>
    internal static bool IsWhatsAppWindow(string processName, string className, string title) =>
        Win32Api.IsWhatsAppWindow(processName, className, title);

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
