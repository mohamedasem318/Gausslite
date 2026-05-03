// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Gausslite.Core.Blur;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.Core.Tests.Blur;

/// <summary>
/// Integration tests for <see cref="Win2DBlurInterop"/> that use a real WARP (software) D3D11
/// device.  These tests do NOT mock IBlurInterop — they call the real implementation to guard
/// against regressions that the existing NSubstitute-based tests cannot catch.
///
/// Primary regression guarded: <see cref="Win2DBlurInterop.TryReadBgra"/> must not throw
/// InvalidCastException when the <see cref="ICachedFrame"/>'s CsWinRT IDirect3DSurface wrapper
/// is alive and registered in the ComWrappers global instance table.
/// </summary>
public sealed class Win2DBlurInteropIntegrationTests : IDisposable
{
    private readonly Win2DBlurInterop _interop = new();
    private readonly IDirect3DDevice  _d3dDevice;
    private readonly IBlurCanvasDevice _canvasDevice;

    public Win2DBlurInteropIntegrationTests()
    {
        _d3dDevice    = CreateWarpDirect3DDevice();
        _canvasDevice = _interop.CreateCanvasDevice(_d3dDevice);
    }

    /// <summary>
    /// Regression test for the CsWinRT ComWrappers identity bug (GH issue #33 / PR #34).
    ///
    /// <c>TryReadBgra</c> must succeed while <paramref name="cachedFrame"/> (and its
    /// <c>IDirect3DSurface</c> CsWinRT projection) is still alive in the ComWrappers table.
    ///
    /// Regression: reverting to <c>Marshal.GetObjectForIUnknown(accessPtr)</c> + managed cast to
    /// <c>IDirect3DDxgiInterfaceAccess</c> causes this test to throw
    /// <see cref="InvalidCastException"/> because the live CsWinRT projection is returned
    /// instead of a raw RCW, and the CCW returns E_NOINTERFACE for the private COM interface.
    /// </summary>
    [Fact]
    public void TryReadBgra_WhileCachedFrameIsAlive_DoesNotThrowAndReturnsTrue()
    {
        const float w = 4f, h = 4f;

        // CreateCachedFrame internally calls MarshalInterface<IDirect3DSurface>.FromAbi, which
        // registers the native WinRT surface in CsWinRT's ComWrappers global instance table.
        // Keeping cachedFrame alive here ensures that registration is still present when
        // TryReadBgra runs — the precise condition that caused the old InvalidCastException.
        using ICachedFrame      cachedFrame = _interop.CreateCachedFrame(_canvasDevice, w, h);
        using IBlurStagingTexture  staging  = _interop.CreateStagingTexture(_canvasDevice, w, h);

        GC.KeepAlive(cachedFrame); // belt-and-suspenders: suppress any JIT liveness optimisation

        var ok = _interop.TryReadBgra(_canvasDevice, cachedFrame, staging,
                     out byte[] pixels, out int width, out int height, out int stride);

        Assert.True(ok);
        Assert.Equal((int)w, width);
        Assert.Equal((int)h, height);
        Assert.True(stride >= width * 4);
        Assert.Equal(stride * height, pixels.Length);
    }

    /// <summary>
    /// Regression test for the ID3D11Device-layer CsWinRT identity bug (sites D/E/F from the
    /// v0.2.0 audit).
    ///
    /// CreateRenderTarget, CreateCachedFrame, CreateStagingTexture, and FlushDevice all reach the
    /// underlying ID3D11Device via IDirect3DDxgiInterfaceAccess::GetInterface.  On a hardware GPU
    /// the returned ID3D11Device* shares IUnknown identity with the registered CsWinRT
    /// IDirect3DDevice projection; Marshal.GetObjectForIUnknown then returns that projection, and
    /// any managed cast to ID3D11Device or IDirect3DDxgiInterfaceAccess throws
    /// InvalidCastException.
    ///
    /// All sites are fixed to use raw vtable dispatch (CreateTexture2DRaw / CallGetInterface).
    /// This test calls every converted site while <c>_d3dDevice</c> — created via
    /// MarshalInterface&lt;IDirect3DDevice&gt;.FromAbi — remains alive and registered in
    /// CsWinRT's ComWrappers table.
    ///
    /// Regression: reverting any site to Marshal.GetObjectForIUnknown + managed cast will throw
    /// InvalidCastException on a hardware GPU.  WARP may not reproduce the IUnknown identity
    /// collapse for the device path (it does for the surface path tested separately above), but
    /// the test still exercises all code paths for correctness.
    /// </summary>
    [Fact]
    public void AllConvertedCallSites_WhileDeviceIsAlive_DoNotThrow()
    {
        const float w = 4f, h = 4f;

        using IBlurRenderTarget   rt          = _interop.CreateRenderTarget(_canvasDevice, w, h);
        using ICachedFrame        cachedFrame = _interop.CreateCachedFrame(_canvasDevice, w, h);
        using IBlurStagingTexture staging     = _interop.CreateStagingTexture(_canvasDevice, w, h);

        // FlushDevice exercises FlushD3D11Context → CallGetInterface (SITE C fix).
        _interop.FlushDevice(_canvasDevice);

        // TryReadBgra exercises the IDirect3DSurface path (surface registered via
        // MarshalInterface.FromAbi inside CreateCachedFrame — IUnknown collision is
        // deterministic in both WARP and hardware, so this is the strongest regression guard).
        var ok = _interop.TryReadBgra(_canvasDevice, cachedFrame, staging,
                     out _, out _, out _, out _);
        Assert.True(ok);
    }

    public void Dispose()
    {
        _canvasDevice.Dispose();
    }

    // ── WARP device helpers ───────────────────────────────────────────────────

    // Creates a D3D11 WARP (software) device and wraps it as IDirect3DDevice for use with Win2D.
    private static IDirect3DDevice CreateWarpDirect3DDevice()
    {
        const uint D3D_DRIVER_TYPE_WARP = 5;
        const uint D3D11_SDK_VERSION    = 7;
        var dxgiDeviceGuid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice

        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20; // required by Win2D / Direct2D
        int hr = D3D11CreateDevice(
            IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out IntPtr d3d11DevicePtr, IntPtr.Zero, IntPtr.Zero);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            hr = Marshal.QueryInterface(d3d11DevicePtr, ref dxgiDeviceGuid, out IntPtr dxgiDevicePtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out IntPtr wrtDevicePtr);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(wrtDevicePtr);
                }
                finally
                {
                    Marshal.Release(wrtDevicePtr);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(d3d11DevicePtr);
        }
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter, uint DriverType, IntPtr Software,
        uint Flags, IntPtr pFeatureLevels, uint FeatureLevels,
        uint SDKVersion, out IntPtr ppDevice,
        IntPtr pFeatureLevel, IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
