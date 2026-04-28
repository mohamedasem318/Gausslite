# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Fixed two crashes/warnings found during the v0.1.0 smoke test on real Windows hardware.

### Fix 1 — Tray icon crash (H.NotifyIcon + RenderTargetBitmap)
- `TrayIconHost` previously generated a placeholder icon via `RenderTargetBitmap`/`DrawingVisual`, which `H.NotifyIcon` does not support — caused `NotImplementedException` on launch.
- Added `Assets\tray-icon.ico` as a `<Resource>` in `WAshed.App.csproj`.
- Replaced the `CreatePlaceholderIcon` method with a `BitmapImage` loaded from the pack URI `pack://application:,,,/Assets/tray-icon.ico`.

### Fix 2 — Win2D AnyCPU build warning
- Added `<Platforms>x64</Platforms>` and `<PlatformTarget>x64</PlatformTarget>` to all five .csproj files (WAshed.Core, WAshed.Overlay, WAshed.App, WAshed.Core.Tests, WAshed.App.Tests).
- Updated `.sln` project config mappings to route the `Any CPU` solution platform to `x64` per-project configs; solution-level platform name kept as `Any CPU` so `dotnet build` works without `--arch x64`.
- Build now produces zero warnings; `Microsoft.Graphics.Canvas.dll` lands under `runtimes/win-x64/native/` in the app output.

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

---

Previous session: Shipped the two remaining pieces blocking the first end-to-end smoke test:

### Part 1 — CaptureItemFactory (real WinRT activation)
- Replaced the stub in `CaptureItemFactory.TryCreateForWhatsApp` with a full
  `IGraphicsCaptureItemInterop` / `RoGetActivationFactory` P/Invoke path
  (combase.dll: `RoGetActivationFactory`, `WindowsCreateString`, `WindowsDeleteString`).
- `IGraphicsCaptureItemInterop` (IID `3628E81B-…`) declared as an internal
  `[ComImport]` interface; `CreateForWindow` marshals the ABI pointer to a managed
  `GraphicsCaptureItem` via `MarshalInterface<GraphicsCaptureItem>.FromAbi`.
- Failure handling: `GraphicsCaptureSession.IsSupported()` guard, non-zero HRESULT
  logged in hex and returns false (no throw), WhatsApp not found returns false.
- Added `InternalsVisibleTo(WAshed.App.Tests)` to `WAshed.App.csproj`.
- Integration test: `TryCreateForWhatsApp_WhenWhatsAppNotRunning_ReturnsFalseAndNull`
  and `_CalledTwice_NeverThrows` in `tests/WAshed.App.Tests/Orchestration/`.

### Part 2 — Win2DBlurRenderTarget (concrete GPU render target with shared texture)
- **`Win2DBlurRenderTarget`** (internal sealed, `WAshed.Core.Blur`):
  implements `IBlurRenderTarget` + `INativeBlurRenderTarget`.  
  Key design: since `CanvasRenderTarget` constructors don't expose
  `D3D11_RESOURCE_MISC_SHARED`, the constructor walks the chain:
  `IDirect3DDevice` (WinRT) → `IDirect3DDxgiInterfaceAccess.GetInterface(IID_ID3D11Device)`
  → `ID3D11Device.CreateTexture2D(DXGI_FORMAT_B8G8R8A8_UNORM, MISC_SHARED)` →
  QI for `IDXGISurface` → `CreateDirect3D11SurfaceFromDXGISurface` →
  `CanvasRenderTarget.CreateFromDirect3D11Surface`.  
  The resulting `IDirect3DSurface` is cached; `GetDirect3DSurface()` returns the
  same instance on every call.
- **`Win2DCanvasDeviceWrapper`** (internal): thin `IBlurCanvasDevice` wrapper that
  also carries the original `IDirect3DDevice` for shared-texture creation.
- **`Win2DBlurInterop`** (public): concrete `IBlurInterop`; uses
  `CanvasDevice.CreateFromDirect3D11Device` to share the same GPU device as
  `CaptureEngine`; draws frames via `GaussianBlurEffect` in a `CanvasDrawingSession`.
- Added `InternalsVisibleTo(WAshed.Core.Tests)` to `WAshed.Core.csproj`.
- GPU-gated tests in `tests/WAshed.Core.Tests/Blur/Win2DBlurRenderTargetTests.cs`
  (skip when `GraphicsCaptureSession.IsSupported()` is false; CI has no GPU).

### Part 3 — WinRT capture concrete wrappers
- Created `WinRTCaptureFrame`, `WinRTCaptureSession`, `WinRTCaptureFramePool`,
  `WinRTCaptureInterop` in `WAshed.Core.Capture` so the real `CaptureEngine`
  can be wired up without the GPU-level stubs.

### Part 4 — Composition root
- `App.xaml.cs`: replaced `NullCaptureEngine` / `NullBlurPipeline` with
  real `CaptureEngine` + `BlurPipeline`.  Shared D3D11 device created via
  `D3D11CreateDevice` → QI for `IDXGIDevice` → `WinRTCaptureInterop.CreateDirect3DDevice`.
  `BlurPipeline.Initialize(d3dDevice)` called immediately so Win2D's CanvasDevice
  is ready before any frame arrives.  `NullCaptureEngine` and `NullBlurPipeline`
  retained as `internal` test fakes.

**All 39 tests green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors.**

## Next up

**End-to-end smoke test on real hardware:**
Launch `WAshed.App`, open WhatsApp Desktop, press Ctrl+Shift+B, verify blur appears
over the WhatsApp window. File any issues found as the v0.2.0 backlog
("The right regions").

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- Solution pinned to x64 (Win2D requires a concrete platform; ARM64 support deferred to post-v1).
- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension
- `SharpDX.Direct3D9` 4.2.0 chosen for D3D9Ex managed wrappers in WAshed.Overlay;
  targets netstandard2.0 so it compiles cleanly on net8.0-windows.
- `INativeBlurRenderTarget` placed in `WAshed.Core.Blur` (not Overlay) to avoid circular
  project reference; concrete Win2D implementations in Core implement both interfaces.
- **Win2D shared-texture path (important for future maintenance):**  
  `CanvasRenderTarget(CanvasDevice, w, h)` does NOT set `D3D11_RESOURCE_MISC_SHARED`,
  so `IDXGIResource.GetSharedHandle` would always fail in `D3DImageBridge`.  
  Fix: create the D3D11 texture manually with `D3D11_RESOURCE_MISC_SHARED`, wrap it
  as `IDirect3DSurface` via `CreateDirect3D11SurfaceFromDXGISurface` (d3d11.dll),
  then use `CanvasRenderTarget.CreateFromDirect3D11Surface`. This is the path taken in
  `Win2DBlurRenderTarget`.
- `CanvasDevice.CreateFromDirect3D11Device` (not `GetOrCreate`) is the correct API in
  Microsoft.Graphics.Win2D 1.3.0 (WinAppSDK flavour).
- `Win2DBlurInterop.DrawBlur` wraps the capture frame surface as a `CanvasBitmap` via
  `CanvasBitmap.CreateFromDirect3D11Surface`, then draws through `GaussianBlurEffect`.
  No pixel copy — Win2D references the frame texture in-place.
