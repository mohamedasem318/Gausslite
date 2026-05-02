# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.2.0 — "The right regions"**

v0.1.0 and v0.1.1 are shipped. See PLAN.md for the v0.2.0 milestone
definition and CHANGELOG.md for full v0.1.x development history.

## Last session summary

**2026-05-02 — Edge-fade fix + true-region occlusion clipping.**

Two features shipped this session.

**Bug fix — Blur edge fade (Task 1).**
Root cause confirmed via systematic debugging: `GaussianBlurEffect` default `BorderMode = Soft`
fades out-of-bounds samples to transparent, creating a gradient fringe within `BlurRadius`
pixels of every edge of the render target. The 14 × 7 px WGC-to-overlay size mismatch
(documented in the previous session) compounds the effect when the render target is stretched
to fill the overlay window.

Fix: `Win2DBlurInterop.DrawPaddedBlur` (new private helper) pads the source frame before
blurring. An intermediate `CanvasRenderTarget` of size `(W + 2·pad) × (H + 2·pad)` is
allocated, filled with edge-clamped content via `BorderEffect.Clamp` + `DrawImage(sourceRect: (-pad,-pad,W+2pad,H+2pad))`,
blurred via `GaussianBlurEffect`, then only the center crop `(pad, pad, W, H)` is written
to the final D3D11 shared texture. Pad = `ceil(BlurRadius)`. Both `DrawBlur` (live frames)
and `DrawBlurFromCache` (on-demand re-render) use the same helper. First-frame diagnostic
log added to `TrayOrchestrator.OnFrameArrived` to record WGC ContentSize, overlay DIP bounds,
and BlurRadius on frame #1.

**Feature — Pixel-region occlusion clipping (Task 2).**
Replaces the v0.1.0 "center-point hide-all" behavior. When another window partially covers
WhatsApp, the overlay now clips to the visible region in real time.

`IWindowTracker` change: `OcclusionChanged: EventHandler<bool>` and `IsOccluded: bool`
replaced by `VisibleRegionChanged: EventHandler<IReadOnlyList<Rect>>` and
`VisibleRegion: IReadOnlyList<Rect>?`. Empty list = fully occluded; single rect = visible;
partial list = L-shape or multi-rect clip.

`WindowTracker` change: `IsOccludedAtCenter` (center-point hit test) replaced by
`ComputeVisibleRegion` (internal static, testable). Algorithm: walk Z-order above WhatsApp
using `GetPreviousWindow` (`GW_HWNDPREV`), skip overlay HWND and minimized/invisible windows,
subtract each covering window's rect via a 4-way split that produces up to 4 sub-rects
per overlap. Two new `IWin32Api` methods: `GetPreviousWindow` and `IsWindowVisible`.

`IOverlayWindow` gains `SetClip(IReadOnlyList<Rect>?)` which builds a frozen WPF
`GeometryGroup` of `RectangleGeometry` instances (in overlay-local DIP coordinates) and
sets it as `_contentRoot.Clip`. Null clears the clip. `TrayOrchestrator.ShowOverlay` always
calls `ApplyRegionClip` after `MoveToBounds` so the clip is refreshed on every show and
every `VisibleRegionChanged` event. `HideOverlay` clears the clip before parking offscreen.

Eager-armed-setup contract preserved: `ComputeVisibleRegion` runs on already-captured frames;
region detection does not gate capture or overlay creation.

Three follow-up bugs found via smoke test and fixed in the same session:

**Bug — title-bar "notch":** WhatsApp's own WinUI 3 `InputNonClientPointerSource` HWND sits
above the main HWND in Z-order and covers the title bar area. `ComputeVisibleRegion` was
subtracting it and clipping the title bar out of the blur. Fixed by skipping windows whose
process ID matches WhatsApp's (`IWin32Api.GetWindowProcessId` added).

**Bug — spurious clip patches during movement:** System UI windows with `WS_EX_TOOLWINDOW`
(taskbar strips, `TopLevelWindowForOverflowXamlIsland`, DWM helpers) appear above normal
windows and their rects intersected WhatsApp at various positions as it was dragged.
`ComputeVisibleRegion` was counting them as covering apps and fragmenting the visible region.
Fixed by skipping `WS_EX_TOOLWINDOW` windows (`IWin32Api.GetWindowExStyle` added).

