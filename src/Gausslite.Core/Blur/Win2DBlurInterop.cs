using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.Core.Blur;

/// <summary>
/// Concrete <see cref="IBlurInterop"/> backed by Win2D (Microsoft.Graphics.Win2D).
/// Handles CanvasDevice lifetime, render-target creation, and Gaussian blur rendering.
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
        var device = ((Win2DCanvasDeviceWrapper)canvasDevice).CanvasDevice;
        // dpi: 96f keeps DIPs == pixels, matching the render target convention.
        var crt = new CanvasRenderTarget(device, width, height, 96f);
        return new Win2DCachedFrame(crt, width, height);
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
    //   ID3D11Device::GetImmediateContext  → slot 40
    //   ID3D11DeviceContext::Flush         → slot 111

    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess =
        new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    private static readonly Guid IID_ID3D11Device =
        new("db6f6ddb-ac77-4e88-8253-819df9bbf140");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetImmediateContextDelegate(IntPtr pDevice, out IntPtr ppImmediateContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FlushDelegate(IntPtr pContext);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(in Guid iid, out IntPtr ppvObject);
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
            // Step 2: Get the underlying ID3D11Device pointer.
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPtr);
            hr = access.GetInterface(in IID_ID3D11Device, out IntPtr d3d11DevicePtr);
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
}
