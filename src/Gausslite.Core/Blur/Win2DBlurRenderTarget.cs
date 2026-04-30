using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.Core.Blur;

/// <summary>
/// Concrete implementation of <see cref="IBlurRenderTarget"/> and
/// <see cref="INativeBlurRenderTarget"/> backed by a Win2D <see cref="CanvasRenderTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why DXGI_RESOURCE_MISC_SHARED and the custom D3D11 texture path:</b><br/>
/// Win2D's public <see cref="CanvasRenderTarget"/> constructors allocate the backing D3D11 texture
/// internally without the <c>D3D11_RESOURCE_MISC_SHARED</c> flag. That flag is required for
/// <c>IDXGIResource::GetSharedHandle</c> to succeed, which is the first step of the D3D9Ex
/// shared-surface bridge used by <c>D3DImageBridge</c> to present frames in the WPF
/// <see cref="System.Windows.Interop.D3DImage"/>.
/// </para>
/// <para>
/// Solution taken:<br/>
/// 1. Obtain the native <c>ID3D11Device</c> pointer from the WinRT <c>IDirect3DDevice</c>
///    wrapper via <c>IDirect3DDxgiInterfaceAccess.GetInterface</c>.<br/>
/// 2. Call <c>ID3D11Device::CreateTexture2D</c> with
///    <c>D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE | D3D11_RESOURCE_MISC_SHARED</c>
///    and format <c>DXGI_FORMAT_B8G8R8A8_UNORM</c> (matches D3D9Ex A8R8G8B8).<br/>
/// 3. QI the texture for <c>IDXGISurface</c>, wrap it as a WinRT <c>IDirect3DSurface</c>
///    via <c>CreateDirect3D11SurfaceFromDXGISurface</c> (d3d11.dll).<br/>
/// 4. Create the <c>CanvasRenderTarget</c> from the surface via
///    <c>CanvasRenderTarget.CreateFromDirect3D11Surface</c>, giving Win2D a render target
///    it can draw to while preserving the shared handle on the underlying D3D11 texture.
/// </para>
/// </remarks>
internal sealed class Win2DBlurRenderTarget : IBlurRenderTarget, INativeBlurRenderTarget
{
    // ── D3D11 / DXGI constants ────────────────────────────────────────────────
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM  = 87;
    private const uint D3D11_BIND_SHADER_RESOURCE   = 0x8;
    private const uint D3D11_BIND_RENDER_TARGET     = 0x20;
    private const uint D3D11_USAGE_DEFAULT          = 0;
    private const uint D3D11_RESOURCE_MISC_SHARED   = 0x2;

    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IID_ID3D11Device    = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    private static readonly Guid IID_IDXGISurface    = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    // ── State ─────────────────────────────────────────────────────────────────
    internal CanvasRenderTarget CanvasRenderTarget { get; }
    private readonly IDirect3DSurface _surface;
    private bool _disposed;

    public float Width  { get; }
    public float Height { get; }

    internal Win2DBlurRenderTarget(CanvasDevice canvasDevice, IDirect3DDevice d3dDevice, float width, float height)
    {
        Width  = width;
        Height = height;

        IntPtr d3d11DevicePtr  = IntPtr.Zero;
        IntPtr texturePtr      = IntPtr.Zero;
        IntPtr dxgiSurfacePtr  = IntPtr.Zero;
        IntPtr winrtSurfacePtr = IntPtr.Zero;

        try
        {
            // Step 1 — get ID3D11Device from the WinRT IDirect3DDevice wrapper.
            // The WinRT wrapper implements IDirect3DDxgiInterfaceAccess, which exposes
            // the raw COM pointer to any underlying native interface on demand.
            d3d11DevicePtr = GetD3D11DevicePtr(d3dDevice);

            // Step 2 — create a D3D11 texture with D3D11_RESOURCE_MISC_SHARED.
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
                MiscFlags         = D3D11_RESOURCE_MISC_SHARED,
            };

            var d3d11Device = (ID3D11Device)Marshal.GetObjectForIUnknown(d3d11DevicePtr);
            int hr = d3d11Device.CreateTexture2D(ref desc, IntPtr.Zero, out texturePtr);
            Marshal.ThrowExceptionForHR(hr);

            // Step 3 — QI the texture for IDXGISurface, then wrap as WinRT IDirect3DSurface.
            var dxgiSurfaceGuid = IID_IDXGISurface;
            hr = Marshal.QueryInterface(texturePtr, ref dxgiSurfaceGuid, out dxgiSurfacePtr);
            Marshal.ThrowExceptionForHR(hr);

            hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurfacePtr, out winrtSurfacePtr);
            Marshal.ThrowExceptionForHR(hr);

            // Cache the IDirect3DSurface; the same instance is required across calls (spec).
            _surface = MarshalInterface<IDirect3DSurface>.FromAbi(winrtSurfacePtr);

            // Step 4 — wrap in a Win2D CanvasRenderTarget so drawing sessions can target it.
            CanvasRenderTarget = CanvasRenderTarget.CreateFromDirect3D11Surface(canvasDevice, _surface, dpi: 96f);
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
    public IDirect3DSurface GetDirect3DSurface() => _surface;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CanvasRenderTarget.Dispose();
        // _surface is a WinRT managed wrapper; ref released when GC collects it.
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IntPtr GetD3D11DevicePtr(IDirect3DDevice d3dDevice)
    {
        // IDirect3DDevice (WinRT) is a wrapper. CsWinRT exposes the raw IInspectable*
        // via IWinRTObject.NativeObject.ThisPtr. From there QI for IDirect3DDxgiInterfaceAccess
        // lets us retrieve any underlying native interface by GUID.
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
            var dxgiAccess = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(dxgiAccessPtr);
            hr = dxgiAccess.GetInterface(in IID_ID3D11Device, out IntPtr d3d11DevicePtr);
            Marshal.ThrowExceptionForHR(hr);
            return d3d11DevicePtr; // caller must release
        }
        finally
        {
            Marshal.Release(dxgiAccessPtr);
        }
    }

    // ── COM interface definitions ─────────────────────────────────────────────

    /// <summary>
    /// Exposes the native COM interfaces underlying a WinRT object (same pattern as in
    /// <c>Gausslite.Overlay.Interop.ComInterop</c>; redefined here to avoid a Core→Overlay reference).
    /// </summary>
    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(in Guid iid, out IntPtr ppvObject);
    }

    /// <summary>
    /// Minimal ID3D11Device vtable: only the first three own methods are needed here
    /// (CreateBuffer, CreateTexture1D, CreateTexture2D — vtable slots 3–5 after IUnknown).
    /// </summary>
    [ComImport]
    [Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3D11Device
    {
        [PreserveSig] int CreateBuffer(IntPtr pDesc, IntPtr pInitialData, out IntPtr ppBuffer);
        [PreserveSig] int CreateTexture1D(IntPtr pDesc, IntPtr pInitialData, out IntPtr ppTexture1D);
        [PreserveSig] int CreateTexture2D(ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public uint SampleDescCount;    // DXGI_SAMPLE_DESC.Count
        public uint SampleDescQuality;  // DXGI_SAMPLE_DESC.Quality
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    // ── P/Invoke (d3d11.dll) ─────────────────────────────────────────────────

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(
        IntPtr dxgiSurface,
        out IntPtr graphicsSurface);
}