**Bug — solid-colour placeholder flash during drag:** `BoundsOutgrewLastBlurredFrame` compared
DIP overlay width (1294) against WGC physical-pixel frame width (1280). The 14 px structural
WGC-content-area gap always exceeded the `+1` threshold, so the placeholder was shown on EVERY
position change — not just resizes. Fixed by replacing the check with `OverlaySizeGrew` which
compares consecutive DIP bounds sizes. A pure move preserves the same DIP size and never
triggers the placeholder; an actual resize still shows it. Fields `_lastBlurredFrameWidth`/
`_lastBlurredFrameHeight`/`_lastBlurredFrameTimestamp` removed (no longer needed).

Test counts: Core 68/68, App 46/46 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

**Previous session (2026-05-02 — Idle repaint fix + WGC capture border + TFM bump to 22621).**

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
AddDirtyRect + InvalidateVisual repaint improvements, region scope submenu scaffold,
edge-fade fix, and pixel-region occlusion clipping are all done.

Pick one for the next session:

v0.2.0 remaining work (no required order):

- RegionDetector module: UIA (UI Automation) primary with a
  computer-vision fallback, returning chat-list and conversation
  rects in WhatsApp's window coordinate space. Headline feature;
  largest single piece of work. Will extend `IAppProfile` when it lands.
- Tray menu region scope submenu ("Blur chat list" / "Blur
  conversation" / "Blur both") — depends on RegionDetector
- Smoke test on 3 different WhatsApp Desktop layouts (default, narrow, wide)

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **Blur edge fade fixed via padded intermediate texture.** `Win2DBlurInterop.DrawPaddedBlur`
  allocates a `(W + 2·pad) × (H + 2·pad)` CanvasRenderTarget on each frame render, fills it
  using `BorderEffect.Clamp` to extend edge pixels into the padding zone, blurs the padded
  texture, then writes only the center crop into the shared D3D11 render target. `pad = ceil(BlurRadius)`.
  No new interface members were needed; the padding is entirely internal to `Win2DBlurInterop`.
  Per-frame allocation is accepted as negligible overhead at 30fps.

- **`ComputeVisibleRegion` skips same-process and `WS_EX_TOOLWINDOW` windows.**
  Same-PID filter removes WhatsApp's own internal WinUI 3 HWNDs (e.g.
  `InputNonClientPointerSource`). `WS_EX_TOOLWINDOW` filter removes system UI strips
  (taskbar, tray, DWM helpers) that overlap WhatsApp's rect without visually covering it.
  Both filters use new `IWin32Api` methods `GetWindowProcessId` and `GetWindowExStyle`.

- **`OverlaySizeGrew` replaces `BoundsOutgrewLastBlurredFrame`.** The old function mixed DIP
  overlay size with physical-pixel frame size; the 14 px structural gap always triggered
  the placeholder. New function compares consecutive DIP bounds sizes — only true when
  WhatsApp is actually resized.

- **`IWindowTracker` visible-region API replaces the bool occlusion API.**
  `OcclusionChanged: EventHandler<bool>` and `bool IsOccluded` are removed.
  `VisibleRegionChanged: EventHandler<IReadOnlyList<Rect>>` and `IReadOnlyList<Rect>? VisibleRegion`
  are the new seam. The list is `null` when the window is absent, empty when fully occluded,
  and non-empty (one or more DIP rects) when visible. `WindowTracker.ComputeVisibleRegion`
  is an `internal static` method — directly testable without starting the poll loop.

- **Z-order walk uses `GW_HWNDPREV` on whatsappRoot, not center-point hit-test.**
  `GetWindow(hwnd, GW_HWNDPREV)` walks from WhatsApp's root upward to the topmost window.
  Each window is checked for visibility + non-minimized before subtracting its rect.
  The overlay HWND is skipped by comparing roots. This generalises correctly to multiple
  covering windows and arbitrary partial-overlap shapes.

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