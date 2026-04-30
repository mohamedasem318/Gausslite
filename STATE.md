# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Renamed project to Gausslite. Solution, projects, namespaces, folders,
assembly names, log filenames, README, PLAN, and STATE updated. Project GUIDs
preserved. CHANGELOG historical entries kept verbatim. Build clean, all tests
green.

---

**v0.1.0 offscreen overlay parking privacy fix.**

The remaining first restore leak was traced to WPF deferring layout and the
first compositor paint while the overlay `Window` had `Visibility=Hidden`.
The eager setup from the previous session stays in place: WhatsApp
HWND-first-seen still runs `TryCreateForWhatsApp`, creates the overlay HWND,
and starts `CaptureEngine` before the restore/unocclude path.

The overlay parking strategy is now offscreen-but-visible. `OverlayWindow`
creates the HWND as a WPF-visible window at `(-32000, -32000)` via
`ShowOffscreen(initialBounds)`, keeps the opaque placeholder visible during
setup, and never uses a hidden/visible WPF visibility flip for armed/active
transitions. `Armed` now means capture+overlay are alive and the overlay HWND
is parked offscreen. `Active` means the same HWND is positioned at WhatsApp's
current bounds. Restore/unocclude calls `MoveToBounds(Rect)`; minimize or
occlusion calls `MoveOffscreen()`. Both paths use `SetWindowPos` through the
existing bounds applier, so the privacy-critical metric is now logged as
`event-to-move` with an expected value under 20ms.

Tests were updated away from `Visibility=Hidden` assertions. The orchestrator
tests now assert eager `ShowOffscreen`, armed `MoveOffscreen`, active
`MoveToBounds`, and no fresh capture setup on armed restore. New overlay tests
verify the parked coordinates, on-screen bounds move, offscreen move, and that
these operations occur while the WPF window remains `Visible`.

**Verification:** Debug solution build passed with 0 warnings/errors using
`dotnet build Gausslite.sln -c Debug -m:1 --no-restore`. Core tests passed
(33/33). App tests were written and attempted, but both the full x64 App test
run and a focused `TrayOrchestratorTests` filter still hung after discovery in
this shell, matching the documented App testhost issue. No manual WhatsApp
smoke test was run in this session, so no real `event-to-move` numbers were
recorded.

---

**v0.1.0 eager armed setup privacy fix.**

The armed->active privacy leak is fixed by moving the expensive work out of
the restore/unocclude transition. `TrayOrchestrator` now treats blur activation
state separately from whether capture+overlay have been prepared for the
current WhatsApp HWND. When blur is enabled and `IWindowTracker` already sees
WhatsApp, or when `WindowPresenceChanged(true)` arrives during an armed
session, the orchestrator eagerly runs `CaptureItemFactory.TryCreateForWhatsApp`
on a background thread, creates the overlay HWND with the placeholder visible
but `Visibility=Hidden`, applies the current/default bounds, and starts
`CaptureEngine` before WhatsApp is privacy-visible.

`Armed` now means blur is enabled and the overlay is hidden. If a WhatsApp HWND
has been seen for the current session, capture+overlay are already alive; if no
HWND has been seen yet, the app is still waiting to do that eager setup.
`Active` means capture+overlay are alive and the overlay is visible. Minimize
or occlusion flips visibility to hidden and keeps capture alive. Restore or
unocclude flips visibility to visible and applies current bounds only; it does
not call `TryCreateForWhatsApp`, `OverlayWindow.Show` on a fresh HWND, or
`CaptureEngine.Start`. WhatsApp close is the path that stops capture and clears
the prepared setup so reopen runs eager setup again for the new HWND.

Diagnostics now log every `Idle`/`Armed`/`Active` transition, eager setup begin
and end with elapsed milliseconds, hidden/visible flips, and the privacy-
critical elapsed time from `MinimizedChanged(false)` or
`OcclusionChanged(false)` receipt to the visible-flip-applied log line.

**Verification:** Debug solution build passed with 0 warnings/errors. Core
tests passed (33/33). Full App tests passed when run as x64 (51/51). A default
AnyCPU App test invocation still hung after discovery in this shell, matching
the previously documented testhost/platform issue; the x64 path is the valid
one for this Win2D project. No manual WhatsApp smoke test was run in this
session, so no real `gausslite-startup.log` event-to-flip number was recorded.

