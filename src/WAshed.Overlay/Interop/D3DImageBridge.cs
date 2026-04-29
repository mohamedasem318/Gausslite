using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SharpDX.Direct3D9;
using WAshed.Core.Blur;
using WAshed.Core.Diagnostics;
using Windows.Graphics.DirectX.Direct3D11;

namespace WAshed.Overlay.Interop;

/// <summary>
/// Imports a Win2D D3D11 render-target into a WPF <see cref="D3DImage"/> via the
/// standard D3D9Ex shared-surface bridge:
///   IDirect3DSurface (WinRT) → IDirect3DDxgiInterfaceAccess → ID3D11Texture2D
///   → IDXGIResource.GetSharedHandle → DeviceEx.CreateTexture → D3DImage.SetBackBuffer
/// </summary>
internal sealed class D3DImageBridge : ID3DImageBridge
{
    // Well-known DirectX interface GUIDs
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IDXGIResource   = new("035f3ab4-482e-4e50-b41f-8a7f8bd8960b");

    private readonly Direct3DEx _d3dEx;
    private readonly DeviceEx   _device;
    private bool _disposed;

    // Update diagnostics are written from the UI thread but kept atomic for consistency.
    private int _updateCallCount;

    public D3DImageBridge()
    {
        _d3dEx = new Direct3DEx();

        // A 1×1 windowed swap chain satisfies D3D9Ex device requirements;
        // the swap chain is never actually presented — only the shared texture matters.
        var pp = new PresentParameters
        {
            Windowed              = true,
            SwapEffect            = SwapEffect.Discard,
            DeviceWindowHandle    = NativeWindow.GetDesktopWindow(),
            PresentationInterval  = PresentInterval.Default,
            BackBufferWidth       = 1,
            BackBufferHeight      = 1,
            BackBufferFormat      = Format.Unknown,
        };

        _device = new DeviceEx(
            _d3dEx,
            0,
            DeviceType.Hardware,
            IntPtr.Zero,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
            pp);
    }

