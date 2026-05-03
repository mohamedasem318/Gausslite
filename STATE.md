# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.3.0 — "Knows when to blur"** (next; not yet started)

v0.1.0, v0.1.1, and v0.2.0 are shipped. See PLAN.md for the v0.3.0
milestone definition and CHANGELOG.md for full v0.1.x / v0.2.0
development history. Issue #35 tracks the v0.2.0 known limitation
(internal-divider drags during static periods) for a v0.4.0 fix.

## Last session summary

**2026-05-03 — v0.2.0 clip composition: forced-repaint nudge after bounds change + known limitation documented (branch v0.2.0-clip-composition).**

Smoke test of the prior round (race-safe self-validation + delayed-retry) showed scope-aware clip mostly converges correctly on resize/maximize, but there was a residual case where the user still had to hover the cursor over WhatsApp before the clip updated. Traced to WhatsApp's WGC `contentSize` not always updating in lockstep with the window's `GetWindowRect` — for some snap/resize scenarios, no fresh WGC frame arrives until the user provokes a paint by hovering.

**Fix 5 — `IWindowTracker.RequestRepaintOfTrackedWindow()`.** New `IWin32Api.InvalidateClientArea(hwnd)` wrapping `InvalidateRect(hwnd, NULL, FALSE)` (allowed cross-process). New `IWindowTracker.RequestRepaintOfTrackedWindow()` resolves the tracked HWND via `_profile.FindWindowHandle()` and calls `InvalidateClientArea` on it. `TrayOrchestrator.OnBoundsChanged` calls this after every dispatch (not gated on `OverlaySizeChanged`, so it covers the `OnVisibleRegionChanged`-races-ahead path). The repaint nudge produces a fresh WGC frame within the existing 400 ms delayed-retry window — by the time the retry fires, detection runs on the freshly-captured frame. No more user hover required after maximize/resize/snap.

3 new tests (84 total App + 102 Core): orchestrator calls `RequestRepaintOfTrackedWindow` on every `BoundsChanged`; tracker's `RequestRepaintOfTrackedWindow` calls `InvalidateClientArea` on the resolved HWND; tracker's no-op when the HWND can't be found. All existing tests still green.

**Known limitation — internal-divider drags during static periods.** Internal WhatsApp layout shifts (e.g. dragging the chat-list/conversation divider inside WhatsApp) emit NO `BoundsChanged` event, so the repaint-nudge path doesn't fire. The remaining mechanism is the cadence (re-detect every 30 frames) — but cadence is measured in frame count, not wall time. When WhatsApp's content is static, WGC stops delivering frames; the cadence stops ticking. The new layout becomes visible to detection only when *something* triggers WGC to deliver a frame again — typically the user's cursor hovering over WhatsApp.

The future fix would be a wall-time timer (~3 s) that calls `RequestRepaintOfTrackedWindow` periodically while blur is active — forcing WGC to deliver fresh frames even when WhatsApp's content is static. Deferred from v0.2.0 because it imposes a small steady CPU/battery cost on a privacy-first background app and only addresses an edge case (active blur + WhatsApp window in foreground with no user input + internal layout change).

Test counts: Core 102/102, App 84/84 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

**2026-05-03 — v0.2.0 clip composition: race-safe self-validation + delayed-retry (branch v0.2.0-clip-composition).**

Smoke test of the prior fix exposed two new failure modes the cadence-only model didn't cover.

**Race condition: `OnVisibleRegionChanged` updates bounds before `OnBoundsChanged` sees the size change.** On maximize, both events fire. `OnVisibleRegionChanged` runs first; its `ShowOverlay` → `CacheCurrentOrDefaultBounds` overwrites `_lastKnownBounds` with the new size. When `OnBoundsChanged` finally lands on the UI thread, `previousBounds == newBounds` already → `OverlaySizeChanged` returns false → the size-change clear DOES NOT run → `RecomputeAndApplyClip` runs with new bounds and STALE cached content size, computing a wrong clip (1.457 × 1.260 stretched). Log confirmed.