---

**v0.1.0 armed-restore dispatcher priority privacy fix.**

The remaining armed->restore privacy gap was traced to WPF dispatcher queue
latency, not the restore handler's behavior. `TrayOrchestrator` was dispatching
all tracker events at `DispatcherPriority.Normal`, so during a WhatsApp
restore-from-minimize the poll-thread `MinimizedChanged(false)` work could sit
behind WPF input, layout, and render work while raw WhatsApp content was already
visible.

`TrayOrchestrator` now accepts an explicit dispatcher priority per tracker
event, defaulting to `Normal`. Bounds changes and non-privacy transitions stay
at `Normal`; the privacy-critical visibility transitions
`MinimizedChanged(false)` and `OcclusionChanged(false)` dispatch at
`DispatcherPriority.Send` so the opaque placeholder/show path jumps to the
front of the UI queue. `DispatchToWpfUiThread` also logs the elapsed queue
latency when the UI action runs, making future regressions visible in
`gausslite-startup.log`.

### Smoke test note
- Reproduction: minimize WhatsApp, enable blur, restore WhatsApp from the
  taskbar. The opaque placeholder should cover WhatsApp as soon as the restored
  window appears, and the log gap between `received minimized=False` and
  `applying minimized=False on UI thread` should be under 5ms.

**Verification:** Debug solution build passed with 0 warnings/errors. Core tests
passed (33/33). Focused `Gausslite.App.Tests` execution for
`TrayOrchestratorTests` still hangs after discovery in this shell, matching the
pre-existing App testhost blocker from prior sessions.

---

**v0.1.0 armed-restore privacy fix and documented occlusion limitation.**

`TrayOrchestrator` now has a testable UI-dispatch seam, and the
`MinimizedChanged(false)` path is covered for the armed state. When WhatsApp is
restored after blur was enabled while minimized, the orchestrator attempts
`CaptureItemFactory.TryCreateForWhatsApp` immediately from the restore event and
starts capture if the visible window is ready. `StartCapture` shows the opaque
placeholder before Windows Graphics Capture delivers its first frame, so the
restore path no longer waits for the later bounds retry before covering
WhatsApp.

The partial-occlusion behavior is documented as a known v0.1.0 limitation:
because occlusion is currently center-point based, a partially visible WhatsApp
window may cause the whole overlay to hide rather than clipping to the visible
region. Pixel-region clipping is deferred to v0.2.0 alongside region-aware blur.

### Smoke test note
- Reproduction for armed->restore privacy fix: minimize WhatsApp, enable blur,
  restore WhatsApp. Opaque placeholder must appear within one poll cycle (~33ms)
  of WhatsApp becoming visible. Live blur replaces placeholder when first frame
  arrives. WhatsApp content must never be readable.

**Verification:** Debug solution build passed with 0 warnings/errors. Core tests
passed (33/33). Focused `Gausslite.App.Tests` execution still hangs after
discovery via `dotnet vstest`, matching the pre-existing App testhost blocker
from prior sessions.

---

**v0.1.0 final visibility/privacy fixes: occlusion-aware overlay and reliable restore placeholder.**

Issue A is fixed in `WindowTracker`: each poll now checks the center point of
WhatsApp's physical bounds with `WindowFromPoint`, normalizes child HWNDs to
their root window, and compares that root to WhatsApp. The tracker exposes
`OcclusionChanged` and `IsOccluded`; `TrayOrchestrator` hides the overlay while
WhatsApp is occluded but keeps capture alive, then shows and reapplies cached
bounds when WhatsApp becomes visible again. The overlay HWND is passed back into
the tracker so the topmost overlay is skipped with `GetWindow(GW_HWNDNEXT)` when
it is the window returned at the center point.

Issue B is fixed by making the opaque charcoal placeholder engage on every
privacy-sensitive show path. `OverlayWindow.Hide()` resets the placeholder to
visible for the next show, `TrayOrchestrator` shows the placeholder before
startup capture, and restore/unocclusion paths show it whenever the last blurred
frame is missing or older than 5 seconds. The placeholder is hidden only after a
successful `PresentFrame`.

