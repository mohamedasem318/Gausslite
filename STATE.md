# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.2.0 — "The right regions"**

v0.1.0 and v0.1.1 are shipped. See PLAN.md for the v0.2.0 milestone
definition and CHANGELOG.md for full v0.1.x development history.

## Last session summary

**2026-05-02 — v0.2.0 Session A: detection plumbing (branch v0.2.0-detection-plumbing).**

Wired `WhatsAppRegionDetector` into the live capture path. Detection only — no clip
changes, no scope-aware blur, no balloons. Visual behavior is unchanged.

**GPU→CPU readback.** Added `IBlurStagingTexture` / `Win2DBlurStagingTexture` and two new
`IBlurInterop` methods: `CreateStagingTexture` (D3D11_USAGE_STAGING / D3D11_CPU_ACCESS_READ)
and `TryReadBgra` (CopyResource + Map/Unmap via vtable-dispatch delegates at slots 47/14/15;
Flush at 111 before the copy). `BlurPipeline` manages the staging texture's lifecycle
(allocated on first read, reused on same dimensions, reallocated on resize, disposed with the
pipeline). `Win2DCachedFrame` creation changed to the explicit D3D11 texture path (same as
`Win2DBlurRenderTarget`) so the backing `IDirect3DSurface` is available for the QI chain.

**Detector triggering.** `TrayOrchestrator` gains `IRegionDetector` as an 8th constructor
dependency (wired in `App.xaml.cs` with `new WhatsAppRegionDetector()`). Detection fires on
the first successfully blurred frame (Interlocked one-shot flag; dispatched to UI thread with
`_setupGeneration` guard against stale-session writes) and on every `BoundsChanged` event.
Results are stored in `_lastDetectionResult` (private, `internal` accessor `LastDetectionResult`
for tests). Logged via `StartupLog.Info` on both success and failure.

**Tests.** 6 new `BlurPipelineTests` (readback lifecycle: first-read allocates, reuse, resize,
dispose). 4 new `TrayOrchestratorTests` (first-frame detection, second-frame no-op, BoundsChanged,
readback-fail skip). `FixedResultDetector` helper avoids NSubstitute's `ReadOnlySpan<byte>` limitations.
Counts: Core 85/85, App 50/50 (x64). Build: 0 errors, 0 warnings.

**2026-05-02 — RTL rail-side detection (issue #30).**

Fixed the LTR-only chat-list assignment bug filed as issue #30.

**Root cause correction.** The previous session notes described the bug as "narrower
side = chat list heuristic fails in wide mode." That description was inaccurate — the
actual code always assigned the *left* panel as chat list regardless of width. Wide-mode
LTR was never broken. The real failure was RTL layouts (Arabic, Hebrew) where WhatsApp
mirrors the UI and the chat list appears on the right.

**Rail-side detection algorithm.** `WhatsAppRegionDetector.Detect()` runs a
`DetermineRailSide` step after finding the divider. It walks inward from each outer
edge column-by-column (skipping the top 30 px title-bar area and the outermost 5 px
border), counting sampled rows where `RowDelta(x, y) > 30`. When a column's busy-row
count reaches 25% of effective frame rows, that column is the first "content" column
and the walk stops. The width of the quiet zone before that stop is the rail-width
estimate. The side with the larger estimate is the rail. If neither side finds a
content column in the search range the default is 0 (no evidence), preventing
a featureless background from impersonating a wide rail. Ties → LTR (Left) fallback.
Search range: min(200 px, 40 % of frame width) — fixed-pixel ceiling ensures the
scan reaches chat-list content even on narrow windows where a fraction-based limit
would fall short.

**Validated on live WhatsApp (LTR English and RTL Arabic), three layouts each:**
- LTR: left quiet ≈ 92 px, right quiet = 0 → Rail=Left ✓ (all widths)
- RTL: left quiet = 0, right quiet ≈ 95 px → Rail=Right ✓ (all widths)
Annotated PNGs confirmed green=CHAT LIST on the correct side.

**New types.** `RailSide` enum (`Left`, `Right`) in `Gausslite.Core.Detection`.
`RegionDetectionResult` gains `DetectedRailSide`, `RailSideLeftWidth`, and
`RailSideRightWidth` for downstream callers and diagnostics.

**4 unit tests** (11 Detection tests total): RailOnLeft, RailOnRight (the RTL case),
NoRailSignal_DefaultsToLeft, RailOnLeft_WithRightScrollbar.

Test counts: Core 79/79, App 46/46 (x64). Build: 0 errors, 0 warnings.

**Previous sessions (2026-05-02 — Edge-fade fix, occlusion clipping, idle repaint,
WGC capture border, TFM bump to 22621)** — see HISTORY.md for full notes.

## Next up

**v0.2.0 Session B — consume detection results.**

Detection now runs and results are stored. Session B consumes `_lastDetectionResult` to drive:
- `OverlayWindow.SetClip` / `ApplyRegionClip` — scope-aware blur (chat list, conversation, both)
- Coordinate-space conversion utilities (frame-pixel rects → DIP overlay-local rects)
- Balloon or log notification when detection fails on a production frame
- `RegionDump` annotation update (optional tidy)

All pre-existing v0.2.0 work (occlusion clipping, intensity presets, edge-fade fix, region scope
submenu scaffold) remains working and unchanged.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **2026-05-02 — Region detector wiring deferred; issue #30 resolved.**
  The detector was originally wiring-deferred because the chat-list assignment was
  purely positional (left panel = chat list) and therefore wrong for RTL layouts.
  Issue #30 is now fixed: `DetermineRailSide` uses horizontal-edge density in the
  outer 15 % strips to find the navigation-rail side and assigns panes accordingly.
  Wiring into BlurPipeline / OverlayWindow / tray submenu remains the next step.

- **2026-05-02 — Pivoted from UIA-primary-with-CV-fallback to CV-only.**
  `tools/UiaDump` recon confirmed WhatsApp Desktop is a WebView2 shell; UIA cannot
  see any chat content past the WebView2 boundary. The UIA path cannot work on the
  current WhatsApp build at all — not partially, not as a primary. Two-path code for
  a path that never executes is dead weight. `WhatsAppRegionDetector` is CV-only.

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