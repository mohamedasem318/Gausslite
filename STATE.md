# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Built the `OverlayWindow` module in `src/WAshed.Overlay/`:

- `WAshed.Overlay.csproj` upgraded from bare `net8.0` to `net8.0-windows10.0.19041.0` with
  `<UseWPF>`, `<AllowUnsafeBlocks>`, `<WindowsPackageType>None</WindowsPackageType>`, and
  `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>` (matches the pattern established for
  WAshed.Core). Added `SharpDX` + `SharpDX.Direct3D9` 4.2.0 for D3D9Ex managed wrappers.
  Added `ProjectReference` to `WAshed.Core`.
- `INativeBlurRenderTarget` (new, `WAshed.Core.Blur`) — secondary interface that concrete
  Win2D `IBlurRenderTarget` implementations will also implement; exposes
  `IDirect3DSurface GetDirect3DSurface()` so the bridge can extract the underlying D3D11
  texture. Must live in Core (not Overlay) to avoid a circular project reference.
- `IOverlayWindow` — public interface: `Show()`, `Hide()`, `SetBounds(Rect)`,
  `PresentFrame(IBlurRenderTarget)`, `Dispose()`.
- `ID3DImageBridge` (internal) — seam isolating D3D9/DXGI concerns from WPF window logic.
  `UpdateD3DImage(D3DImage, IBlurRenderTarget)` is the single method.
- `ComInterop.cs` — `[ComImport]` definitions for `IDirect3DDxgiInterfaceAccess` (bridges
  WinRT `IDirect3DSurface` to native DXGI) and `IDXGIResource` (vtable-correct, including
  inherited IDXGIObject + IDXGIDeviceSubObject stubs). Both are internal.
- `NativeWindow.cs` — `GetWindowLong`/`SetWindowLong` P/Invoke plus the three extended-style
  constants (`WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`).
- `D3DImageBridge` — creates a D3D9Ex device once (1×1 windowed, desktop HWND); on each
  `UpdateD3DImage` call: casts `IBlurRenderTarget` to `INativeBlurRenderTarget`, walks the
  WinRT→DXGI chain (`IWinRTObject.NativeObject.ThisPtr` → `IDirect3DDxgiInterfaceAccess` →
  `ID3D11Texture2D` → `IDXGIResource.GetSharedHandle`) to obtain the D3D11 shared handle,
  imports it as a `SharpDX.Direct3D9.Texture`, and sets `IDirect3DSurface9` as the
  `D3DImage` back buffer. Degrades gracefully (no-op) if the render target doesn't implement
  `INativeBlurRenderTarget` or the texture lacks `DXGI_RESOURCE_MISC_SHARED`.
- `OverlayWindow` — `WindowStyle=None`, `AllowsTransparency=true`, `Background=Transparent`,
  `Topmost=true`, `ShowInTaskbar=false`. Applies `WS_EX_TRANSPARENT | WS_EX_LAYERED |
  WS_EX_TOOLWINDOW` in the `SourceInitialized` handler (once the HWND exists). `PresentFrame`
  dispatches to the WPF UI thread via `Dispatcher.Invoke`. Public constructor takes no args;
  internal constructor accepts `ID3DImageBridge` for injection.
- All 19 existing xUnit tests still green; solution builds with 0 errors.

## Next up

**Build the `TrayApp` orchestration** in `WAshed.App` per the v0.1.0 milestone.

Responsibilities:
- `App.xaml.cs`: suppress the default main window, create a system-tray icon
- Wire `WindowTracker`, `CaptureEngine`, `BlurPipeline`, `OverlayWindow` together
- Tray menu: "Enable blur" toggle, hotkey (to be decided), "Exit"
- Per-monitor V2 DPI awareness manifest entry (required for `OverlayWindow.SetBounds` to
  align correctly with `WindowTracker` bounds)

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension
- `SharpDX.Direct3D9` 4.2.0 chosen for D3D9Ex managed wrappers in WAshed.Overlay;
  targets netstandard2.0 so it compiles cleanly on net8.0-windows.
- `INativeBlurRenderTarget` placed in `WAshed.Core.Blur` (not Overlay) to avoid circular
  project reference; concrete Win2D implementations in Core implement both interfaces.
- The concrete Win2D `IBlurRenderTarget` wrapper has not been written yet (GPU/smoke-test
  code); its `CanvasRenderTarget` must be created with `DXGI_RESOURCE_MISC_SHARED` for the
  shared-handle bridge to succeed at runtime.