### Fixed
- **Occluded WhatsApp:** overlay visibility now follows whether WhatsApp is
  actually visible at its center point, including overlay-self-exclusion.
- **Armed -> active privacy gap:** restore from minimized/armed state now paints
  an opaque dark rectangle immediately until a fresh blurred frame is presented.

### Smoke test sequence for armed restore placeholder
1. Minimize WhatsApp.
2. Enable blur. The overlay should not appear yet; this is armed state.
3. Click WhatsApp in the taskbar to restore.
4. Watch the moment WhatsApp appears: an opaque dark rectangle should appear
   over WhatsApp's window region instantly, before any WhatsApp pixels are
   visible. Within about 1 second the rectangle should become a live blurred
   view.
5. At no point during step 4 should WhatsApp content be readable.

**Verification:** Debug solution build passed with 0 warnings/errors. Core tests
passed (33/33), including new occlusion transition and overlay-self-exclusion
tests. Focused App test execution still hangs after discovery in this shell via
`dotnet vstest`, matching the pre-existing App testhost blocker from prior
sessions.

---

**v0.1.0 final privacy blockers: active-resize frame-pool recreation and armed-state placeholder cover.**

Issue A was traced to the Windows Graphics Capture layer. `BlurPipeline`
already reallocates its Win2D render target when frame dimensions change, and
`D3DImageBridge` already calls `SetBackBuffer` for every presented frame. The
missing piece was `Direct3D11CaptureFramePool.Recreate(...)`: `CaptureEngine`
now logs every incoming WGC frame `ContentSize`, detects content-size changes,
recreates the frame pool at the new size, and drops the transition frame so a
new-size render target is not fed by an old-size pool surface. A regression test
covers the recreate-and-drop behavior.

Issue B is covered by an explicit opaque overlay placeholder. `OverlayWindow`
now has a charcoal placeholder layer that stays visible until the first frame is
presented. `TrayOrchestrator` shows that placeholder and applies current bounds
before starting capture, so armed-state restore does not expose raw WhatsApp
content during WGC startup. During active resize, if overlay bounds outgrow the
last blurred frame, the placeholder is shown until the resized frame arrives.

### Fixed
- **Maximize during active blur:** `CaptureEngine` recreates the WGC frame pool
  when frame `ContentSize` changes and logs every incoming WGC content size for
  smoke-test diagnosis.
- **Armed-state restore privacy gap:** overlay startup now uses an opaque
  placeholder before capture begins and hides it after the first presented blur
  frame.

**Verification:** Debug solution build passed with 0 warnings/errors. Core tests
passed (29/29), including the new `CaptureEngine` frame-pool recreation test.
The App testhost still hangs after discovery in this shell, including with a
`TrayOrchestratorTests` filter; this matches the pre-existing App testhost
blocker noted in prior sessions.

---

**v0.1.0 final blocker fixes: native overlay resizing and stale-frame restore privacy.**

`OverlayWindow.ApplyBounds` now keeps the WPF window properties in sync but also
positions the HWND with `SetWindowPos`. This bypasses the transparent,
borderless WPF window sizing path that can fail to visually honor large
maximized or screen-edge bounds even when `Window.Left/Top/Width/Height` are
assigned. `ApplyBounds` now logs actual overlay, grid, and image size after
every bounds application, so future smoke-test logs show whether requested
bounds became real layout.

Restore-from-minimize no longer tears down capture. When WhatsApp minimizes,
the overlay hides but the capture session and last blurred `D3DImage` content
stay alive. When WhatsApp restores, the orchestrator shows the overlay
immediately and logs `OverlayWindow.Show called with last frame age = X ms`
before waiting for any new WGC frame, so the user sees stale blur instead of
raw WhatsApp content during slow WinUI/WebView2 repaint.

### Fixed
- **Maximized overlay resize:** overlay placement now uses native
  `SetWindowPos` after WPF property assignment, with tests covering large
  maximized-style bounds and negative extended-frame coordinates.
- **Persistent overlay size diagnostics:** every `ApplyBounds` call schedules
  an actual-size log for the WPF window, grid, and image.
- **Restore-from-minimize privacy gap:** minimize hides the overlay without
  stopping capture; restore shows the overlay immediately using the last
  blurred frame and logs the stale-frame age.

