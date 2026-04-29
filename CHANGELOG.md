# Changelog

All notable changes to WAshed will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- OverlayWindow now correctly sizes itself to the tracked window's bounds; previously it was rendering blur into a 14x14 area in the top-left corner.
- OverlayWindow Image element now correctly stretches to fill the window; previously the D3DImage was feeding pixels into a 0x0 element, so the blurred frame was rendered but invisible.
- Tray icon now loads reliably from disk instead of an embedded resource that wasn't being packaged correctly — H.NotifyIcon silently failed to convert a pack-URI `BitmapImage` to a native `HICON`, leaving the app running with no visible icon.
- TrayApp now loads a real .ico file for the system tray icon, fixing a launch crash on Windows (`H.NotifyIcon` does not support `RenderTargetBitmap` as an `IconSource`).

### Added
- Per-frame diagnostic logging in the blur pipeline to diagnose why frames stop after the first arrives.
- WhatsApp detection updated to match the current Microsoft Store WinUI 3 build (`WhatsApp.Root` process). Win32 builds still detected; WebView2 child windows (`msedgewebview2`) correctly excluded.
- Startup diagnostic log written to `washed-startup.log` to trace tray icon initialization: every step of `TrayIconHost.Initialize` and `App.OnStartup` is timestamped so the exact failure point is visible on the next run.
- Global exception logger writes uncaught errors to `washed-crash.log` next to the executable, so silent failures are no longer silent. Hooks both `AppDomain.UnhandledException` (fatal) and `DispatcherUnhandledException` (non-fatal, sets `Handled = true` to keep the app running).

### Changed
- Build now targets x64 explicitly (was AnyCPU); Win2D's native dependencies now deploy correctly (`Microsoft.Graphics.Canvas.dll` at `runtimes/win-x64/native/`).
- Tray icon library swapped from H.NotifyIcon.Wpf to Hardcodet.NotifyIcon.Wpf for reliability.

### Added
- Diagnostic logging across the full blur pipeline (capture, frame processing, overlay rendering, shared-handle bridge) written to `washed-startup.log`.
- Capture pipeline now activates against the real WhatsApp Desktop window via Windows Graphics Capture: `CaptureItemFactory` resolves the HWND and creates a `GraphicsCaptureItem` through `IGraphicsCaptureItemInterop` / `RoGetActivationFactory` P/Invoke (replaces the earlier stub that always returned false)
- BlurPipeline now produces a real GPU-shared blurred frame consumable by the overlay window: `Win2DBlurRenderTarget` wraps a D3D11 texture created with `DXGI_RESOURCE_MISC_SHARED`, enabling the D3D9Ex shared-surface bridge in `D3DImageBridge` to present frames without a CPU copy
- TrayApp: system-tray icon with "Enable blur" toggle and global hotkey (Ctrl+Shift+B); blur state is persisted across hotkey and menu interactions; overlay and capture pipeline start/stop automatically when the toggle changes
- OverlayWindow module: transparent, always-on-top, click-through WPF window that renders blurred output via D3DImage; uses the D3D9Ex shared-surface bridge (IDirect3DDxgiInterfaceAccess → IDXGIResource shared handle → D3D9Ex texture) to GPU-accelerate display without a CPU readback; click-through and hidden from taskbar/Alt-Tab at the Win32 level (WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW)
- BlurPipeline module: applies configurable Gaussian blur to captured frames using Win2D, with a default radius of 20 DIPs; render target is reused across frames and reallocated only when frame dimensions change
- CaptureEngine module: wraps Windows.Graphics.Capture to deliver per-frame textures via a free-threaded FrameArrived event; all WinRT factory calls are behind ICaptureInterop for unit-test isolation
- WindowTracker module: tracks WhatsApp Desktop window bounds at 10 Hz with per-monitor DPI awareness
- Project scaffolding and planning documents
