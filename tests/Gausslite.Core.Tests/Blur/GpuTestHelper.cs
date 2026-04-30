using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Gausslite.Core.Blur;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.Core.Tests.Blur;

/// <summary>
/// Minimal helper that creates real Win2D / D3D11 objects for GPU-gated tests.
/// Only call when <c>GraphicsCaptureSession.IsSupported()</c> is true.
/// </summary>
internal static class GpuTestHelper
{
    private static readonly Guid IDXGIDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    internal static Win2DBlurRenderTarget CreateRenderTarget(float width, float height)
    {
        // Create a hardware D3D11 device (BGRA support required by Win2D).
        const int  D3D_DRIVER_TYPE_HARDWARE        = 1;
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        const uint D3D11_SDK_VERSION               = 7;

        int hr = D3D11CreateDevice(
            IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero, 0, D3D11_SDK_VERSION,
            out IntPtr devicePtr, IntPtr.Zero, IntPtr.Zero);
        Marshal.ThrowExceptionForHR(hr);

        IDirect3DDevice d3dDevice;
        CanvasDevice canvasDevice;

        try
        {
            var dxgiGuid = IDXGIDeviceGuid;
            hr = Marshal.QueryInterface(devicePtr, ref dxgiGuid, out IntPtr dxgiPtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiPtr, out IntPtr winrtPtr);
                Marshal.ThrowExceptionForHR(hr);
                try { d3dDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtPtr); }
                finally { Marshal.Release(winrtPtr); }
            }
            finally { Marshal.Release(dxgiPtr); }
        }
        finally { Marshal.Release(devicePtr); }

        canvasDevice = CanvasDevice.CreateFromDirect3D11Device(d3dDevice);
        return new Win2DBlurRenderTarget(canvasDevice, d3dDevice, width, height);
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter, int DriverType, IntPtr Software, uint Flags,
        IntPtr pFeatureLevels, uint FeatureLevels, uint SDKVersion,
        out IntPtr ppDevice, IntPtr pFeatureLevel, IntPtr ppImmediateContext);

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