**Verification:** Debug solution build passed with 0 warnings/errors. New
`WindowBoundsApplierTests` passed (2/2). Full Core tests are still blocked in
this shell by the pre-existing Windows Graphics Capture service COMException.
The broader App testhost still hangs after discovery outside the isolated
overlay test filter. Release build was blocked because a running `Gausslite.App`
process held the Release output `Gausslite.Overlay.dll` open.

---

**v0.1.0 smoke-test fixes: maximize tracking, drag lag, and armed blur activation.**

`WindowTracker` now polls at 33ms (~30 Hz) instead of 100ms, so fast title-bar drags should no longer visibly outrun the overlay. Its bounds path now distinguishes normal window rectangles from maximized rectangles: normal windows still report the raw visual window bounds converted from physical pixels to WPF DIPs, while maximized windows are normalized to the current monitor work area before DPI conversion. This clips Windows' invisible maximized frame extension (for example `-8,-8` / monitor+8 raw `GetWindowRect` values) and keeps multi-monitor per-monitor-DPI behavior intact.

`TrayOrchestrator` now uses an explicit blur activation state machine:

- `Idle`: blur is disabled.
- `Armed`: blur is enabled but waiting for a visible, non-minimized WhatsApp window. No overlay is shown and capture is not started.
- `Active`: capture is running and the overlay is shown/following bounds.

Enabling blur while WhatsApp is missing or minimized now enters `Armed` silently. Tracker polling continues, and the orchestrator transitions to `Active` when WhatsApp appears/restores with valid bounds. At the time of that session, closing or minimizing WhatsApp while blur was active stopped capture and returned to `Armed`; the final blocker fix above changed minimize specifically to keep capture alive while the overlay is hidden. This replaces the previous implicit "retry on BoundsChanged" path.

### Fixed
- **Maximize overlay position:** `WindowTracker` normalizes maximized `GetWindowRect` output to `MonitorFromWindow`/`GetMonitorInfo.rcWork` before converting to WPF DIPs, preventing the overlay from staying offset by the invisible extended frame.
- **Drag lag:** default `WindowTracker` polling interval reduced from 100ms to 33ms.
- **Arming while minimized/not running:** `TrayOrchestrator.EnableBlur` no longer starts capture or shows overlay unless the tracker reports WhatsApp present, non-minimized, and with current bounds.

### Added
- **Window presence tracking:** `IWindowTracker.WindowPresenceChanged`, `IsWindowPresent`, and `IsMinimized` let orchestration react to WhatsApp close/reopen and minimized/restore transitions explicitly.
- **State-machine tests:** added tests for `Idle -> Armed -> Active`, `Idle -> Active`, and `Active -> Armed -> Active`.
- **Coordinate-normalization tests:** added maximized bounds normalization coverage at 100%, 125%, and 150% DPI, including negative-coordinate monitor layouts.

**Build:** `dotnet build Gausslite.sln --no-restore -v:minimal -maxcpucount:1` passed with 0 warnings/errors. `WindowTracking` tests passed (12/12). Full `dotnet test` is currently blocked in this shell by pre-existing environment issues: Windows Graphics Capture tests throw `COMException: The specified service does not exist as an installed service`, and the App testhost hangs before entering even a pure state-machine test. See notes below.

---

**WindowTracker/TrayOrchestrator hardening after overlay smoke diagnostics.**

`WindowTracker` events are raised from its polling thread, but `TrayOrchestrator` was touching the WPF overlay directly from those callbacks. Bounds updates and minimize/restore overlay operations are now marshalled to `Application.Current.Dispatcher`; the handlers still log immediately on the originating polling thread, and shutdown races where `Application.Current` is null or the dispatcher is shutting down are logged and dropped.

The tracker also now logs bounds changes with throttling instead of logging only the first change. This keeps drag diagnostics visible (`#1`, `#10`, `#20`, ...) without flooding `gausslite-startup.log`.

Finally, the DPI path was corrected. The app manifest is `PerMonitorV2`, so `GetWindowRect` returns physical pixels. Because `OverlayWindow.SetBounds` assigns WPF `Window.Left/Top/Width/Height`, `WindowTracker` converts physical pixels to WPF DIPs by dividing by `GetDpiForWindow(hwnd) / 96.0`; the previous multiply path double-scaled high-DPI displays.

