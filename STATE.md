# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.2.0 — "The right regions"**

v0.1.0 and v0.1.1 are shipped. See PLAN.md for the v0.2.0 milestone
definition and CHANGELOG.md for full v0.1.x development history.

## Last session summary

**2026-04-30 — IAppProfile abstraction (v0.2.0 refactor item).** Extracted
all WhatsApp-specific window-detection knowledge behind a new `IAppProfile`
interface (`src/Gausslite.Core/AppProfiles/`). `WhatsAppProfile` is the
first concrete implementation: it owns the `IsAppWindow` predicate and
`FindWindowHandle` (via a new generic `IWin32Api.FindWindowHandle(predicate)`
method). `Win32Api.IsWhatsAppWindow` and `FindWhatsAppWindowHandle` are gone;
the predicate logic lives only in `WhatsAppProfile`.

`WindowTracker`, `CaptureItemFactory`, and `TrayOrchestrator` now receive
`IAppProfile` by constructor injection. `ICaptureItemFactory.TryCreateForWhatsApp`
renamed to `TryCreateForProfile`. `App.xaml.cs` is the sole place that
constructs `WhatsAppProfile`. All WhatsApp-hardcoded log strings replaced with
`_profile.Name` interpolations. Predicate unit tests moved from
`CaptureItemFactoryTests` to a new `WhatsAppProfileTests` in Core.Tests.
Build: 0 warnings, 0 errors. Tests: Core 53/53, App 36/36 (x64).
(Core +20 from WhatsAppProfileTests; App -18 because the 18 predicate theory
cases moved to Core.)

Full session archive in HISTORY.md.

## Next up

**v0.2.0 — "The right regions"** remaining items. `IAppProfile` is done.
Recommended next: runtime-configurable blur radius + tray intensity submenu
(Light/Medium/Heavy presets) as a self-contained pair — both are in
`BlurPipeline` and `TrayOrchestrator` and can ship together in one session.

v0.2.0 remaining work (no required order):

- RegionDetector module: UIA (UI Automation) primary with a
  computer-vision fallback, returning chat-list and conversation
  rects in WhatsApp's window coordinate space. Headline feature;
  largest single piece of work. Will extend `IAppProfile` when it lands.
- Pixel-region occlusion clipping (replaces v0.1.0 center-point
  hide-all behavior when WhatsApp is partially behind another window)
- Runtime-configurable blur radius in BlurPipeline (currently
  hardcoded at 20 DIPs)
- Tray menu intensity submenu (Light / Medium / Heavy presets,
  exposing the runtime-configurable radius via the tray)
- Tray menu region scope submenu ("Blur chat list" / "Blur
  conversation" / "Blur both")
- Smoke test on 3 different WhatsApp Desktop layouts (default, narrow, wide)

## Blockers

None. v0.1.1 is shipped clean. v0.2.0 is unblocked.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

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