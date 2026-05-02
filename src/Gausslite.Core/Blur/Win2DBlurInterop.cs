using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.Core.Blur;

/// <summary>
/// Concrete <see cref="IBlurInterop"/> backed by Win2D (Microsoft.Graphics.Win2D).
/// Handles CanvasDevice lifetime, render-target creation, Gaussian blur rendering,
/// and GPU→CPU frame readback via D3D11 staging textures.
/// </summary>
public sealed class Win2DBlurInterop : IBlurInterop
{
    /// <inheritdoc/>
    public IBlurCanvasDevice CreateCanvasDevice(IDirect3DDevice device)
    {
        // CreateFromDirect3D11Device wraps the existing D3D11 device rather than allocating a new one,
        // ensuring Win2D and the CaptureEngine share the same GPU device and can exchange surfaces.
        var canvasDevice = CanvasDevice.CreateFromDirect3D11Device(device);
        return new Win2DCanvasDeviceWrapper(canvasDevice, device);
    }

    /// <inheritdoc/>
    public IBlurRenderTarget CreateRenderTarget(IBlurCanvasDevice canvasDevice, float width, float height)
    {
        var wrapper = (Win2DCanvasDeviceWrapper)canvasDevice;
        return new Win2DBlurRenderTarget(wrapper.CanvasDevice, wrapper.Direct3DDevice, width, height);
    }

    /// <inheritdoc/>
    public void DrawBlur(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget, ICaptureFrame frame, float radius)
    {
        var device = ((Win2DCanvasDeviceWrapper)canvasDevice).CanvasDevice;
        var rt     = (Win2DBlurRenderTarget)renderTarget;

        // Create a temporary CanvasBitmap from the captured frame surface.
        // CanvasBitmap.CreateFromDirect3D11Surface wraps the IDirect3DSurface without copying pixels.
        using var frameBitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Frame.Surface);

        var blurEffect = new GaussianBlurEffect
        {
            Source     = frameBitmap,
            BlurAmount = radius,
        };

