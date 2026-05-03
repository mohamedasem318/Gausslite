// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Gausslite.Core.Capture;

/// <summary>
/// Concrete <see cref="ICaptureInterop"/> backed by the real Windows.Graphics.Capture WinRT APIs.
/// </summary>
public sealed class WinRTCaptureInterop : ICaptureInterop
{
    /// <inheritdoc/>
    public IDirect3DDevice CreateDirect3DDevice(IntPtr dxgiDevice)
    {
        int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr graphicsDevicePtr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(graphicsDevicePtr);
        }
        finally
        {
            Marshal.Release(graphicsDevicePtr);
        }
    }

    /// <inheritdoc/>
    public ICaptureFramePool CreateFreeThreadedFramePool(IDirect3DDevice device, GraphicsCaptureItem item)
    {
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            item.Size);
        return new WinRTCaptureFramePool(pool);
    }

    /// <inheritdoc/>
    public ICaptureSession CreateSession(ICaptureFramePool pool, GraphicsCaptureItem item)
    {
        var nativePool = ((WinRTCaptureFramePool)pool).NativePool;
        var session = nativePool.CreateCaptureSession(item);
        return new WinRTCaptureSession(session);
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