### Fixed
- **`TrayOrchestrator`** now dispatches `OnBoundsChanged` and `OnMinimizedChanged` UI work via the WPF dispatcher before calling `IOverlayWindow.SetBounds`, `Hide`, or `Show`.
- **`WindowTracker`** replaced the one-shot `_firstBoundsChangeLogged` guard with throttled bounds-change logging every 10 changes.
- **`WindowTracker`** now returns WPF DIP bounds from physical `GetWindowRect` pixels by dividing by the HWND DPI scale.
- Updated the 150% DPI unit test to catch double-scaling regressions.
- Updated `app.manifest` comments to document the PerMonitorV2 physical-pixel-to-WPF-DIP contract.

**61 tests green (38 Gausslite.App.Tests + 23 Gausslite.Core.Tests).**

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
- **`Win32Api`**: Removed `FindStoreWhatsAppWindowHandle()`. Added `internal static IsWhatsAppWindow(processName, className, title)` (the single authoritative predicate, accessible to `Gausslite.App` via existing `InternalsVisibleTo`). Added `FindWhatsAppWindowHandle()` using that predicate.
- **`WindowTracker`**: `SampleBoundsWithHandle` now calls `_win32.FindWhatsAppWindowHandle()` only — no more process-name loop or two-strategy fallback. Removed `WhatsAppProcessNames` array.
- **`CaptureItemFactory`**: Replaced `FindWin32WhatsAppWindow`/`FindStoreWhatsAppWindow` and Strategy A/B with single `FindWhatsAppWindow()`. Removed `IsWin32WhatsAppProcess`/`IsStoreWhatsAppWindow`; added `IsWhatsAppWindow` wrapper to `Win32Api.IsWhatsAppWindow`. Added per-candidate `DiagLog` tracing (up to 20 candidates, with `match` + `reason` per entry).
- **`CaptureItemFactoryTests`**: Replaced `IsWin32WhatsAppProcess` + `IsStoreWhatsAppWindow` predicate tests with 16 `IsWhatsAppWindow` tests covering all match/reject cases. Integration test `TryCreateForWhatsApp_WhenWhatsAppNotRunning` now skips gracefully when WhatsApp IS running (detection success correctly returns true in that case).
- **`WindowTrackerTests`**: Updated mock setup from `GetWindowHandlesForProcessName` to `FindWhatsAppWindowHandle()`.

**57 tests green (35 Gausslite.App.Tests + 22 Gausslite.Core.Tests). Build 0 errors, 0 warnings.**

Added step-by-step `StartupLog`/`DiagLog` tracing to every layer of the blur path so a top-to-bottom read of `gausslite-startup.log` reveals exactly where things go silent when "Enable blur" is clicked with no visible result.

### Added
- **`src/Gausslite.Core/Diagnostics/DiagLog.cs`** — new internal static logger (same gausslite-startup.log file, same ISO-8601 timestamp format as `StartupLog`). Used by Gausslite.Core and Gausslite.Overlay, which cannot reference Gausslite.App. `InternalsVisibleTo(Gausslite.Overlay)` added to Gausslite.Core.csproj.
- **`TrayOrchestrator`** — `EnableBlur` now logs entry, WindowTracker start/started, TryCreateForWhatsApp call and result, abort if WhatsApp not found, CaptureEngine start, OverlayWindow show, SetBounds call, complete. `DisableBlur` logs entry/complete. `OnFrameArrived` logs first-frame dimensions (Interlocked flag), a per-60-frame heartbeat, and any exception (logged then re-thrown).
- **`CaptureEngine.Start`** — logs entry, `GraphicsCaptureSession.IsSupported()` result, frame-pool creation, FrameArrived subscription, session start. First WGC frame arrival logged once via Interlocked flag.
- **`WindowTracker`** — logs first WhatsApp window detected (HWND + bounds), first bounds change, and a one-time warning if WhatsApp is not found within 5 seconds of the poll loop starting. Refactored `SampleBounds` → `SampleBoundsWithHandle` to return the HWND alongside the rect for logging.
- **`OverlayWindow.Show`** — logs entry (current Visibility), and HWND + IsVisible after `_window.Show()`. **`SetBounds`** logs all four coordinates. **`PresentFrame`** logs first-call source dimensions (Interlocked flag); exceptions are caught, logged via DiagLog, and swallowed so one bad frame cannot crash the app.
- **`D3DImageBridge.UpdateD3DImage`** — first-call only (Interlocked flag) logs every step of the WinRT→DXGI→D3D9Ex bridge: surface acquisition, QI for IDirect3DDxgiInterfaceAccess (with HR), GetInterface for ID3D11Texture2D (HR), QI for IDXGIResource (HR), GetSharedHandle result (HR + handle hex value). **CRITICAL log**: if shared handle is zero logs "ABORT — render target was not created with DXGI_RESOURCE_MISC_SHARED". All exceptions caught, logged, swallowed (no re-throw).