    /// <inheritdoc/>
    public void UpdateD3DImage(D3DImage d3dImage, IBlurRenderTarget blurTarget)
    {
        if (_disposed) return;

        int callNumber = Interlocked.Increment(ref _updateCallCount);
        bool shouldLog = callNumber <= 5;
        string prefix = $"[bridge call #{callNumber}]";

        try
        {
            if (blurTarget is not INativeBlurRenderTarget nativeTarget)
            {
                if (shouldLog)
                    DiagLog.Warn($"{prefix} D3DImageBridge.UpdateD3DImage: blurTarget does not implement INativeBlurRenderTarget — returning without update");
                return;
            }

            if (shouldLog) DiagLog.Info($"{prefix} D3DImageBridge.UpdateD3DImage: acquiring IDirect3DSurface from render target...");
            IDirect3DSurface direct3DSurface = nativeTarget.GetDirect3DSurface();
            if (shouldLog) DiagLog.Info($"{prefix} D3DImageBridge.UpdateD3DImage: IDirect3DSurface acquired, beginning WinRT→DXGI unwrap...");

            IntPtr sharedHandle = GetSharedHandleFromSurface(direct3DSurface, shouldLog, callNumber);
            if (shouldLog) DiagLog.Info($"{prefix} D3DImageBridge.UpdateD3DImage: GetSharedHandle returned 0x{sharedHandle:X16}");

            if (sharedHandle == IntPtr.Zero)
            {
                if (shouldLog)
                    DiagLog.Warn($"{prefix} D3DImageBridge: ABORT — shared handle is zero, render target was not created with DXGI_RESOURCE_MISC_SHARED");
                return;
            }

            if (shouldLog) DiagLog.Info($"{prefix} D3DImageBridge.UpdateD3DImage: creating D3D9Ex texture via shared handle...");
            using Texture texture9 = new(
                _device,
                width:       (int)blurTarget.Width,
                height:      (int)blurTarget.Height,
                levelCount:  1,
                usage:       Usage.RenderTarget,
                format:      Format.A8R8G8B8,
                pool:        Pool.Default,
                sharedHandle: ref sharedHandle);

            using Surface surface9 = texture9.GetSurfaceLevel(0);

            if (shouldLog) DiagLog.Info($"{prefix} D3DImageBridge.UpdateD3DImage: calling D3DImage.Lock / SetBackBuffer / Unlock...");
            d3dImage.Lock();
            d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface9.NativePointer);
            d3dImage.AddDirtyRect(new Int32Rect(0, 0, (int)blurTarget.Width, (int)blurTarget.Height));
            d3dImage.Unlock();
            if (shouldLog) DiagLog.Info($"{prefix} D3DImageBridge.UpdateD3DImage: SetBackBuffer completed successfully");
        }
        catch (Exception ex)
        {
            DiagLog.Warn($"{prefix} D3DImageBridge.UpdateD3DImage: exception during surface bridge operations", ex);
            // Do NOT re-throw — one bad frame must not crash the app.
        }
    }

    /// <summary>
    /// Walks the WinRT → DXGI interop chain to retrieve the shared HANDLE of the
    /// underlying D3D11 texture.  Returns <see cref="IntPtr.Zero"/> on any failure
    /// (e.g. texture created without DXGI_RESOURCE_MISC_SHARED).
    /// </summary>
    private static IntPtr GetSharedHandleFromSurface(IDirect3DSurface surface, bool log, int callNumber)
    {
        string prefix = $"[bridge call #{callNumber}]";

        // CsWinRT projects all WinRT types as IWinRTObject, giving access to the
        // raw IInspectable* (which is also IUnknown*) without an extra AddRef.
        if (surface is not WinRT.IWinRTObject winrtObj)
        {
            if (log) DiagLog.Warn($"{prefix} D3DImageBridge.GetSharedHandle: surface is not IWinRTObject — cannot unwrap");
            return IntPtr.Zero;
        }
        if (winrtObj.NativeObject is not { } objRef)
        {
            if (log) DiagLog.Warn($"{prefix} D3DImageBridge.GetSharedHandle: NativeObject is null");
            return IntPtr.Zero;
        }

        IntPtr surfacePtr = objRef.ThisPtr; // borrowed — do NOT release
        if (log) DiagLog.Info($"{prefix} D3DImageBridge.GetSharedHandle: surfacePtr=0x{surfacePtr:X16}");

        // Step 1: QI for IDirect3DDxgiInterfaceAccess (AddRefs result)
        var dxgiAccessGuid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        int hr = Marshal.QueryInterface(surfacePtr, ref dxgiAccessGuid, out IntPtr dxgiAccessPtr);
        if (log) DiagLog.Info($"{prefix} D3DImageBridge.GetSharedHandle: QI IDirect3DDxgiInterfaceAccess hr=0x{hr:X8}");
        if (hr < 0) return IntPtr.Zero;

        try
        {
            var dxgiAccess = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(dxgiAccessPtr);

            // Step 2: Ask for the underlying ID3D11Texture2D (returned pointer is AddRef'd)
            int tex2DHr = dxgiAccess.GetInterface(in IID_ID3D11Texture2D, out IntPtr texture2DPtr);
            if (log) DiagLog.Info($"{prefix} D3DImageBridge.GetSharedHandle: GetInterface ID3D11Texture2D hr=0x{tex2DHr:X8}");
            if (tex2DHr < 0) return IntPtr.Zero;

            try
            {
                // Step 3: QI for IDXGIResource on the D3D11 texture (AddRefs result)
                var resGuid = IID_IDXGIResource;
                int resHr = Marshal.QueryInterface(texture2DPtr, ref resGuid, out IntPtr dxgiResPtr);
                if (log) DiagLog.Info($"{prefix} D3DImageBridge.GetSharedHandle: QI IDXGIResource hr=0x{resHr:X8}");
                if (resHr < 0) return IntPtr.Zero;

                try
                {
                    var dxgiRes = (IDXGIResource)Marshal.GetObjectForIUnknown(dxgiResPtr);

                    // Step 4: Get the NT/legacy shared handle (no AddRef — it's a HANDLE)
                    int shareHr = dxgiRes.GetSharedHandle(out IntPtr handle);
                    if (log) DiagLog.Info($"{prefix} D3DImageBridge.GetSharedHandle: GetSharedHandle hr=0x{shareHr:X8}, handle=0x{handle:X16}");
                    return shareHr < 0 ? IntPtr.Zero : handle;
                }
                finally
                {
                    Marshal.Release(dxgiResPtr);
                }
            }
            finally
            {
                Marshal.Release(texture2DPtr);
            }
        }
        finally
        {
            Marshal.Release(dxgiAccessPtr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device.Dispose();
        _d3dEx.Dispose();
    }
}