**Mid-transition first frame.** Even when detection re-runs after maximize, the first WGC frame after maximize captures WhatsApp mid-responsive-layout (chat list at intermediate width). Detection on it produces a transitional rect. Then WhatsApp content goes static, no further frames arrive, and the cadence (every 30 frames) doesn't tick until the user nudges WhatsApp into repainting (e.g. by hovering).

**Fix 1 — self-validating `RecomputeAndApplyClip`.** Top of the method now checks the bounds-to-content ratio against the same envelope as `IsReadbackFrameConsistentWithBounds` (maximized ≈1.000 or windowed ≈1.012/1.009, ±0.03). If inconsistent, clears `_lastDetectionResult` / `_lastContentWidth/Height` and falls back to full-coverage. Catches the race regardless of which event handler updated bounds first. The envelope-check helper is now extracted as `IsScaleRatioConsistent` and shared between readback validation and clip self-validation.

**Fix 2 — debounced delayed RunDetection retry.** New `DelayedUiDispatch` delegate plumbed through the constructor (same testability pattern as `UiThreadDispatch` / `BackgroundDispatch`). On every `OnBoundsChanged`, schedules a `RunDetection` 400 ms later via `DispatcherTimer`. Each new event disposes the prior pending timer (debounce), so a continuous drag fires the retry exactly once at drag end. By the 400 ms mark, WhatsApp's responsive layout has settled and the cached input frame holds the steady-state — re-detection picks up correct rects without waiting for a hover.

**3 new tests** (81 total App): stale-content auto-clear after VisibleRegionChanged race; delayed retry runs `RunDetection` when timer fires; rapid BoundsChanged cancels prior pending retry.

Test counts: Core 100/100, App 81/81 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning. Smoke test for these two specific fixes is pending.

---

**2026-05-03 — v0.2.0 clip composition: stale-frame validation + continuous detection (branch v0.2.0-clip-composition).**

Final two-bug fix on the v0.2.0-clip-composition branch. Three previous fixes did not converge the clip on left-edge resize, maximize, or internal-divider drag. Recon traced the root causes to (1) `RunDetection` writing cached state from stale-frame readbacks during the bounds-changed→post-resize-frame race window, and (2) the one-shot `_detectionDone` gating preventing re-detection after WhatsApp internal layout shifts that emit no `BoundsChanged` event.

**Fix 1 — stale-frame validation in `RunDetection`.** New `IsReadbackFrameConsistentWithBounds` helper validates the readback frame's dimensions against `_lastKnownBounds` before writing `_lastContentWidth/Height/_lastDetectionResult`. The bounds-to-content scale ratio must lie within ±0.03 of the maximized envelope (≈1.000) or the windowed envelope (≈1.012 horizontal, ≈1.009 vertical, the 14×7 px DWM gap). Anything else means the cache holds a pre-resize frame; detection is skipped, no cached state is written, and the privacy-safe full-coverage fallback in `RecomputeAndApplyClip` holds until the next post-resize frame.

**Fix 2 — continuous (cadence-driven) detection.** `_detectionDone` field and its reset paths removed. `OnFrameArrived` now triggers detection when `count == 1 || count % DetectionCadenceFrames == 0` (`DetectionCadenceFrames = 30` ≈ 1 s at 30 fps). `_frameCount` resets in `TearDownCaptureAndOverlay` so each new capture session's first frame triggers detection. Internal-divider drags (no `BoundsChanged` event) get caught within ~1 s. The existing `RunDetection("OnBoundsChanged")` best-effort fast path stays — Fix 1 ensures it doesn't write stale state, and when it succeeds it converges sooner than waiting for the next cadence tick.

