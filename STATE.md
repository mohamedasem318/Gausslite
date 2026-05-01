# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.2.0 — "The right regions"**

v0.1.0 and v0.1.1 are shipped. See PLAN.md for the v0.2.0 milestone
definition and CHANGELOG.md for full v0.1.x development history.

## Last session summary

**2026-05-02 — Idle repaint fix + WGC capture border + TFM bump to 22621.**

Three bugs diagnosed from log analysis and fixed in one session:

**Bug 1 — Blur intensity preset changes had no visible effect when WhatsApp was idle.**
Root cause confirmed via log: `TryRenderCurrentFrame` re-renders on the synchronous
UI-thread path (no dispatcher hop), so the D3D11 UMD driver had not flushed its
CPU-side command buffer by the time `D3DImageBridge` opened the shared D3D9Ex texture.
D3D9Ex read stale GPU content; WPF composited the previous frame regardless of
`InvalidateVisual` firing. Fix: `IBlurInterop.FlushDevice` added (calls
`ID3D11DeviceContext::Flush` via vtable dispatch — slots 40 for `GetImmediateContext`,
111 for `Flush`); called at the end of `BlurPipeline.TryRenderCurrentFrame` after all
Win2D sessions complete. `DiagnosticOverlayEnabled` diagnostic flag removed now that
root cause is confirmed. Issue #23 closed.

**Bug 2 — Yellow/amber border around WhatsApp while blur was active.**
`GraphicsCaptureSession.IsBorderRequired` was never set; Windows 11 defaults it to
`true` and draws a system border around the captured window. `ICaptureSession` gains a
`bool IsBorderRequired { set; }` property; `WinRTCaptureSession` proxies it to the WGC
session; `CaptureEngine.Start` sets it `false` before `StartCapture`.

**Bug 3 — Blur fades/washes at edges.**
`GaussianBlurEffect.BorderMode = Soft` (default) fades out-of-bounds samples to
transparent, creating a gradient fringe. Compounded by a 14 × 7 px size mismatch
between WGC ContentSize (1280 × 765) and overlay window bounds (1294 × 772). An
attempt to use `EffectBorderMode.Hard` made the edge look crisper than the interior
(repeated-pixel artifact), so it was reverted. Documented as a known issue; correct
fix (pad the source texture before blurring) deferred.

**TFM bump.** All five `.csproj` files moved from `net8.0-windows10.0.19041.0` to
`net8.0-windows10.0.22621.0`. Required to access `GraphicsCaptureSession.IsBorderRequired`
in the SDK surface area.

Test counts: Core 62/62, App 46/46 (x64). Build: 0 errors, 1 pre-existing Win2D
AnyCPU warning.

## Next up

**v0.2.0 — "The right regions"** remaining items. `IAppProfile`, blur-intensity presets,
AddDirtyRect + InvalidateVisual repaint improvements, and the region scope submenu scaffold
are done.

Pick one for the next session:

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

- **Blur edge fade (known visual defect).** `GaussianBlurEffect.BorderMode = Soft`
  combined with the 14 × 7 px WGC-to-overlay size mismatch creates a visible gradient
  fringe on all four sides. The correct fix is to pad the source texture (clamp edge
  pixels into a border of width ≥ `BlurRadius`) before running the effect so the fade
  zone falls outside the visible area. Deferred; no functional blocker.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **D3D11 context flush pattern for on-demand re-renders.** `IBlurInterop.FlushDevice`
  must be called after all Win2D drawing sessions complete in `TryRenderCurrentFrame`,
  before `PresentFrame` runs the D3D9Ex bridge. Capture frames do not need this because
  the multi-thread dispatch from the WGC callback to the UI thread provides enough
  latency for the UMD to auto-flush. On-demand (UI-thread) renders have no such gap.
  Implementation: raw vtable dispatch via `Marshal.GetDelegateForFunctionPointer`
  (ID3D11Device slot 40 → GetImmediateContext; ID3D11DeviceContext slot 111 → Flush).

- **Minimum supported OS is Windows 11 22H2 (build 22621).** Raised from Windows 10
  20H1 (19041) to gain `GraphicsCaptureSession.IsBorderRequired`. All five .csproj
  files target `net8.0-windows10.0.22621.0`. WGC itself requires 17134; the border
  suppression is the new binding constraint.

- **`ICaptureSession.IsBorderRequired` is a setter-only interface property.**
  `WinRTCaptureSession` proxies it; `CaptureEngine.Start` sets it `false` before
  `StartCapture`. Test doubles (NSubstitute) auto-stub it without changes.

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