**All 39 tests still green (17 Gausslite.App.Tests + 22 Gausslite.Core.Tests). Build 0 errors, 0 warnings.**

---

**Previous session — Library swap** — H.NotifyIcon.Wpf replaced with Hardcodet.NotifyIcon.Wpf 2.0.1.

Diagnostic evidence from the previous session's `gausslite-startup.log` proved conclusively that our code was correct: the .ico file existed on disk, `BitmapImage` loaded with real 32×32 dimensions, `TaskbarIcon` was constructed, `IconSource` was assigned, `Visibility` was forced to `Visible`, and the `System.Drawing.Icon` fallback also succeeded — yet no icon ever appeared in the system tray. The bug is inside H.NotifyIcon or its interaction with the Windows shell notification area on at least one tested machine. Swapped to Hardcodet.NotifyIcon.Wpf (the original project that H.NotifyIcon forked from), which is more mature and widely deployed.

### Changed
- `Gausslite.App.csproj`: Replaced `H.NotifyIcon.Wpf 2.1.0` with `Hardcodet.NotifyIcon.Wpf 2.0.1`.
- `TrayIconHost.cs`: Updated `using H.NotifyIcon;` → `using Hardcodet.Wpf.TaskbarNotification;`. All other code is unchanged — the `TaskbarIcon` API (`IconSource`, `Icon`, `ToolTipText`, `ContextMenu`, `Visibility`, `Dispose`) is identical between the two libraries.
- All diagnostic logging and the `System.Drawing.Icon` fallback retained unchanged.

**All 39 tests still green (17 Gausslite.App.Tests + 22 Gausslite.Core.Tests). Build 0 errors, 0 warnings (1 platform warning only when building project directly, not via sln).**

---

**Previous session — Debugging session** — tray icon still not visible during v0.1.0 smoke testing despite prior fixes (icon file present on disk, no crash log written, process alive at ~164 MB). Added step-by-step diagnostic instrumentation to pinpoint exactly where initialization silently stalls.

### Added — Startup diagnostic log (`gausslite-startup.log`)
- New `src/Gausslite.App/Diagnostics/StartupLog.cs`: `Info`/`Warn` methods, ISO 8601 timestamps with milliseconds, append-mode with flush-per-write, truncated on each app start so the file always reflects the most recent run.
- `App.xaml.cs` `OnStartup` now logs "OnStartup begin / TrayOrchestrator constructed / TrayIconHost constructed / Initialize() returned / OnStartup complete" with try/catch around each step; exceptions are logged then re-thrown so the existing crash-log behaviour is preserved.
- `TrayIconHost.Initialize` logs every step: resolved icon path, `File.Exists`, `BitmapImage` (IsFrozen, PixelWidth, PixelHeight), `TaskbarIcon` creation, `IconSource` assignment, `Visibility` before and after forcing it to `Visible`.

### Added — `System.Drawing.Icon` native fallback in `TrayIconHost.Initialize`
After the `BitmapImage`/`IconSource` path, also sets `taskbarIcon.Icon = new System.Drawing.Icon(iconPath)`. This is the legacy HICON path that bypasses WPF imaging entirely; H.NotifyIcon accepts either. If the WPF path silently fails, the native path may succeed. Success/failure of this fallback is both logged and non-fatal (wrapped in try/catch). Added `System.Drawing.Common 8.0.7` to `Gausslite.App.csproj` (version pinned to match the transitive version required by `H.GeneratedIcons.System.Drawing 2.1.0`).

