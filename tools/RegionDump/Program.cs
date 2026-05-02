using System.Runtime.InteropServices;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.WindowTracking;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

// ── Entry point ──────────────────────────────────────────────────────────────

Console.WriteLine("RegionDump placeholder");

// ── COM interfaces ────────────────────────────────────────────────────────────

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(IntPtr hwnd, ref Guid iid, out IntPtr ppv);
}

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

// ── P/Invoke + GUIDs ──────────────────────────────────────────────────────────

static class NativeMethods
{
    public static readonly Guid IID_IGraphicsCaptureItemInterop =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    public static readonly Guid IID_IGraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    public static readonly Guid IID_IDXGIDevice =
        new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int RoGetActivationFactory(
        IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll", PreserveSig = true)]
    public static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("d3d11.dll", PreserveSig = true)]
    public static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,         // D3D_DRIVER_TYPE_HARDWARE = 1
        IntPtr Software,
        uint Flags,
        IntPtr pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,        // D3D11_SDK_VERSION = 7
        out IntPtr ppDevice,
        IntPtr pFeatureLevel,
        out IntPtr ppImmediateContext);
}
