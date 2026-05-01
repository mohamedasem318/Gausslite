# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.2.0 — "The right regions"**

v0.1.0 and v0.1.1 are shipped. See PLAN.md for the v0.2.0 milestone
definition and CHANGELOG.md for full v0.1.x development history.

## Last session summary

**2026-05-01 (multi-session) — Blur intensity feature shipped; idle-window on-demand repaint unresolved.**

Shipped the blur intensity preset feature: `BlurIntensityPreset` enum (Light / Medium /
Heavy), tray menu "Blur intensity" submenu with checkmark tracking, runtime-configurable
`BlurPipeline.BlurRadius` (`volatile float` backing field), and an input-frame cache
(`ICachedFrame` / `Win2DCachedFrame`) that lets `TryRenderCurrentFrame()` re-render at
the new radius without waiting for a new WGC frame. Diagnostic logging added across the
full re-render path (`SetIntensity`, `TryRenderCurrentFrame`, `UpdateD3DImage`,
`PresentFrame`).

`D3DImage.AddDirtyRect(full rect)` (between `SetBackBuffer`/`Unlock`) and
`Image.InvalidateVisual()` (after `Unlock`) are both in place and improve repaint
reliability for the natural 60fps capture path.

**What didn't ship:** switching presets while WhatsApp is fully idle has no immediate
visual effect. The on-demand re-render chain executes successfully (confirmed in logs),
but the overlay does not visually update until cursor movement over WhatsApp triggers a
new WGC frame. Multiple sessions investigated this residual bug; a stale-exe build path
issue caused several false negatives along the way. Compositor scheduling was confirmed
healthy (first `CompositionTarget.Rendering` fires within 10 ms of `InvalidateVisual`).
Root cause of DWM not presenting new pixels during idle periods is not fully understood.
Shipped as a known issue; deferred to v1.0 IDD architecture or future investigation.

Test counts: Core 62/62, App 42/42 (x64). Build: 1 pre-existing Win2D warning, 0 errors.

## Next up

**v0.2.0 — "The right regions"** remaining items. `IAppProfile`, blur-intensity presets,
and the AddDirtyRect + InvalidateVisual repaint improvements are done. Known issue:
idle-window preset repaint deferred (see Last session summary).

Pick one for the next session:
- **Pixel-region occlusion clipping** — blur only the visible portion of WhatsApp when
  it is partially behind another window; replaces the v0.1.0 center-point hide-all
  behavior. No dependency on RegionDetector.