### Added — Force `Visibility.Visible` on `TaskbarIcon`
Explicitly sets `_taskbarIcon.Visibility = Visibility.Visible` after construction. H.NotifyIcon's default `Visibility` can vary by version; this ensures the icon is always shown regardless of the default.

**Next session:** run the app, read `gausslite-startup.log`, identify exactly which step fails or confirm the icon now appears. If the icon appears, strip diagnostics and remove the `StartupLog` calls.

### Fix — Tray icon never appeared (silent failure)
**Root cause:** `Assets\tray-icon.ico` was declared as `<Resource>` in `Gausslite.App.csproj`. The file IS embedded into the WPF `.g.resources` bundle (confirmed by enumerating the bundle — key `assets/tray-icon.ico` was present). However, H.NotifyIcon cannot convert a `BitmapImage` loaded from a pack URI to a native `HICON`; its internal conversion path requires a real file path or a `System.Drawing.Icon`. The conversion throws silently on the WPF dispatcher, the app continues with no icon, and there is no console output.

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
Hooked `AppDomain.CurrentDomain.UnhandledException` and `Application.DispatcherUnhandledException` in `App.xaml.cs`. All unhandled exceptions (including full `InnerException` chain and stack trace) are written to `gausslite-crash.log` in `AppContext.BaseDirectory` AND to Debug output. For dispatcher exceptions, `e.Handled = true` is set so a bad icon load cannot take down the whole app, but the failure IS recorded.

**Known fragility (do not revert):** Using `<Resource>` for .ico files in H.NotifyIcon projects is unreliable — H.NotifyIcon silently fails to create the native icon from a pack-URI BitmapImage. Always use `<Content CopyToOutputDirectory>` + file-path loading for tray icon assets.

**All 39 tests still green (17 Gausslite.App.Tests + 22 Gausslite.Core.Tests). Build 0 errors, 0 warnings.**

### Fix 2 — Win2D AnyCPU build warning
- Added `<Platforms>x64</Platforms>` and `<PlatformTarget>x64</PlatformTarget>` to all five .csproj files (Gausslite.Core, Gausslite.Overlay, Gausslite.App, Gausslite.Core.Tests, Gausslite.App.Tests).
- Updated `.sln` project config mappings to route the `Any CPU` solution platform to `x64` per-project configs; solution-level platform name kept as `Any CPU` so `dotnet build` works without `--arch x64`.
- Build now produces zero warnings; `Microsoft.Graphics.Canvas.dll` lands under `runtimes/win-x64/native/` in the app output.

**All 39 tests still green (17 Gausslite.App.Tests + 22 Gausslite.Core.Tests). Build 0 errors, 0 warnings.**

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
- Added `InternalsVisibleTo(Gausslite.App.Tests)` to `Gausslite.App.csproj`.
- Integration test: `TryCreateForWhatsApp_WhenWhatsAppNotRunning_ReturnsFalseAndNull`
  and `_CalledTwice_NeverThrows` in `tests/Gausslite.App.Tests/Orchestration/`.

### Part 2 — Win2DBlurRenderTarget (concrete GPU render target with shared texture)
- **`Win2DBlurRenderTarget`** (internal sealed, `Gausslite.Core.Blur`):
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
- Added `InternalsVisibleTo(Gausslite.Core.Tests)` to `Gausslite.Core.csproj`.
- GPU-gated tests in `tests/Gausslite.Core.Tests/Blur/Win2DBlurRenderTargetTests.cs`
  (skip when `GraphicsCaptureSession.IsSupported()` is false; CI has no GPU).

### Part 3 — WinRT capture concrete wrappers
- Created `WinRTCaptureFrame`, `WinRTCaptureSession`, `WinRTCaptureFramePool`,
  `WinRTCaptureInterop` in `Gausslite.Core.Capture` so the real `CaptureEngine`
  can be wired up without the GPU-level stubs.

### Part 4 — Composition root
- `App.xaml.cs`: replaced `NullCaptureEngine` / `NullBlurPipeline` with
  real `CaptureEngine` + `BlurPipeline`.  Shared D3D11 device created via
  `D3D11CreateDevice` → QI for `IDXGIDevice` → `WinRTCaptureInterop.CreateDirect3DDevice`.
  `BlurPipeline.Initialize(d3dDevice)` called immediately so Win2D's CanvasDevice
  is ready before any frame arrives.  `NullCaptureEngine` and `NullBlurPipeline`
  retained as `internal` test fakes.

