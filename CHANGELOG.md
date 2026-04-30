# Changelog

All notable changes to Gausslite will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- Updated tray icon to match new Gausslite branding.

## [0.1.1] - 2026-04-30

### Changed
- Project renamed from WAshed to Gausslite. Solution file, project
  files, namespaces, folders, assembly names, executable name, and
  log file names all updated. No functional changes.
- Documentation restructured: PLAN.md roadmap rewritten (v0.3.0
  redefined as share-target awareness, v0.5.0 added for toast
  notification blur, v0.2.0 expanded, multi-app deferred to post-v1.0);
  README rewritten with Gausslite framing; STATE.md split — compact
  forward-looking state stays in STATE.md, verbose session archive
  moved to new HISTORY.md.

## [0.1.0] - 2026-04-29

### Added
- Project scaffolding and planning documents.
- WindowTracker module: tracks WhatsApp Desktop window bounds at 10 Hz with per-monitor DPI awareness.
- CaptureEngine module: wraps Windows.Graphics.Capture to deliver per-frame textures via a free-threaded FrameArrived event; all WinRT factory calls are behind ICaptureInterop for unit-test isolation.
- BlurPipeline module: applies configurable Gaussian blur to captured frames using Win2D, with a default radius of 20 DIPs; render target is reused across frames and reallocated only when frame dimensions change.
- OverlayWindow module: transparent, always-on-top, click-through WPF window that renders blurred output via D3DImage; uses the D3D9Ex shared-surface bridge (IDirect3DDxgiInterfaceAccess → IDXGIResource shared handle → D3D9Ex texture) to GPU-accelerate display without a CPU readback; click-through and hidden from taskbar/Alt-Tab at the Win32 level (WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW).
- TrayApp: system-tray icon with "Enable blur" toggle and global hotkey (Ctrl+Shift+B); blur state is persisted across hotkey and menu interactions; overlay and capture pipeline start/stop automatically when the toggle changes.
- Capture pipeline now activates against the real WhatsApp Desktop window via Windows Graphics Capture: `CaptureItemFactory` resolves the HWND and creates a `GraphicsCaptureItem` through `IGraphicsCaptureItemInterop` / `RoGetActivationFactory` P/Invoke (replaces the earlier stub that always returned false).
- BlurPipeline now produces a real GPU-shared blurred frame consumable by the overlay window: `Win2DBlurRenderTarget` wraps a D3D11 texture created with `DXGI_RESOURCE_MISC_SHARED`, enabling the D3D9Ex shared-surface bridge in `D3DImageBridge` to present frames without a CPU copy.
- Diagnostic logging across the full blur pipeline (capture, frame processing, overlay rendering, shared-handle bridge) written to `washed-startup.log`.
- Per-frame diagnostic logging in the blur pipeline to diagnose why frames stop after the first arrives.
- Startup diagnostic log written to `washed-startup.log` to trace tray icon initialization: every step of `TrayIconHost.Initialize` and `App.OnStartup` is timestamped so the exact failure point is visible on the next run.
- Global exception logger writes uncaught errors to `washed-crash.log` next to the executable, so silent failures are no longer silent. Hooks both `AppDomain.UnhandledException` (fatal) and `DispatcherUnhandledException` (non-fatal, sets `Handled = true` to keep the app running).
- Explicit blur activation state handling for idle, armed, and active states, with tests for delayed activation and WhatsApp close/reopen flows.
- WhatsApp detection updated to match the current Microsoft Store WinUI 3 build (`WhatsApp.Root` process). Win32 builds still detected; WebView2 child windows (`msedgewebview2`) correctly excluded.

### Changed
- Build now targets x64 explicitly (was AnyCPU); Win2D's native dependencies now deploy correctly (`Microsoft.Graphics.Canvas.dll` at `runtimes/win-x64/native/`).
- Tray icon library swapped from H.NotifyIcon.Wpf to Hardcodet.NotifyIcon.Wpf for reliability.
- WindowTracker bounds-change diagnostics now log the first change and then every tenth change, so window dragging remains visible in logs without flooding `washed-startup.log`.

### Fixed
- TrayApp now loads a real .ico file for the system tray icon, fixing a launch crash on Windows (`H.NotifyIcon` does not support `RenderTargetBitmap` as an `IconSource`).
- Tray icon now loads reliably from disk instead of an embedded resource that wasn't being packaged correctly — H.NotifyIcon silently failed to convert a pack-URI `BitmapImage` to a native `HICON`, leaving the app running with no visible icon.
- OverlayWindow Image element now correctly stretches to fill the window; previously the D3DImage was feeding pixels into a 0x0 element, so the blurred frame was rendered but invisible.
- OverlayWindow now correctly sizes itself to the tracked window's bounds; previously it was rendering blur into a 14x14 area in the top-left corner.
- Overlay now hides automatically when WhatsApp is minimized and reappears when restored. Previously a stale blurred frame would float over the desktop until blur was manually toggled off.
- Title bar drag and other window-frame operations on WhatsApp now work correctly while blur is on; the overlay was intercepting `WM_NCHITTEST` and reporting itself as a non-transparent client surface, which prevented Windows from initiating drag gestures.
- WindowTracker now reports DPI-correct WPF bounds on high-DPI displays; previously it double-scaled `GetWindowRect` coordinates at 125%/150%/175% scaling.
- Overlay updates from window tracking are now marshalled to the WPF UI thread before moving, hiding, or showing the overlay, preventing cross-thread WPF access during bounds and minimize/restore events.
- WindowTracker now polls at ~30 Hz for smoother overlay tracking during fast title-bar drags.
- Enabling blur while WhatsApp is minimized or not running now arms blur silently and waits for a visible WhatsApp window instead of showing stale overlay content.
- Overlay now follows WhatsApp correctly when the window is maximized, including monitor work-area clipping and per-monitor DPI conversion.
- Restoring WhatsApp from minimize now shows the overlay immediately with the last blurred frame while Windows Graphics Capture waits for WhatsApp to repaint.
- Overlay bounds are now applied through Win32 `SetWindowPos` as well as WPF sizing, so the blur overlay can follow maximized and screen-edge WhatsApp windows instead of being clamped to its previous normal bounds.
- Enabling blur while WhatsApp is minimized or not running now shows an opaque placeholder over WhatsApp as soon as it becomes visible, hiding raw content until the first blurred frame is ready.
- Maximizing WhatsApp while blur is already active now recreates the Windows Graphics Capture frame pool on content-size changes so blurred frames fill the resized overlay instead of staying at the pre-maximize size.
- Restore and armed activation paths now show an opaque placeholder whenever no fresh blurred frame is available, preventing readable WhatsApp content during capture startup.
- Overlay now hides when WhatsApp is behind another foreground window, so blurred WhatsApp pixels no longer float over unrelated apps.
- Restoring WhatsApp while blur is armed now starts capture from the restore event itself, so the opaque placeholder covers WhatsApp within one tracker poll instead of waiting for a later bounds retry.
- Restoring or unoccluding WhatsApp while blur is armed now prioritizes the privacy-critical overlay update on the WPF dispatcher and logs dispatch queue latency, preventing readable content during restore.
- Armed-state restore now pre-creates capture and an offscreen parked overlay when WhatsApp's HWND is first seen, so restore/unocclude only moves the overlay instead of exposing WhatsApp during setup work.
- First restore from minimize in a blur session now moves the pre-created overlay on-screen instead of flipping WPF visibility, avoiding the first-show layout cost.