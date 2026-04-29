# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

**Per-frame diagnostic instrumentation added — debugging work only, no feature behavior changed.**

The previous blur-pipeline diagnostics were mostly one-shot, which could only prove that the first captured frame reached the app. This session replaced the first-call gates with bounded per-frame/call counters so the next smoke run can show whether frames 2 through N are lost in capture dispatch, blur processing, overlay presentation, or the D3DImage bridge.

### Added
- **`TrayOrchestrator.OnFrameArrived`** now logs `FrameArrived #N: dims={w}x{h}` for frames 1-10 and every 30th frame after that. The handler now swallows and logs every exception with frame number, exception count, type/message, and stack trace so one bad frame cannot stop diagnosis.
- **`OverlayWindow.PresentFrame`** now logs calls 1-5 and every 30th call, including source dimensions plus `D3DImage.IsFrontBufferAvailable`, `PixelWidth`, and `PixelHeight` before the bridge update.
- **`OverlayWindow.Show`** now writes a one-time visual-tree summary confirming the content host and whether the `Image` is sourced from the `D3DImage`.
- **`D3DImageBridge.UpdateD3DImage`** now logs every bridge step for calls 1-5 with `[bridge call #N]` prefixes, including shared handle and HRESULT values, while still logging full exception details on any failure.

**Next session:** run the smoke test, keep WhatsApp captured for at least 40 seconds, then read the resulting `washed-startup.log` to identify where frames stop.

---

**WhatsApp detection unified — real diagnostic data showed the Store version is now a WinUI 3 app, not UWP.**