**Diagnostic log.** New single-line `clip-compose` log at the join point in `RecomputeAndApplyClip` printing `bounds`, `content` size, `scale` ratios, `captureRect` input, and `scopeRect` output. Pinned out as the single point that lets smoke tests for Layouts B (left-edge resize) and C (maximize) verify the new bounds and the cached content size are paired with the correct ratio.

**5 new tests** (78 total App): stale-frame readback rejected, fresh readback accepted, maximized ratio accepted, cadence-driven detection fires at frames 1 + 30, internal-divider scenario re-detects with updated rects. Updated `BoundsChanged_SizeChange_*` test to reflect the cadence-only convergence model (the rearm test was removed). All existing privacy-invariant + size-change-clears-state tests still pass.

Test counts: Core 100/100, App 78/78 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning. Smoke test pending (manual).

---

**2026-05-03 — v0.2.0 Session B + detector hardening + orchestrator race fix (branch v0.2.0-clip-composition).**

**Orchestrator race fix.** When WhatsApp resizes, `OnBoundsChanged` dispatches at Normal priority while new-size WGC frames may not yet be cached. `RunDetection("OnBoundsChanged")` could read a stale-size frame (or skip entirely), leaving `_lastDetectionResult` and `_lastContentWidth/Height` set for the wrong dimensions. `CaptureToOverlayConverter` would then map old rects through the wrong ratio onto the new overlay size.

Two additions gated on `OverlaySizeChanged` (new helper, same ±1 DIP tolerance as `OverlaySizeGrew`, covers shrink as well as grow):

(a) **Stale-state clear**: on a size-changing BoundsChanged, `_lastDetectionResult`, `_lastContentWidth`, and `_lastContentHeight` are reset before `ApplyVisibilityForCurrentWindow` runs. `RecomputeAndApplyClip` therefore falls back to full-coverage (privacy-safe) while detection is pending. Position-only drags are unaffected.

(b) **Re-arm first-frame detection**: `_detectionDone` is reset to 0 on the same path (without tearing down the capture session). The next successfully blurred frame after the resize triggers detection via the existing `OnFrameArrived` path — a guaranteed post-resize read. The existing `RunDetection("OnBoundsChanged")` stays as the best-effort fast path.

4 new tests: size-change clears result, position-only drag does not clear, re-armed first-frame detection fires, privacy invariant (scoped clip falls back to full coverage while detection is pending).

Test counts: Core 100/100, App 73/73 (x64). Build: 0 errors, 1 pre-existing Win2D warning.

---

**2026-05-03 — v0.2.0 Session B + detector hardening (branch v0.2.0-clip-composition).**

**Detector fix.** `WhatsAppRegionDetector` Phase 1 divider-selection criterion changed from **global maximum** to **leftmost above threshold**. Root cause: in maximized WhatsApp with a narrow chat list (~451 px), the message-bubble max-width boundary (~750 px into the conversation pane, at x≈1201 in a 1920-px frame) produces a secondary vertical edge whose consistent-row count can exceed the real chat-list/conversation divider. The old global-max rule picked the secondary edge; the new leftmost rule stops at the first column reaching minConsistent (70 % of sampled rows) — always the real divider, which is closer to the left edge than any secondary structural column. RTL layouts unaffected (rail-side detection still runs independently after divider detection). Two new regression tests (Tests 12–13): one synthetic two-edge frame where the secondary edge has a higher row count, one maximized-layout LTR frame with a rail. All 100 Core + 69 App tests pass.

---

**2026-05-03 — v0.2.0 Session B: clip composition wiring (branch v0.2.0-clip-composition).**

Consumed `_lastDetectionResult` to drive scope-aware clip composition and tray balloon notifications.

**Coordinate conversion.** New `CaptureToOverlayConverter` static class in `Gausslite.Core/Detection/`. Pure ratio scale (`overlayDip = capturePixel × (overlaySize / contentSize)`) on each axis independently. Zero-size degenerate inputs return `Rect.Empty`. 11 unit tests.

