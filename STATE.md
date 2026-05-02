# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.2.0 — "The right regions"**

v0.1.0 and v0.1.1 are shipped. See PLAN.md for the v0.2.0 milestone
definition and CHANGELOG.md for full v0.1.x development history.

## Last session summary

**2026-05-02 — Region detection arc: UIA recon, CV detector, smoke tool.**

Three deliverables shipped across this arc.

**UiaDump recon tool.** `tools/UiaDump/` is a standalone diagnostic that dumps
WhatsApp's full UI Automation tree to console. Finding: WhatsApp Desktop is a
WebView2 shell — the UIA tree stops at the `WebView2` boundary and chat content
(message list, chat header, contact list) is invisible to UIA. This invalidated
the original v0.2.0 plan of "UIA primary, CV fallback."

**WhatsAppRegionDetector (CV-primary).** Following the UIA-to-CV pivot,
`src/Gausslite.Core/Detection/WhatsAppRegionDetector` was built as a pure-C#
region detector with no third-party CV dependencies. It operates on BGRA frames
already captured by the blur pipeline and uses three-row vertical-divider edge
consensus to locate the boundary between the chat-list and conversation panes.
Returns two `Rect` values in window coordinate space. 7 unit tests, all passing.

**RegionDump smoke tool.** `tools/RegionDump/` is a standalone smoke-test utility
that captures one WhatsApp frame, runs the detector, and saves both a raw PNG and
an annotated PNG (detected boundary drawn). Eyeball-verified on default, narrow,
and wide LTR English WhatsApp Desktop layouts.

**Known limitation — deferred wiring.** The chat-list-vs-conversation assignment
heuristic (narrower side = chat list) is wrong in wide mode, where the conversation
pane can be wider than the chat list. Filed as GitHub issue #30
(https://github.com/mohamedasem318/Gausslite/issues/30); proposed fix is
horizontal-edge density analysis. Wiring the detector into BlurPipeline,
OverlayWindow, and the tray submenu is deliberately deferred until the heuristic
is resolved. The tray "Blur region" submenu remains a no-op.

Test counts: Core 68/68, App 46/46 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

**Previous sessions (2026-05-02 — Edge-fade fix, occlusion clipping, idle repaint,
WGC capture border, TFM bump to 22621)** — see HISTORY.md for full notes.

## Next up

**v0.2.0 — "The right regions"** remaining items. `IAppProfile`, blur-intensity presets,
AddDirtyRect + InvalidateVisual repaint improvements, region scope submenu scaffold,
edge-fade fix, pixel-region occlusion clipping, and the region detector module are all done.

Pick one for the next session:

v0.2.0 remaining work (no required order):

- Smoke test on 3 different WhatsApp Desktop layouts (default, narrow, wide) — verifies
  the v0.2.0 features that ARE wired (occlusion clipping, intensity presets, edge-fade fix).
  Does not require region detection to be wired.
- Wire RegionDetector into BlurPipeline / OverlayWindow / tray submenu — optional v0.2.0
  extension; currently deferred. Depends on resolving issue #30 first (wide/RTL heuristic
  wrong), or shipping with the known limitation documented.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **2026-05-02 — Region detector wiring deferred until issue #30 is resolved.**
  The detector module is built and tested, but the chat-list-vs-conversation
  assignment heuristic (narrower side = chat list) produces wrong results in
  wide mode and RTL layouts. Wiring a region-aware blur that mis-identifies
  which pane is which is worse than no region-aware blur. Defer wiring until the
  heuristic is replaced with horizontal-edge density analysis (see issue #30).

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