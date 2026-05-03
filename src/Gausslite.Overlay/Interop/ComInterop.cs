// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;

namespace Gausslite.Overlay.Interop;

/// <summary>
/// Bridges a WinRT <c>IDirect3DSurface</c> to the underlying native COM object so the
/// D3D11 texture pointer can be extracted via <c>IDirect3DDxgiInterfaceAccess</c>.
/// </summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    /// <summary>
    /// Returns a pointer to the native interface identified by <paramref name="iid"/>.
    /// The returned pointer is AddRef'd; the caller must release it.
    /// </summary>
    [PreserveSig]
    int GetInterface(in Guid iid, out IntPtr ppvObject);
}

/// <summary>
/// Minimal vtable-correct declaration of <c>IDXGIResource</c> (DXGI 1.0).
/// Methods are declared in full vtable order (IDXGIObject + IDXGIDeviceSubObject inherited
/// methods first) so COM dispatch lands on the right slot.
/// </summary>
[ComImport]
[Guid("035f3ab4-482e-4e50-b41f-8a7f8bd8960b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIResource
{
    // ── IDXGIObject (vtable slots 3-6) ──────────────────────────────────────
    [PreserveSig] int SetPrivateData(in Guid name, uint dataSize, IntPtr pData);
    [PreserveSig] int SetPrivateDataInterface(in Guid name, IntPtr pUnknown);
    [PreserveSig] int GetPrivateData(in Guid name, ref uint pDataSize, IntPtr pData);
    [PreserveSig] int GetParent(in Guid riid, out IntPtr ppParent);

    // ── IDXGIDeviceSubObject (vtable slot 7) ────────────────────────────────
    [PreserveSig] int GetDevice(in Guid riid, out IntPtr ppDevice);

    // ── IDXGIResource own methods (vtable slots 8-11) ───────────────────────
    [PreserveSig] int GetSharedHandle(out IntPtr pSharedHandle);
    [PreserveSig] int GetUsage(out int pUsage);
    [PreserveSig] int SetEvictionPriority(uint evictionPriority);
    [PreserveSig] int GetEvictionPriority(out uint pEvictionPriority);
}