**Content-size cache.** `TrayOrchestrator` gains `_lastContentWidth` and `_lastContentHeight` int fields, written inside `RunDetection` from the `w`/`h` values returned by `TryReadLatestFrameAsBgra`. This keeps the cached dims always in sync with the actual frame used for detection. Both fields cleared in `TearDownCaptureAndOverlay`.

**`RecomputeAndApplyClip`.** Single method replacing `ApplyRegionClip`. Reads visible-region rects (converts from screen DIPs to overlay-local), detection result, content size, and `CurrentScope`. For `Both` or when detection failed/absent: passes visible rects through (with the existing full-coverage null-clip optimization). For `ChatList`/`Conversation`: intersects each visible rect with the converted scope rect; filters zero-area intersections (WPF `Rect.Intersect` returns a non-`Empty` zero-width rect when edges exactly touch). Empty intersection after scope filtering → `SetClip([])` (privacy-safe full-overlay fallback), logged.

**Three triggers.** `ShowOverlay` (formerly called `ApplyRegionClip`), `RunDetection` (after writing `_lastDetectionResult`), and `SetScope` (after writing `CurrentScope`) all call `RecomputeAndApplyClip`. All are UI-thread; no marshaling needed.

**Notifier seam.** New `ITrayNotifier` interface and `NotificationIcon` enum (both `internal`) in `Gausslite.App.Tray`. `TrayNotifier` wraps `TaskbarIcon.ShowBalloonTip` with an `Attach(TaskbarIcon)` setter called from `TrayIconHost.Initialize()`. `TrayOrchestrator.SetTrayNotifier(ITrayNotifier?)` wires the notifier post-construction (avoids the `internal`-type-in-public-constructor accessibility error). `App.xaml.cs` creates `TrayNotifier` after the orchestrator and calls `SetTrayNotifier` before `TrayIconHost.Initialize`. `DynamicProxyGenAssembly2` added to `InternalsVisibleTo` so NSubstitute can mock `ITrayNotifier` in tests.

**Balloon transition tracking.** Four state fields on `TrayOrchestrator`: `_hasEverDetected`, `_detectionWasSucceeding`, `_lastFailureBalloonAt`, `_scopeFallbackBalloonShown`. `UpdateBalloonState(bool)` fires: failure balloon on success→failure (guarded by `_hasEverDetected`); recovery balloon on failure→success if > 30 s since failure balloon (immediate recovery suppressed); scope-fallback info balloon fired from `SetScope` once per session when scope ≠ Both before any detection has ever succeeded.

**Tests.** 11 converter unit tests (Core). 10 clip/trigger tests + 9 balloon transition tests (App). Total: Core 98/98, App 69/69 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

**2026-05-03 — E_NOINTERFACE audit: six sites eliminated across Blur module (branch v0.2.0-detection-plumbing).**

Full codebase audit of `src/Gausslite.Core/Blur/` for the `Marshal.GetObjectForIUnknown +
managed-cast` bug class after a previous session's single-site fix left five more live sites.

**6 sites found and fixed.** Sites A/B/C (`GetD3D11DevicePtr` ×2, `FlushD3D11Context`) used
`(IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(dxgiAccessPtr)` — worked by luck
because `IDirect3DDxgiInterfaceAccess` is a COM tear-off on `IDirect3DDevice` (different
IUnknown from the registered WinRT projection). Sites D/E/F (`Win2DBlurRenderTarget` ctor,
`CreateCachedFrame`, `CreateStagingTexture`) used `(ID3D11Device)Marshal.GetObjectForIUnknown(d3d11DevicePtr)` — throws `InvalidCastException` in production because on a hardware GPU the
`ID3D11Device*` from `GetInterface` shares IUnknown with the registered CsWinRT IDirect3DDevice
projection; in WARP the test device keeps them separate so the WARP integration test missed it.
All 6 sites converted to `CreateTexture2DRaw` (vtable slot 5) and `CallGetInterface` (vtable
slot 3). Both dead `[ComImport]` interface definitions removed from both files.
`Win2DBlurRenderTarget` gained its own `GetInterfaceDelegate`, `CreateTexture2DDelegate`,
`CallGetInterface`, and `CreateTexture2DRaw` helpers.

