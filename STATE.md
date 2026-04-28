# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Built the `TrayApp` orchestration in `src/WAshed.App/`:

- `WAshed.App.csproj` upgraded to `net8.0-windows10.0.19041.0` with `<UseWPF>`,
  `<WindowsPackageType>None</WindowsPackageType>`, `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>`,
  and `<ApplicationManifest>app.manifest</ApplicationManifest>`. Added `H.NotifyIcon.Wpf 2.1.0`
  and `ProjectReference`s to `WAshed.Core` and `WAshed.Overlay`.
- `app.manifest` — Per-Monitor V2 DPI awareness (`PerMonitorV2`), required for
  `OverlayWindow.SetBounds` to align correctly with `WindowTracker` bounds on high-DPI monitors.
- `App.xaml` — removed `StartupUri`; `ShutdownMode` set to `OnExplicitShutdown` in `OnStartup`.
  `MainWindow.xaml` / `MainWindow.xaml.cs` deleted.
- `ITrayOrchestrator` / `TrayOrchestrator` — wires `IWindowTracker`, `ICaptureEngine`,
  `IBlurPipeline`, `IOverlayWindow`, `IHotkeyService`, `ICaptureItemFactory`; public surface:
  `ToggleBlur()`, `EnableBlur()`, `DisableBlur()`, `IsBlurEnabled`, `BlurStateChanged`, `Dispose()`;
  tracks blur state and capture-started state; on `BoundsChanged` propagates `SetBounds` and
  lazily starts capture if WhatsApp appears after blur is enabled.
- `ICaptureItemFactory` / `CaptureItemFactory` — `bool TryCreateForWhatsApp(out GraphicsCaptureItem? item)`;
  concrete implementation finds the WhatsApp HWND via `IWin32Api`; the
  `IGraphicsCaptureItemInterop` activation-factory path is stubbed out with a
  TODO because `WindowsRuntimeMarshal.GetActivationFactory` was removed in .NET 6+
  (fix in next session alongside the Win2D render target).
- `TrayIconHost` — wraps `H.NotifyIcon.Wpf TaskbarIcon`; context menu: "Enable blur"
  (checkable, synced via `BlurStateChanged`), separator, "Exit"; placeholder 16×16 teal icon
  rendered via `RenderTargetBitmap`; TODO for real icon.
- `HotkeyService` / `IHotkeyService` — registers Ctrl+Shift+B via `RegisterHotKey` P/Invoke;
  uses an `HwndSource` HWND_MESSAGE window to receive `WM_HOTKEY`; fires on WPF UI thread.
- `App.xaml.cs` — composition root; `NullCaptureEngine` and `NullBlurPipeline` null-objects
  hold the place of the not-yet-built Win2D pipeline until next session.
- `tests/WAshed.App.Tests/` — new xUnit test project; 15 tests covering `TrayOrchestrator`:
  enable order, disable order, hotkey toggle (both directions), bounds-changed propagation,
  lazy capture start when WhatsApp appears after blur is enabled, double-enable no-op, dispose
  idempotent, dispose stops everything, dispose releases pipeline and overlay.
- All 34 tests green (19 pre-existing + 15 new); build 0 errors.

## Next up

**Build the concrete Win2D `IBlurRenderTarget` wrapper** in `WAshed.Core/Blur` with
`DXGI_RESOURCE_MISC_SHARED` so the `OverlayWindow` bridge can produce a real shared texture.
Also implement `CaptureItemFactory.TryCreateForWhatsApp` via P/Invoke to `RoGetActivationFactory`
(replaces the stubbed TODO). Together these two unblock the first end-to-end smoke test.

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
- `CaptureItemFactory.TryCreateForWhatsApp` is stubbed (returns false) because
  `WindowsRuntimeMarshal.GetActivationFactory` was removed in .NET 6+; the real
  `IGraphicsCaptureItemInterop` path via `RoGetActivationFactory` P/Invoke is next session.
- `NullCaptureEngine` and `NullBlurPipeline` null-objects live in `App.xaml.cs`
  (composition root) until the real Win2D pipeline is built.