The previous two-strategy detection (Strategy A: process name loop; Strategy B: `ApplicationFrameWindow` class) was based on wrong assumptions. Real diagnostic data (`Get-Process` + `GetClassName` P/Invoke on the user's machine) shows the Microsoft Store WhatsApp is:

- **Process:** `WhatsApp.Root`
- **Window class:** `WinUIDesktopWin32WindowClass`
- **Title:** `WhatsApp`
- There is also a child `msedgewebview2` process (`Chrome_WidgetWin_1` class) that renders the chat UI via WebView2 — this must NOT be captured.

The `ApplicationFrameWindow` / `ApplicationFrameHost` strategy never matched because there is no ApplicationFrameHost involvement in a WinUI 3 app.

**Detection is now a single unified strategy** implemented by the predicate `Win32Api.IsWhatsAppWindow(processName, className, title)`:

1. Reject if `title` is empty.
2. Reject if `processName` contains `"msedgewebview"` (case-insensitive) — excludes WebView2 child.
3. Match if `processName` starts with `"WhatsApp"` (case-insensitive) — covers `WhatsApp.Root`, `WhatsApp`, `WhatsAppDesktop`, etc.
4. Match if `className == "WinUIDesktopWin32WindowClass"` AND `title` contains `"WhatsApp"` (case-insensitive) — belt-and-suspenders for future Store versions.

### Changed
- **`IWin32Api`**: Replaced `FindStoreWhatsAppWindowHandle()` with `FindWhatsAppWindowHandle()`.
- **`Win32Api`**: Removed `FindStoreWhatsAppWindowHandle()`. Added `internal static IsWhatsAppWindow(processName, className, title)` (the single authoritative predicate, accessible to `WAshed.App` via existing `InternalsVisibleTo`). Added `FindWhatsAppWindowHandle()` using that predicate.
- **`WindowTracker`**: `SampleBoundsWithHandle` now calls `_win32.FindWhatsAppWindowHandle()` only — no more process-name loop or two-strategy fallback. Removed `WhatsAppProcessNames` array.
- **`CaptureItemFactory`**: Replaced `FindWin32WhatsAppWindow`/`FindStoreWhatsAppWindow` and Strategy A/B with single `FindWhatsAppWindow()`. Removed `IsWin32WhatsAppProcess`/`IsStoreWhatsAppWindow`; added `IsWhatsAppWindow` wrapper to `Win32Api.IsWhatsAppWindow`. Added per-candidate `DiagLog` tracing (up to 20 candidates, with `match` + `reason` per entry).
- **`CaptureItemFactoryTests`**: Replaced `IsWin32WhatsAppProcess` + `IsStoreWhatsAppWindow` predicate tests with 16 `IsWhatsAppWindow` tests covering all match/reject cases. Integration test `TryCreateForWhatsApp_WhenWhatsAppNotRunning` now skips gracefully when WhatsApp IS running (detection success correctly returns true in that case).
- **`WindowTrackerTests`**: Updated mock setup from `GetWindowHandlesForProcessName` to `FindWhatsAppWindowHandle()`.

**57 tests green (35 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

Added step-by-step `StartupLog`/`DiagLog` tracing to every layer of the blur path so a top-to-bottom read of `washed-startup.log` reveals exactly where things go silent when "Enable blur" is clicked with no visible result.

### Added
- **`src/WAshed.Core/Diagnostics/DiagLog.cs`** — new internal static logger (same washed-startup.log file, same ISO-8601 timestamp format as `StartupLog`). Used by WAshed.Core and WAshed.Overlay, which cannot reference WAshed.App. `InternalsVisibleTo(WAshed.Overlay)` added to WAshed.Core.csproj.
- **`TrayOrchestrator`** — `EnableBlur` now logs entry, WindowTracker start/started, TryCreateForWhatsApp call and result, abort if WhatsApp not found, CaptureEngine start, OverlayWindow show, SetBounds call, complete. `DisableBlur` logs entry/complete. `OnFrameArrived` logs first-frame dimensions (Interlocked flag), a per-60-frame heartbeat, and any exception (logged then re-thrown).
- **`CaptureEngine.Start`** — logs entry, `GraphicsCaptureSession.IsSupported()` result, frame-pool creation, FrameArrived subscription, session start. First WGC frame arrival logged once via Interlocked flag.
- **`WindowTracker`** — logs first WhatsApp window detected (HWND + bounds), first bounds change, and a one-time warning if WhatsApp is not found within 5 seconds of the poll loop starting. Refactored `SampleBounds` → `SampleBoundsWithHandle` to return the HWND alongside the rect for logging.
- **`OverlayWindow.Show`** — logs entry (current Visibility), and HWND + IsVisible after `_window.Show()`. **`SetBounds`** logs all four coordinates. **`PresentFrame`** logs first-call source dimensions (Interlocked flag); exceptions are caught, logged via DiagLog, and swallowed so one bad frame cannot crash the app.
- **`D3DImageBridge.UpdateD3DImage`** — first-call only (Interlocked flag) logs every step of the WinRT→DXGI→D3D9Ex bridge: surface acquisition, QI for IDirect3DDxgiInterfaceAccess (with HR), GetInterface for ID3D11Texture2D (HR), QI for IDXGIResource (HR), GetSharedHandle result (HR + handle hex value). **CRITICAL log**: if shared handle is zero logs "ABORT — render target was not created with DXGI_RESOURCE_MISC_SHARED". All exceptions caught, logged, swallowed (no re-throw).

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

---

**Previous session — Library swap** — H.NotifyIcon.Wpf replaced with Hardcodet.NotifyIcon.Wpf 2.0.1.

Diagnostic evidence from the previous session's `washed-startup.log` proved conclusively that our code was correct: the .ico file existed on disk, `BitmapImage` loaded with real 32×32 dimensions, `TaskbarIcon` was constructed, `IconSource` was assigned, `Visibility` was forced to `Visible`, and the `System.Drawing.Icon` fallback also succeeded — yet no icon ever appeared in the system tray. The bug is inside H.NotifyIcon or its interaction with the Windows shell notification area on at least one tested machine. Swapped to Hardcodet.NotifyIcon.Wpf (the original project that H.NotifyIcon forked from), which is more mature and widely deployed.

### Changed
- `WAshed.App.csproj`: Replaced `H.NotifyIcon.Wpf 2.1.0` with `Hardcodet.NotifyIcon.Wpf 2.0.1`.
- `TrayIconHost.cs`: Updated `using H.NotifyIcon;` → `using Hardcodet.Wpf.TaskbarNotification;`. All other code is unchanged — the `TaskbarIcon` API (`IconSource`, `Icon`, `ToolTipText`, `ContextMenu`, `Visibility`, `Dispose`) is identical between the two libraries.
- All diagnostic logging and the `System.Drawing.Icon` fallback retained unchanged.

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings (1 platform warning only when building project directly, not via sln).**

---

**Previous session — Debugging session** — tray icon still not visible during v0.1.0 smoke testing despite prior fixes (icon file present on disk, no crash log written, process alive at ~164 MB). Added step-by-step diagnostic instrumentation to pinpoint exactly where initialization silently stalls.

### Added — Startup diagnostic log (`washed-startup.log`)
- New `src/WAshed.App/Diagnostics/StartupLog.cs`: `Info`/`Warn` methods, ISO 8601 timestamps with milliseconds, append-mode with flush-per-write, truncated on each app start so the file always reflects the most recent run.
- `App.xaml.cs` `OnStartup` now logs "OnStartup begin / TrayOrchestrator constructed / TrayIconHost constructed / Initialize() returned / OnStartup complete" with try/catch around each step; exceptions are logged then re-thrown so the existing crash-log behaviour is preserved.
- `TrayIconHost.Initialize` logs every step: resolved icon path, `File.Exists`, `BitmapImage` (IsFrozen, PixelWidth, PixelHeight), `TaskbarIcon` creation, `IconSource` assignment, `Visibility` before and after forcing it to `Visible`.

### Added — `System.Drawing.Icon` native fallback in `TrayIconHost.Initialize`
After the `BitmapImage`/`IconSource` path, also sets `taskbarIcon.Icon = new System.Drawing.Icon(iconPath)`. This is the legacy HICON path that bypasses WPF imaging entirely; H.NotifyIcon accepts either. If the WPF path silently fails, the native path may succeed. Success/failure of this fallback is both logged and non-fatal (wrapped in try/catch). Added `System.Drawing.Common 8.0.7` to `WAshed.App.csproj` (version pinned to match the transitive version required by `H.GeneratedIcons.System.Drawing 2.1.0`).

### Added — Force `Visibility.Visible` on `TaskbarIcon`
Explicitly sets `_taskbarIcon.Visibility = Visibility.Visible` after construction. H.NotifyIcon's default `Visibility` can vary by version; this ensures the icon is always shown regardless of the default.

**Next session:** run the app, read `washed-startup.log`, identify exactly which step fails or confirm the icon now appears. If the icon appears, strip diagnostics and remove the `StartupLog` calls.

### Fix — Tray icon never appeared (silent failure)
**Root cause:** `Assets\tray-icon.ico` was declared as `<Resource>` in `WAshed.App.csproj`. The file IS embedded into the WPF `.g.resources` bundle (confirmed by enumerating the bundle — key `assets/tray-icon.ico` was present). However, H.NotifyIcon cannot convert a `BitmapImage` loaded from a pack URI to a native `HICON`; its internal conversion path requires a real file path or a `System.Drawing.Icon`. The conversion throws silently on the WPF dispatcher, the app continues with no icon, and there is no console output.

**Fix — csproj:** Replaced `<Resource Include="Assets\tray-icon.ico" />` with a `<Content>` directive that copies the file next to the exe at build time:
```xml
<Content Include="Assets\tray-icon.ico">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```
Verified: `bin/x64/Release/net8.0-windows10.0.19041.0/Assets/tray-icon.ico` now exists on disk after a clean rebuild.

**Fix — TrayIconHost.cs:** Replaced the pack-URI `BitmapImage` with file-path loading:
```csharp
var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray-icon.ico");
// throws FileNotFoundException with a descriptive message if missing
var iconImage = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
```
If the file is missing, a loud `FileNotFoundException` is thrown (not swallowed) so build misconfiguration is immediately obvious.

### Added — Global exception logger
Hooked `AppDomain.CurrentDomain.UnhandledException` and `Application.DispatcherUnhandledException` in `App.xaml.cs`. All unhandled exceptions (including full `InnerException` chain and stack trace) are written to `washed-crash.log` in `AppContext.BaseDirectory` AND to Debug output. For dispatcher exceptions, `e.Handled = true` is set so a bad icon load cannot take down the whole app, but the failure IS recorded.

**Known fragility (do not revert):** Using `<Resource>` for .ico files in H.NotifyIcon projects is unreliable — H.NotifyIcon silently fails to create the native icon from a pack-URI BitmapImage. Always use `<Content CopyToOutputDirectory>` + file-path loading for tray icon assets.

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

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

**Run the smoke test and read the resulting diagnostic log.** Keep WhatsApp captured for at least 40 seconds, then inspect `washed-startup.log` for `FrameArrived #N`, `PresentFrame #N`, `[bridge call #N]`, and `OverlayWindow.Show: visual tree = ...` lines to determine where frames 2 through N are lost.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **WhatsApp detection strategy** = match by process name prefix `"WhatsApp"` (case-insensitive) OR window class `"WinUIDesktopWin32WindowClass"` + title contains `"WhatsApp"`, explicitly excluding `msedgewebview2`. Real diagnostic data showed the Store version is now a WinUI 3 app (`WhatsApp.Root` process) with a WebView2 child, not a classic UWP app. The `ApplicationFrameWindow` strategy is dead and removed.
- **Tray library = Hardcodet.NotifyIcon.Wpf** (not H.NotifyIcon.Wpf). Rationale: H.NotifyIcon silently failed to register the icon with the Windows shell notification area on at least one tested machine despite all setup steps succeeding (file on disk, BitmapImage loaded, Icon property set, Visibility forced to Visible — all logged clean). Hardcodet's library is the more mature parent project (H.NotifyIcon is a fork of it) and is known to work reliably across Windows versions.
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