        using var session = rt.CanvasRenderTarget.CreateDrawingSession();
        session.DrawImage(blurEffect);
    }

    /// <inheritdoc/>
    public (float Width, float Height) GetFrameSize(ICaptureFrame frame)
    {
        var size = frame.ContentSize;
        return ((float)size.Width, (float)size.Height);
    }

    /// <inheritdoc/>
    public ICachedFrame CreateCachedFrame(IBlurCanvasDevice canvasDevice, float width, float height)
    {
        // Create the backing D3D11 texture explicitly so we can keep the IDirect3DSurface handle.
        // This lets TryReadBgra later QI the surface for ID3D11Texture2D without a complex Win2D
        // COM traversal.  No MISC_SHARED needed — this texture is not shared with D3D9Ex.
        var wrapper = (Win2DCanvasDeviceWrapper)canvasDevice;

        IntPtr d3d11DevicePtr  = IntPtr.Zero;
        IntPtr texturePtr      = IntPtr.Zero;
        IntPtr dxgiSurfacePtr  = IntPtr.Zero;
        IntPtr winrtSurfacePtr = IntPtr.Zero;

        try
        {
            d3d11DevicePtr = GetD3D11DevicePtr(wrapper.Direct3DDevice);

            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width             = (uint)width,
                Height            = (uint)height,
                MipLevels         = 1,
                ArraySize         = 1,
                Format            = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDescCount   = 1,
                SampleDescQuality = 0,
                Usage             = D3D11_USAGE_DEFAULT,
                BindFlags         = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE,
                CPUAccessFlags    = 0,
                MiscFlags         = 0,
            };

            int hr = CreateTexture2DRaw(d3d11DevicePtr, ref desc, out texturePtr);
            Marshal.ThrowExceptionForHR(hr);

            var dxgiSurfaceGuid = IID_IDXGISurface;
            hr = Marshal.QueryInterface(texturePtr, ref dxgiSurfaceGuid, out dxgiSurfacePtr);
            Marshal.ThrowExceptionForHR(hr);

            hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurfacePtr, out winrtSurfacePtr);
            Marshal.ThrowExceptionForHR(hr);

            var surface = MarshalInterface<IDirect3DSurface>.FromAbi(winrtSurfacePtr);
            var crt = CanvasRenderTarget.CreateFromDirect3D11Surface(wrapper.CanvasDevice, surface, dpi: 96f);
            return new Win2DCachedFrame(crt, surface, width, height);
        }
        finally
        {
            if (winrtSurfacePtr != IntPtr.Zero) Marshal.Release(winrtSurfacePtr);
            if (dxgiSurfacePtr  != IntPtr.Zero) Marshal.Release(dxgiSurfacePtr);
            if (texturePtr      != IntPtr.Zero) Marshal.Release(texturePtr);
            if (d3d11DevicePtr  != IntPtr.Zero) Marshal.Release(d3d11DevicePtr);
        }
    }

    /// <inheritdoc/>
    public void UpdateCachedFrame(IBlurCanvasDevice canvasDevice, ICachedFrame cachedFrame, ICaptureFrame frame)
    {
        var device = ((Win2DCanvasDeviceWrapper)canvasDevice).CanvasDevice;
        var cacheTarget = ((Win2DCachedFrame)cachedFrame).CanvasRenderTarget;
        using var frameBitmap = CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Frame.Surface);
        using var session = cacheTarget.CreateDrawingSession();
        session.DrawImage(frameBitmap);
    }

    /// <inheritdoc/>
    public void DrawBlurFromCache(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget, ICachedFrame cachedFrame, float radius)
    {
        var device = ((Win2DCanvasDeviceWrapper)canvasDevice).CanvasDevice;
        var rt = (Win2DBlurRenderTarget)renderTarget;
        var cacheTarget = ((Win2DCachedFrame)cachedFrame).CanvasRenderTarget;

        var blurEffect = new GaussianBlurEffect
        {
            Source     = cacheTarget,
            BlurAmount = radius,
        };

        using var session = rt.CanvasRenderTarget.CreateDrawingSession();
        session.DrawImage(blurEffect);
    }

    /// <inheritdoc/>
    public void DrawDiagnosticOverlay(IBlurCanvasDevice canvasDevice, IBlurRenderTarget renderTarget)
    {
        var rt = (Win2DBlurRenderTarget)renderTarget;
        using var session = rt.CanvasRenderTarget.CreateDrawingSession();
        session.FillRectangle(0, 0, 200, 200, new Windows.UI.Color { A = 255, R = 255, G = 0, B = 0 });
    }

    /// <inheritdoc/>
    public void FlushDevice(IBlurCanvasDevice canvasDevice)
    {
        var wrapper = (Win2DCanvasDeviceWrapper)canvasDevice;
        FlushD3D11Context(wrapper.Direct3DDevice);
    }

    /// <inheritdoc/>
    public IBlurStagingTexture CreateStagingTexture(IBlurCanvasDevice canvasDevice, float width, float height)
    {
        var wrapper = (Win2DCanvasDeviceWrapper)canvasDevice;
        IntPtr d3d11DevicePtr = GetD3D11DevicePtr(wrapper.Direct3DDevice);

        try
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width             = (uint)width,
                Height            = (uint)height,
                MipLevels         = 1,
                ArraySize         = 1,
                Format            = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDescCount   = 1,
                SampleDescQuality = 0,
                Usage             = D3D11_USAGE_STAGING,
                BindFlags         = 0,
                CPUAccessFlags    = D3D11_CPU_ACCESS_READ,
                MiscFlags         = 0,
            };

            int hr = CreateTexture2DRaw(d3d11DevicePtr, ref desc, out IntPtr stagingPtr);
            Marshal.ThrowExceptionForHR(hr);
            // Win2DBlurStagingTexture takes ownership of stagingPtr — do NOT release here.
            return new Win2DBlurStagingTexture(stagingPtr, width, height);
        }
        finally
        {
            Marshal.Release(d3d11DevicePtr);
        }
    }

    /// <inheritdoc/>
    public bool TryReadBgra(
        IBlurCanvasDevice canvasDevice,
        ICachedFrame cachedFrame,
        IBlurStagingTexture staging,
        out byte[] bgraPixels,
        out int width,
        out int height,
        out int stride)
    {
        bgraPixels = Array.Empty<byte>();
        width = height = stride = 0;

        var cachedFrameImpl = (Win2DCachedFrame)cachedFrame;
        var stagingImpl     = (Win2DBlurStagingTexture)staging;

        if (cachedFrameImpl.Surface is not IWinRTObject surfaceWinRT ||
            surfaceWinRT.NativeObject is not { } surfaceRef)
            return false;

        IntPtr surfaceNativePtr = surfaceRef.ThisPtr; // borrowed — do NOT release

        // QI the surface native pointer for IDirect3DDxgiInterfaceAccess, then reach
        // ID3D11Texture2D via raw vtable dispatch (slot 3).
        //
        // Marshal.GetObjectForIUnknown + managed cast is intentionally absent. The CsWinRT
        // IDirect3DSurface projection is alive and registered in the ComWrappers global table,
        // so GetObjectForIUnknown returns the managed projection rather than a raw RCW. Casting
        // that projection to IDirect3DDxgiInterfaceAccess routes through its CCW which has no
        // entry for this private COM interface → E_NOINTERFACE → InvalidCastException ~90% of
        // calls (GC-timing-dependent on whether the managed wrapper is still in the table).
        // Raw vtable dispatch bypasses the managed layer entirely.
        var accessGuid = IID_IDirect3DDxgiInterfaceAccess;
        int hr = Marshal.QueryInterface(surfaceNativePtr, ref accessGuid, out IntPtr accessPtr);
        if (hr < 0) return false;

        try
        {
            hr = CallGetInterface(accessPtr, in IID_ID3D11Texture2D, out IntPtr srcTexturePtr);
            if (hr < 0) return false;

            try
            {
                // Obtain the D3D11 device via the same vtable-only path.
                var wrapper = (Win2DCanvasDeviceWrapper)canvasDevice;
                if (wrapper.Direct3DDevice is not IWinRTObject devWinRT ||
                    devWinRT.NativeObject is not { } devRef)
                    return false;

                var devAccessGuid = IID_IDirect3DDxgiInterfaceAccess;
                hr = Marshal.QueryInterface(devRef.ThisPtr, ref devAccessGuid, out IntPtr devAccessPtr);
                if (hr < 0) return false;

                IntPtr d3d11DevicePtr = IntPtr.Zero;
                try
                {
                    hr = CallGetInterface(devAccessPtr, in IID_ID3D11Device, out d3d11DevicePtr);
                    if (hr < 0) return false;
                }
                finally
                {
                    Marshal.Release(devAccessPtr);
                }

                try
                {
                    // ID3D11Device::GetImmediateContext — vtable slot 40.
                    IntPtr deviceVtable = Marshal.ReadIntPtr(d3d11DevicePtr);
                    var getCtx = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(
                        Marshal.ReadIntPtr(deviceVtable, 40 * IntPtr.Size));
                    getCtx(d3d11DevicePtr, out IntPtr contextPtr);
                    if (contextPtr == IntPtr.Zero) return false;

                    try
                    {
                        IntPtr ctxVtable = Marshal.ReadIntPtr(contextPtr);

                        // Flush pending GPU commands before the copy so we read the latest frame.
                        // See the FlushD3D11Context note: omitting this can cause stale-frame readback
                        // on the synchronous UI-thread path for the same reason it affects TryRenderCurrentFrame.
                        var flush = Marshal.GetDelegateForFunctionPointer<FlushDelegate>(
                            Marshal.ReadIntPtr(ctxVtable, 111 * IntPtr.Size));
                        flush(contextPtr);

                        // ID3D11DeviceContext::CopyResource — vtable slot 47.
                        var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                            Marshal.ReadIntPtr(ctxVtable, 47 * IntPtr.Size));
                        copyResource(contextPtr, stagingImpl.TexturePtr, srcTexturePtr);

                        // ID3D11DeviceContext::Map — vtable slot 14.
                        var map = Marshal.GetDelegateForFunctionPointer<MapDelegate>(
                            Marshal.ReadIntPtr(ctxVtable, 14 * IntPtr.Size));
                        hr = map(contextPtr, stagingImpl.TexturePtr, 0, D3D11_MAP_READ, 0,
                                 out D3D11_MAPPED_SUBRESOURCE mapped);
                        if (hr < 0) return false;

                        try
                        {
                            int w        = (int)cachedFrame.Width;
                            int h        = (int)cachedFrame.Height;
                            int rowPitch = (int)mapped.RowPitch;
                            var pixels   = new byte[rowPitch * h];
                            Marshal.Copy(mapped.pData, pixels, 0, pixels.Length);
                            bgraPixels = pixels;
                            width  = w;
                            height = h;
                            stride = rowPitch;
                            return true;
                        }
                        finally
                        {
                            // ID3D11DeviceContext::Unmap — vtable slot 15.
                            var unmap = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(
                                Marshal.ReadIntPtr(ctxVtable, 15 * IntPtr.Size));
                            unmap(contextPtr, stagingImpl.TexturePtr, 0);
                        }
                    }
                    finally
                    {
                        Marshal.Release(contextPtr);
                    }
                }
                finally
                {
                    Marshal.Release(d3d11DevicePtr);
                }
            }
            finally
            {
                Marshal.Release(srcTexturePtr);
            }
        }
        finally
        {
            Marshal.Release(accessPtr);
        }
    }

    // ── D3D11 context flush ───────────────────────────────────────────────────
    //
    // Win2D submits drawing commands to the D3D11 immediate context via D2D1.  On the
    // on-demand (UI-thread) re-render path every step is synchronous: Win2D writes → the
    // D3D9Ex bridge opens the same shared texture → WPF composites.  Without an explicit
    // ID3D11DeviceContext::Flush() the D3D11 UMD driver may still hold pending commands in
    // its internal CPU-side buffer.  The D3D9Ex reader then sees the pre-render GPU content,
    // so the displayed blur never changes despite a correct TryRenderCurrentFrame execution.
    //
    // Capture frames work by coincidence: the multi-thread dispatch from WGC callback through
    // Dispatcher.Invoke adds ~0.5 ms latency during which the UMD auto-flushes.
    //
    // We resolve this by calling Flush() on the immediate context via raw vtable dispatch,
    // which avoids defining dozens of placeholder COM interface methods.
    //
    // Vtable slot constants (0-based, including IUnknown):
    //   ID3D11Device::GetImmediateContext       → slot 40
    //   ID3D11DeviceContext::Flush              → slot 111
    //   ID3D11DeviceContext::Map                → slot 14
    //   ID3D11DeviceContext::Unmap              → slot 15
    //   ID3D11DeviceContext::CopyResource       → slot 47

    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess =
        new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private static readonly Guid IID_ID3D11Device =
        new("db6f6ddb-ac77-4e88-8253-819df9bbf140");

    private static readonly Guid IID_ID3D11Texture2D =
        new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private static readonly Guid IID_IDXGISurface =
        new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    // D3D11 / DXGI constants
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM  = 87;
    private const uint D3D11_BIND_SHADER_RESOURCE   = 0x8;
    private const uint D3D11_BIND_RENDER_TARGET     = 0x20;
    private const uint D3D11_USAGE_DEFAULT          = 0;
    private const uint D3D11_USAGE_STAGING          = 3;
    private const uint D3D11_CPU_ACCESS_READ        = 0x20000;
    private const uint D3D11_MAP_READ               = 1;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetImmediateContextDelegate(IntPtr pDevice, out IntPtr ppImmediateContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FlushDelegate(IntPtr pContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(IntPtr pContext, IntPtr pDst, IntPtr pSrc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(
        IntPtr pContext, IntPtr pResource, uint subresource,
        uint mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE pMapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(IntPtr pContext, IntPtr pResource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(
        IntPtr pDevice, ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInterfaceDelegate(IntPtr pThis, in Guid iid, out IntPtr ppvObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint   RowPitch;
        public uint   DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleDescCount;
        public uint SampleDescQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    private static void FlushD3D11Context(IDirect3DDevice d3dDevice)
    {
        if (d3dDevice is not IWinRTObject winrtObj || winrtObj.NativeObject is not { } objRef)
            return;

        IntPtr deviceWrapperPtr = objRef.ThisPtr; // borrowed — do NOT release

        // Step 1: QI the WinRT device wrapper for IDirect3DDxgiInterfaceAccess.
        var accessGuid = IID_IDirect3DDxgiInterfaceAccess;
        int hr = Marshal.QueryInterface(deviceWrapperPtr, ref accessGuid, out IntPtr accessPtr);
        if (hr < 0) return;

        try
        {
            // Step 2: Get the underlying ID3D11Device pointer via raw vtable dispatch.
            hr = CallGetInterface(accessPtr, in IID_ID3D11Device, out IntPtr d3d11DevicePtr);
            if (hr < 0) return;

            try
            {
                // Step 3: Call ID3D11Device::GetImmediateContext via vtable slot 40.
                IntPtr deviceVtable = Marshal.ReadIntPtr(d3d11DevicePtr);
                IntPtr getCtxFnPtr  = Marshal.ReadIntPtr(deviceVtable, 40 * IntPtr.Size);
                var getCtx = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(getCtxFnPtr);
                getCtx(d3d11DevicePtr, out IntPtr contextPtr);
                if (contextPtr == IntPtr.Zero) return;

                try
                {
                    // Step 4: Call ID3D11DeviceContext::Flush via vtable slot 111.
                    IntPtr ctxVtable = Marshal.ReadIntPtr(contextPtr);
                    IntPtr flushFnPtr = Marshal.ReadIntPtr(ctxVtable, 111 * IntPtr.Size);
                    var flush = Marshal.GetDelegateForFunctionPointer<FlushDelegate>(flushFnPtr);
                    flush(contextPtr);
                }
                finally
                {
                    Marshal.Release(contextPtr);
                }
            }
            finally
            {
                Marshal.Release(d3d11DevicePtr);
            }
        }
        finally
        {
            Marshal.Release(accessPtr);
        }
    }

    /// <summary>
    /// Retrieves the raw ID3D11Device* from a WinRT IDirect3DDevice wrapper via
    /// IDirect3DDxgiInterfaceAccess.  Caller must release the returned pointer.
    /// </summary>
    private static IntPtr GetD3D11DevicePtr(IDirect3DDevice d3dDevice)
    {
        if (d3dDevice is not IWinRTObject winrtObj)
            throw new InvalidOperationException("IDirect3DDevice is not a CsWinRT IWinRTObject.");
        if (winrtObj.NativeObject is not { } objRef)
            throw new InvalidOperationException("IWinRTObject.NativeObject is null.");

        IntPtr inspectablePtr = objRef.ThisPtr; // borrowed — do NOT release

        var accessGuid = IID_IDirect3DDxgiInterfaceAccess;
        int hr = Marshal.QueryInterface(inspectablePtr, ref accessGuid, out IntPtr dxgiAccessPtr);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            hr = CallGetInterface(dxgiAccessPtr, in IID_ID3D11Device, out IntPtr d3d11DevicePtr);
            Marshal.ThrowExceptionForHR(hr);
            return d3d11DevicePtr; // caller must release
        }
        finally
        {
            Marshal.Release(dxgiAccessPtr);
        }
    }

    /// <summary>
    /// Calls IDirect3DDxgiInterfaceAccess::GetInterface (vtable slot 3) via raw pointer dispatch,
    /// bypassing any managed COM wrapper that may be registered for the same IUnknown identity.
    /// </summary>
    private static int CallGetInterface(IntPtr dxgiAccessPtr, in Guid iid, out IntPtr result)
    {
        IntPtr vtable = Marshal.ReadIntPtr(dxgiAccessPtr);
        var fn = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(
            Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
        return fn(dxgiAccessPtr, iid, out result);
    }

    private static int CreateTexture2DRaw(IntPtr devicePtr, ref D3D11_TEXTURE2D_DESC desc, out IntPtr texture)
    {
        IntPtr vtable = Marshal.ReadIntPtr(devicePtr);
        var fn = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
            Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size));
        return fn(devicePtr, ref desc, IntPtr.Zero, out texture);
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(
        IntPtr dxgiSurface,
        out IntPtr graphicsSurface);
}