**Test.** New `AllConvertedCallSites_WhileDeviceIsAlive_DoNotThrow` integration test exercises
all 6 converted paths while the WinRT IDirect3DDevice and IDirect3DSurface projections are alive
in the CsWinRT ComWrappers table. Surface path (TryReadBgra) catches regression in WARP;
device path catches regression on hardware GPU.

**Smoke test.** Zero `InvalidCastException` entries, detection-succeeded on every trigger
with plausible rects, no exceptions from any call site.

Test counts: Core 87/87, App 50/50 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

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

**v0.2.0 has shipped** (PR #34 squash-merged to main, branch deleted, tagged `v0.2.0`). Issue #35 tracks the documented known limitation. Roadmap from here:

- **v0.3.0 — "Knows when to blur".** Auto-activation: detect screen-share clients (Zoom, Teams, Meet, Discord, OBS) and which monitor/window they're capturing. Blur activates within 2 s of share start, deactivates within 2 s of share end. Privacy-first fallback: blur enabled when share-target detection is uncertain. See PLAN.md for full milestone definition.
- **v0.4.0 — "Polish".** Settings window with persistence; continuous blur-radius slider (replaces the fixed Light/Medium/Heavy presets, or adds a fourth "Custom" option); auto-start with Windows; opt-in armed-state notification toggle; opt-in toggle for the wall-time forced-repaint timer that closes issue #35.
- **v0.5.0 — "Notifications too".** Toast-notification blur during screen sharing.
- **v1.0.0 — Indirect Display Driver.** The big architectural shift: blur appears only in the shared stream, real monitor stays untouched.
- `RegionDump` annotation fix (separate Codex task, still pending; orthogonal to the milestones above).

All pre-existing v0.2.0 work (occlusion clipping, intensity presets, edge-fade fix, region scope
submenu, scope-aware clip composition) remains working and unchanged.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **2026-05-03 — Bounds change forces tracked window to repaint via `InvalidateRect`.**
  Some bounds changes (snap resizes in particular) produce no fresh WGC frame on their
  own — without intervention the user has to hover over the captured app to provoke a
  paint. `IWindowTracker.RequestRepaintOfTrackedWindow()` calls `InvalidateRect(hwnd,
  NULL, FALSE)` cross-process; the resulting paint produces a fresh WGC frame the
  delayed-retry detects on. Called on every `OnBoundsChanged` (not gated on size change)
  so racy paths benefit too.

- **2026-05-03 — `RecomputeAndApplyClip` self-validates cached content size against bounds.**
  Multiple event handlers update `_lastKnownBounds` (`OnBoundsChanged` AND `OnVisibleRegionChanged`
  via `CacheCurrentOrDefaultBounds`); when the visible-region handler lands first on a resize,
  the bounds-change handler sees `previousBounds == newBounds` and never fires its size-change
  clear. The clip-recompute method now applies the same envelope check used by readback
  validation (≈1.000 maximized or ≈1.012/1.009 windowed, ±0.03) and auto-clears stale state
  centrally. The envelope check is extracted as `IsScaleRatioConsistent` for reuse.

- **2026-05-03 — Bounds change schedules a debounced delayed `RunDetection` ~400 ms later.**
  Even when the immediate post-resize detection succeeds, the first WGC frame typically
  captures WhatsApp mid-responsive-layout. Without a delayed retry the user has to hover
  over WhatsApp to nudge it into delivering a fresh frame. The retry uses a `DispatcherTimer`
  via the new `DelayedUiDispatch` delegate (testable seam matching the existing
  `UiThreadDispatch` / `BackgroundDispatch` pattern). Each new `BoundsChanged` cancels the
  prior pending timer, so a continuous drag fires exactly one retry at drag end.

- **2026-05-03 — Detection runs continuously on a frame-cadence, not as one-shot.**
  `OnFrameArrived` triggers detection on the first frame of each capture session and then
  every `DetectionCadenceFrames` (30) frames thereafter. The previous `_detectionDone`
  one-shot gating could not catch internal layout shifts inside WhatsApp (e.g. dragging
  the chat-list/conversation divider) because they emit no `BoundsChanged` event. Cadence
  is the only general-purpose backstop for any layout change the bounds-tracker can't see.
  Latency budget: ~1 s at 30 fps, which meets the smoke-test "within ~1 second" spec.

- **2026-05-03 — `RunDetection` rejects readback frames that don't match current bounds.**
  `IsReadbackFrameConsistentWithBounds` checks the bounds-to-content scale ratio against
  the maximized (≈1.000) or windowed (≈1.012/1.009) envelope ±0.03. Stale-frame readbacks
  during the bounds-change → post-resize-frame race window are skipped — no cached state
  is written. The privacy-safe full-coverage fallback in `RecomputeAndApplyClip` holds
  until the next matching-size frame arrives via `OnFrameArrived`. This is the targeted
  patch for the "ratio of 1.596 instead of 1.000 on maximize" failure mode.

- **2026-05-03 — Detection state cleared on size-changing BoundsChanged.**
  `TrayOrchestrator` resets `_lastDetectionResult` / `_lastContentWidth/Height` whenever
  `OverlaySizeChanged` is true. Prevents stale capture-pixel rects from being mapped
  through the wrong ratio onto the resized overlay. Position-only drags are not affected
  (< 1 DIP tolerance, same as `OverlaySizeGrew`). With the cadence-driven detection above,
  no explicit "re-arm" of a one-shot flag is needed.

- **2026-05-03 — Divider selection uses leftmost-above-threshold, not global maximum.**
  `WhatsAppRegionDetector` Phase 1 stops at the first column in the plausible range
  whose consistent-row count reaches minConsistent (70 %), scanning left-to-right.
  In WhatsApp's layout the real chat-list/conversation divider is always the leftmost
  strong edge; secondary structural columns (bubble max-width boundary, context-panel
  edge) appear further right and can outscore the real divider under a global-max rule.

- **2026-05-03 — Vtable-only rule for D3D11/DXGI interop in `Gausslite.Core/Blur`.**
  All D3D11/DXGI interop in this module uses raw vtable dispatch. Never `Marshal.GetObjectForIUnknown` + managed cast on these pointers — CsWinRT's ComWrappers table will return the registered managed projection on hardware GPU (not WARP), which has no entry for private COM interfaces and throws E_NOINTERFACE. Any new D3D interop must follow the existing vtable-dispatch helpers and ship with a hardware-path test (the WARP test runner doesn't reproduce the bug class).

- **2026-05-03 — Vtable-only rule for private COM interfaces in the Blur module.**
  `Marshal.GetObjectForIUnknown + managed cast` is banned for any private COM interface
  (`ID3D11Device`, `IDirect3DDxgiInterfaceAccess`, `ID3D11Texture2D`, etc.) in
  `src/Gausslite.Core/Blur/`. These interfaces are not in Windows metadata; CsWinRT does
  not project them, but `GetObjectForIUnknown` can return a CsWinRT projection if the native
  pointer happens to share IUnknown with a registered WinRT object — producing an
  `InvalidCastException` that is timing/hardware-dependent and not reliably caught in WARP
  tests. All access must use raw vtable dispatch. Grep enforcement: zero
  `GetObjectForIUnknown` call sites remain in `src/Gausslite.Core/Blur/`.

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