- **Tray menu region scope submenu** ("Blur chat list" / "Blur conversation" / "Blur
  both") — scaffold can land now; becomes functional when RegionDetector lands.

v0.2.0 remaining work (no required order):

- RegionDetector module: UIA (UI Automation) primary with a
  computer-vision fallback, returning chat-list and conversation
  rects in WhatsApp's window coordinate space. Headline feature;
  largest single piece of work. Will extend `IAppProfile` when it lands.
- Pixel-region occlusion clipping (replaces v0.1.0 center-point
  hide-all behavior when WhatsApp is partially behind another window)
- Tray menu region scope submenu ("Blur chat list" / "Blur
  conversation" / "Blur both") — depends on RegionDetector
- Smoke test on 3 different WhatsApp Desktop layouts (default, narrow, wide)

## Blockers

None. v0.1.1 is shipped clean. v0.2.0 is unblocked.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **Blur pipeline retains a copy of the most recent input frame for on-demand re-render.**
  `BlurPipeline` allocates a `ICachedFrame` (Win2D `CanvasRenderTarget`, no D3D9Ex shared
  flag needed) alongside the render target. `TryRenderCurrentFrame()` re-renders from this
  cache at the current radius. Region-scope and similar future blur-parameter changes will
  call `TryRenderCurrentFrame` the same way.

- **`BlurIntensityPresets` is the single source of truth for preset numeric values.**
  `BlurPipeline.DefaultBlurRadius` derives from `BlurIntensityPresets.MediumRadius`
  so both always agree. Future slider code in v0.4.0 bypasses the preset entirely
  and writes a radius float directly to `BlurPipeline.BlurRadius`.

- **`BlurRadius` backing field is `volatile float`.** Written from the WPF UI thread
  (tray menu click → `TrayOrchestrator.SetIntensity`), read from the frame-processing
  thread. `volatile` prevents the JIT from caching the value in a register across
  frame invocations and matches the existing Interlocked-for-cross-thread-writes
  pattern in `TrayOrchestrator`.

- **`IWin32Api.FindWindowHandle(predicate)` is the generic window-finder.**
  `WhatsAppProfile.FindWindowHandle()` calls it with `IsAppWindow` as the
  predicate. `WindowTracker` calls `_profile.FindWindowHandle()`. This keeps
  all P/Invoke enumeration in `Win32Api` while keeping app-specific criteria
  in `WhatsAppProfile`.

- **`CaptureItemFactory` keeps its own diagnostic enumeration loop.** It uses
  `_profile.IsAppWindow()` inside its existing per-window logging loop rather
  than delegating to `_profile.FindWindowHandle()`, preserving detailed
  per-window log lines during capture-item creation.

- **`TrayOrchestrator` also receives `IAppProfile`** for the two log strings
  ("armed - waiting for {Name} HWND", "restore arrived while {Name} is still
  occluded") that previously hardcoded "WhatsApp".

Decisions carrying forward from v0.1.0 development that future v0.2.0+
work should keep in mind:

- **Eager armed setup** is the privacy contract. Capture and overlay
  are prepared the moment WhatsApp's HWND is first seen; restore and
  unocclude transitions are SetWindowPos moves only, not fresh capture
  setup. Region-aware blur in v0.2.0 must preserve this — region
  detection runs on already-captured frames, not as a precondition for
  showing the overlay.

- **Overlay HWND parking uses offscreen-but-visible coordinates**
  (-32000, -32000) rather than `Visibility=Hidden`. WPF defers layout
  and first paint while a window is hidden, which causes a ~250ms
  first-show cost. The current strategy keeps the WPF window visible
  at all times and moves the HWND on/off screen via SetWindowPos.

- **WindowTracker emits WPF DIP bounds**, not physical pixels. In a
  PerMonitorV2 process (which Gausslite is), GetWindowRect returns
  physical pixels; the tracker divides by GetDpiForWindow / 96.0
  before raising BoundsChanged. v0.2.0 region detection must continue
  to operate in DIP space at the OverlayWindow boundary.

- **Maximized WindowTracker bounds are normalized** to the monitor
  work area before DIP conversion to clip Windows' invisible
  extended-frame coordinates.

- **WhatsApp detection** = process name prefix "WhatsApp"
  (case-insensitive) OR window class "WinUIDesktopWin32WindowClass"
  with title containing "WhatsApp", explicitly excluding
  msedgewebview2 (the WebView2 child process that renders chat UI).

- **Tray library = Hardcodet.NotifyIcon.Wpf** (not H.NotifyIcon.Wpf).
  H.NotifyIcon silently fails to register the tray icon on at least
  one tested machine despite all setup steps succeeding.

- **Solution pinned to x64.** Win2D requires a concrete platform;
  ARM64 support deferred to post-v1.

- **Win2D shared-texture path:** CanvasRenderTarget(CanvasDevice, w,
  h) does NOT set D3D11_RESOURCE_MISC_SHARED, so
  IDXGIResource.GetSharedHandle would always fail in D3DImageBridge.
  Workaround: create the D3D11 texture manually with
  D3D11_RESOURCE_MISC_SHARED, wrap as IDirect3DSurface via
  CreateDirect3D11SurfaceFromDXGISurface, then use
  CanvasRenderTarget.CreateFromDirect3D11Surface. This is the path
  taken in Win2DBlurRenderTarget. Future Win2D-related work should
  not regress this; comment in Win2DBlurRenderTarget explains why.

- **Partial-occlusion hide-all behavior is a known v0.1.x limitation**
  to be fixed in v0.2.0 with pixel-region occlusion clipping
  alongside region-aware blur.

- **Armed blur state is silent.** If blur is enabled while WhatsApp
  is missing or minimized, the app waits without showing overlay or
  starting capture. User-facing notification of armed state is
  deferred to v0.4.0 settings as an opt-in toggle, off by default.

- **App test runs require x64 platform explicitly.** A default AnyCPU
  `dotnet test` invocation against the App test project hangs after
  discovery. `dotnet test --arch x64` works correctly. This is a
  long-standing testhost/platform issue documented across v0.1.0
  sessions; not a regression and not a blocker.