**All 39 tests green (17 Gausslite.App.Tests + 22 Gausslite.Core.Tests). Build 0 errors.**

## Next up

**Reconcile v0.2.0–v0.6.0 milestone descriptions in PLAN.md to match the new sequence agreed in handover (Reading A: v0.3.0 includes screen-share-client process scan AND share-target detection, default-blur-if-uncertain as the activation rule).**

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **OverlayWindow Image element must use `Stretch=Fill` and stretch alignments**; default Image layout produces a 0x0 element which prevents `D3DImage` from ever being painted, even though `D3DImage.PixelWidth/Height` are correct.
- **Eager armed setup:** blur setup starts as soon as the current WhatsApp HWND is known, even if WhatsApp is minimized or occluded. Restore/unocclude is only a `SetWindowPos` move of the already-created overlay HWND.
- **Overlay HWND parking uses offscreen-but-visible coordinates, not `Visibility=Hidden`.** `Visibility=Hidden` was tried and moved heavy setup off the privacy path, but WPF still deferred first layout/paint and caused a roughly 250ms first-show cost. The overlay now stays WPF-visible at `(-32000, -32000)` while armed and moves to WhatsApp bounds when active.
- **Partial-occlusion hide-all behavior accepted for v0.1.0; pixel-region clipping deferred to v0.2.0 alongside region-aware blur.**
- **WindowTracker emits WPF DIP bounds, not physical pixels.** In a `PerMonitorV2` process, `GetWindowRect` returns physical pixels; since `OverlayWindow.SetBounds` writes WPF `Window.Left/Top/Width/Height`, the tracker divides by `GetDpiForWindow(hwnd) / 96.0` before raising `BoundsChanged`.
- **Maximized WindowTracker bounds are normalized before DIP conversion.** Windows can report invisible extended-frame coordinates for maximized windows; the tracker clips maximized rectangles to the monitor work area first, then applies the HWND DPI scale. This keeps overlay bounds aligned with what the user visually sees as the WhatsApp window.
- **Armed blur state is silent for v0.1.0.** If blur is enabled while WhatsApp is missing or minimized, the app waits without showing overlay/capture. User-facing notification is deferred to v0.4.0 settings and should remain optional, silent by default.
- **OverlayWindow must return `HTTRANSPARENT` for `WM_NCHITTEST`** so title-bar drag and other non-client frame operations pass through to WhatsApp while blur is enabled. `WS_EX_TRANSPARENT` alone is insufficient for non-client hit-testing; WPF otherwise reports the overlay as an `HTCLIENT` surface.
- **OverlayWindow sizing must be applied via cached bounds replayed at `SourceInitialized` and after `Show()` because the HWND does not exist when the first tracker bounds can arrive, not just Width/Height property setters before Show.**
- **WhatsApp detection strategy** = match by process name prefix `"WhatsApp"` (case-insensitive) OR window class `"WinUIDesktopWin32WindowClass"` + title contains `"WhatsApp"`, explicitly excluding `msedgewebview2`. Real diagnostic data showed the Store version is now a WinUI 3 app (`WhatsApp.Root` process) with a WebView2 child, not a classic UWP app. The `ApplicationFrameWindow` strategy is dead and removed.
- **Tray library = Hardcodet.NotifyIcon.Wpf** (not H.NotifyIcon.Wpf). Rationale: H.NotifyIcon silently failed to register the icon with the Windows shell notification area on at least one tested machine despite all setup steps succeeding (file on disk, BitmapImage loaded, Icon property set, Visibility forced to Visible — all logged clean). Hardcodet's library is the more mature parent project (H.NotifyIcon is a fork of it) and is known to work reliably across Windows versions.
- Solution pinned to x64 (Win2D requires a concrete platform; ARM64 support deferred to post-v1).
- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension
- `SharpDX.Direct3D9` 4.2.0 chosen for D3D9Ex managed wrappers in Gausslite.Overlay;
  targets netstandard2.0 so it compiles cleanly on net8.0-windows.
- `INativeBlurRenderTarget` placed in `Gausslite.Core.Blur` (not Overlay) to avoid circular
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
