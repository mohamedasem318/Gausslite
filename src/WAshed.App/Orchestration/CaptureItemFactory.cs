using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WAshed.Core.WindowTracking;
using WinRT;

namespace WAshed.App.Orchestration;

/// <summary>
/// Locates WhatsApp Desktop's HWND and creates a <see cref="GraphicsCaptureItem"/> for it
/// via <c>IGraphicsCaptureItemInterop</c> / <c>RoGetActivationFactory</c> P/Invoke.
/// </summary>
internal sealed class CaptureItemFactory : ICaptureItemFactory
{
    // IID for the IGraphicsCaptureItemInterop activation-factory interface
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    // IID for the IGraphicsCaptureItem WinRT interface — passed to CreateForWindow as the
    // desired interface so the OS returns an IGraphicsCaptureItem* we can marshal to managed.
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly IWin32Api _win32Api;

    public CaptureItemFactory(IWin32Api win32Api) => _win32Api = win32Api;

    public bool TryCreateForWhatsApp(out GraphicsCaptureItem? item)
    {
        item = null;

        // Guard: Windows.Graphics.Capture requires Windows 10 1803+ with hardware support.
        if (!GraphicsCaptureSession.IsSupported())
        {
            System.Diagnostics.Debug.WriteLine("[CaptureItemFactory] Windows.Graphics.Capture not supported on this system.");
            return false;
        }

        // Find the WhatsApp Desktop window.
        var handles = _win32Api.GetWindowHandlesForProcessName("WhatsApp");
        if (handles.Count == 0)
            handles = _win32Api.GetWindowHandlesForProcessName("WhatsAppDesktop");
        if (handles.Count == 0)
            return false;

        IntPtr hwnd = handles[0];

        // --- Activate IGraphicsCaptureItemInterop ---
        //
        // .NET 6+ removed WindowsRuntimeMarshal.GetActivationFactory, so we call
        // RoGetActivationFactory directly via P/Invoke. WindowsCreateString allocates the
        // HSTRING for the runtime class name; WindowsDeleteString frees it in the finally.

        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = WindowsCreateString(runtimeClass, (uint)runtimeClass.Length, out IntPtr hstring);
        if (hr < 0)
        {
            System.Diagnostics.Debug.WriteLine($"[CaptureItemFactory] WindowsCreateString failed: 0x{hr:X8}");
            return false;
        }

        IntPtr factoryPtr = IntPtr.Zero;
        IntPtr itemAbi = IntPtr.Zero;

        try
        {
            var interopIid = IID_IGraphicsCaptureItemInterop;
            hr = RoGetActivationFactory(hstring, ref interopIid, out factoryPtr);
            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureItemFactory] RoGetActivationFactory failed: 0x{hr:X8}");
                return false;
            }

            // Marshal the raw factory pointer to the ComImport interface for vtable dispatch.
            // GetObjectForIUnknown creates an RCW; the cast QIs for IGraphicsCaptureItemInterop.
            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);

            var itemIid = IID_IGraphicsCaptureItem;
            hr = interop.CreateForWindow(hwnd, ref itemIid, out itemAbi);
            if (hr < 0)
            {
                System.Diagnostics.Debug.WriteLine($"[CaptureItemFactory] CreateForWindow failed: 0x{hr:X8}");
                return false;
            }

            // FromAbi increments the WinRT object's ref count so it's safe to release itemAbi below.
            item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemAbi);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CaptureItemFactory] Unexpected exception: {ex.Message}");
            return false;
        }
        finally
        {
            if (itemAbi != IntPtr.Zero) Marshal.Release(itemAbi);
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
            WindowsDeleteString(hstring);
        }
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

    // ── P/Invokes (combase.dll) ───────────────────────────────────────────────

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        ref Guid iid,
        out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);
}
