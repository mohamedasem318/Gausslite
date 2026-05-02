# Gausslite — Session history

> Verbose archive of session-by-session development notes. Reference-only.
> For current state and forward-looking work, see STATE.md.
> For user-facing release notes, see CHANGELOG.md.

## Session history

### 2026-05-02 — RTL rail-side detection (issue #30)

**Root cause correction.**
The original session notes described issue #30 as "the narrower-side-equals-chat-list
heuristic fails in wide mode." That was imprecise. Reading the code: `Detect()` always
assigned `chatListRect` to the left panel, with no width comparison at all. Wide-mode LTR
was never broken — the detector happened to be right because in LTR WhatsApp the chat
list is always on the left, regardless of width. The actual failure was RTL layouts
(Arabic, Hebrew), where WhatsApp mirrors the entire UI and the chat list moves to the
right side of the divider.

**First attempt and why it failed.**
The first implementation measured horizontal-edge density in the outermost 15% strips.
The rail side was expected to have denser icon edges than the conversation side. In practice
this was inverted on real WhatsApp: an active conversation (chat bubbles, input bar,
wallpaper) has far more horizontal edges than the sparse navigation rail. The conversation
strip outscored the rail strip on every LTR frame tested.

**Signal choice — vertical uniformity walk.**
WhatsApp's navigation rail is the **outermost strip of solid uniform background** before
chat content begins. Its columns are vertically uniform: the same background color repeats
from top to bottom. Chat-list content (contact rows, avatars, timestamps) follows
immediately after the rail, and has much higher row-to-row pixel variation. The conversation
pane's outer edge may be quiet (empty background, no message bubbles near the frame
boundary), but unlike the rail side it has no fixed chat-list boundary to mark.

The algorithm: walk inward from each outer edge column-by-column (skipping the top 30 px
title-bar area and the outermost 5 px border). For each column, count sampled rows where
`RowDelta(x, y) > EdgeStrengthThreshold` — the vertical-edge strength (L1 B+G+R delta
between adjacent rows at column x). When this count reaches `busyThreshold` (25% of
effective rows), that column is the first "content" column; the number of quiet columns
before it is the rail-width estimate for that side.

**Key invariant: default is 0, not maxSearch.** If no content column is found within the
search range, the estimate defaults to 0 (no evidence), not to the search limit. This
prevents a featureless conversation background that exhausts the search range from
impersonating a wide quiet zone. A genuine rail always has a chat-list boundary within
the search range; a flat background that runs to the limit scores 0.

The side with the larger estimate is the rail side. Ties → LTR (Left) fallback.

**Search range.** `maxSearch = min(200 px, 40% × frameWidth)`. The 200 px ceiling is
necessary because the rail + chat-list margin is ~92 px regardless of window width. A
fraction-only limit (e.g. 10%) would give only 80 px on an 806 px frame — too short to
reach the first content column. The 40% cap ensures the scan never crosses the divider.

**Validation on live WhatsApp.**
`tools/RegionDump` was extended with a `Rail side: (left=Xpx, right=Ypx)` diagnostic line.
Results across six captures (LTR English + RTL Arabic, default/narrow/wide each):

| Layout | Frame | Rail side | Left quiet | Right quiet |
|---|---|---|---|---|
| LTR default | 1269×793 | **Left** ✓ | 92 px | 0 px |
| LTR narrow | 806×793 | **Left** ✓ | 92 px | 0 px |
| LTR wide | 1886×993 | **Left** ✓ | 92 px | 0 px |
| RTL default | 1269×793 | **Right** ✓ | 0 px | 95 px |
| RTL narrow | 806×793 | **Right** ✓ | 0 px | 95 px |
| RTL wide | 1886×993 | **Right** ✓ | 0 px | 95 px |

Rail-width estimate is consistent at 92–95 px across all window sizes, confirming the
signal is stable. Annotated PNGs verified visually: green CHAT LIST box on the correct
side (left in LTR, right in RTL) in all six captures.

**Implementation.**
`DetermineRailSide(pixels, stride, frameWidth, frameHeight)` private static method added
to `WhatsAppRegionDetector`. `RowDelta(x, y)` helper measures L1 B+G+R delta between
(x, y-1) and (x, y) — the symmetric transpose of the existing `ColumnDelta` helper.
`RailSide` enum (`Left`, `Right`) added as a new type in `Gausslite.Core.Detection`.
`RegionDetectionResult` gains `DetectedRailSide`, `RailSideLeftWidth`, `RailSideRightWidth`
(the raw estimates for diagnostics and downstream confidence checks). The `Fail()` helper
returns defaults (`Left`, both widths 0) without change.

**Tests.**
Four new tests in `WhatsAppRegionDetectorTests.cs` (11 Detection tests total). Synthetic
frames use vertically uniform columns for the rail (no row-to-row variation) and
10-row-period alternating bands for content (maximum row-to-row variation at the sample
stride), so each test directly exercises the signal the algorithm relies on.

- `Detect_RailOnLeft_AssignsChatListToLeft` — 50 px uniform rail on left; asserts
  `leftWidth > rightWidth` and `ChatListRect.X == 0`.
- `Detect_RailOnRight_AssignsChatListToRight` — 50 px uniform rail on right; asserts
  `rightWidth > leftWidth`, `ChatListRect.X == 400`, `ConversationRect.X == 0`.
- `Detect_NoRailSignalEitherSide_DefaultsToLeft` — content starts immediately on both
  edges; asserts `leftWidth == rightWidth` (tie → LTR fallback).
- `Detect_RailOnLeft_WithRightScrollbar_StillAssignsChatListToLeft` — 50 px uniform rail
  left, 5 px uniform scrollbar right; asserts `leftWidth(50) > rightWidth(5)` → Left.

Test counts: Core 79/79, App 46/46 (x64). Build: 0 errors, 0 warnings.

---

### 2026-05-02 — Region detection arc: UIA recon, CV detector, smoke tool

**UiaDump recon.**
`tools/UiaDump/` is a standalone C# console utility that walks WhatsApp's full
UI Automation tree and prints every element (control type, name, bounding rect) to
stdout. The goal was to determine whether WhatsApp Desktop exposes enough UIA
structure to locate the chat-list and conversation panes programmatically.

Finding: WhatsApp Desktop is a WebView2 shell. The UIA tree contains a root WinUI 3
frame and a single `Document` element of type `WebView2`, and nothing beneath it —
no `List`, no `ListItem`, no named panes, no text content. The WebView2 boundary is
opaque to UIA; all chat UI is rendered in the web layer and invisible to the
accessibility tree. The implication is unambiguous: the original v0.2.0 plan of
"UIA primary, CV fallback" cannot be executed as described. UIA cannot provide even
a coarse region on the current WhatsApp build.

**Pivot decision — UIA to CV.**
The original spec allowed for a CV fallback if UIA failed partially. Recon showed UIA
fails completely — not partially. Keeping a UIA primary path alongside CV would mean
shipping untestable dead code. Decision: drop the UIA path entirely; make
`WhatsAppRegionDetector` CV-only from the start. The fallback case never exists on
the current build, so there is nothing to fall back from.

**WhatsAppRegionDetector.**
`src/Gausslite.Core/Detection/WhatsAppRegionDetector` is a pure-C# detector with no
third-party CV dependencies. It receives a BGRA frame (the same type already produced
by the WGC capture path) and returns two `Rect` values — one for the chat-list pane,
one for the conversation pane — in window coordinate space.

Algorithm: sample three evenly-spaced rows in the middle vertical band of the frame.
For each row, compute horizontal pixel-to-pixel luminance differences and find the
column with the highest contrast transition (the vertical divider between the two
panes). Take the median of the three per-row peak columns as the consensus divider
position. Assign the left rect as chat-list and the right rect as conversation based
on a width heuristic (narrower side = chat list, wider side = conversation).

Implementation notes: operates on captured frames already in memory; no file I/O, no
COM interop, no additional dependencies. 7 unit tests covering divider detection,
frame edge cases, and rect output, all passing.

**RegionDump smoke tool.**
`tools/RegionDump/` is a standalone executable that captures one WhatsApp frame via
the existing `CaptureEngine` path, instantiates `WhatsAppRegionDetector`, runs it on
the captured frame, and saves two files: `raw.png` (the unmodified capture) and
`annotated.png` (the capture with the detected divider column and the two region rects
drawn as overlaid rectangles). Intended for visual regression testing — run it, inspect
the annotated PNG, confirm the divider is in the right place.

Verified on three layouts: default (balanced panes), narrow (WhatsApp window at minimum
width), and wide (WhatsApp window at near-full-screen width). Divider detection was
correct on all three. The annotated PNGs were eyeball-verified.

**Known limitation — assignment heuristic, filed as issue #30.**
The "narrower side = chat list" heuristic fails in wide mode. At near-full-screen width
the conversation pane expands significantly and becomes the wider side; the heuristic
then assigns it as chat-list and the chat-list column as conversation. This is a
mis-identification, not a detection failure — the divider column itself is found
correctly; only the labelling is wrong.

Filed as https://github.com/mohamedasem318/Gausslite/issues/30. Proposed fix:
horizontal-edge density analysis — the chat-list pane has dense horizontal edges
(contact separators, avatars, timestamp text) while the conversation pane has sparser
edge structure. A density comparison would produce a robust label independent of pane
width ratios.

**Wiring deferred.**
`WhatsAppRegionDetector` is not connected to `BlurPipeline`, `OverlayWindow`, or the
tray "Blur region" submenu. The submenu remains a no-op. Wiring was deliberately
deferred rather than shipping region-aware blur that mis-identifies panes in wide mode.
The decision: it is better to hold the feature until issue #30 is resolved than to ship
behaviour that makes the wrong pane blurry. Region-aware blur is an optional v0.2.0
extension; it can also be deferred to a later milestone if issue #30 turns out to be
more involved than expected.

Test counts: Core 68/68, App 46/46 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

### 2026-05-02 — Occlusion false-positive filters + placeholder flash fix

Three bugs discovered via smoke test after the occlusion clipping landed.

**Bug 1 — Title-bar "notch" (same-process filter).**
WhatsApp (WinUI 3) creates an `InputNonClientPointerSource` top-level HWND that sits directly
above the main HWND in Z-order and is positioned exactly over the title-bar area (~32px tall,
full width). `ComputeVisibleRegion` was walking past it, subtracting its rect, and producing
a clip that blacked out the title bar — the "macbook notch" appearance. Identified by reading
the startup log: `visible region changed to 0 rect(s)` on first WhatsApp detection before any
user app was covering it. Fix: skip windows whose process ID matches `whatsappHwnd`'s PID.
New `IWin32Api` method: `GetWindowProcessId` → `GetWindowThreadProcessId` in `Win32Api`.
New test: `ComputeVisibleRegion_SkipsSameProcessWindows`.

**Bug 2 — Spurious clip patches during movement (`WS_EX_TOOLWINDOW` filter).**
System UI windows — specifically `explorer`'s `TopLevelWindowForOverflowXamlIsland` (system
tray overflow), taskbar strips, and DWM helper HWNDs — are visible, non-minimized, different-
process windows that sit above normal windows in Z-order. Their rects overlapped WhatsApp's
bounds at various positions as it was dragged, producing 3-rect visible regions and fragmented
clips. Confirmed in the log: `visible region changed to 3 rect(s)` during movement even with
no user app covering WhatsApp. Fix: skip windows with `WS_EX_TOOLWINDOW` in their extended
style. New `IWin32Api` method: `GetWindowExStyle` → `GetWindowLong(GWL_EXSTYLE)` in `Win32Api`.
New test: `ComputeVisibleRegion_SkipsToolWindows`.

**Bug 3 — Solid-colour flash while dragging (placeholder threshold fix).**
`BoundsOutgrewLastBlurredFrame` compared DIP overlay width (`bounds.Width = 1294`) against
the WGC physical-pixel frame width (`_lastBlurredFrameWidth = 1280`). The 14 px structural
gap between GetWindowRect (full window including extended frame) and WGC ContentSize (content
area only) always exceeded the `+1` threshold. The placeholder was shown on EVERY `BoundsChanged`
event — ~30 times per second during a drag — and then quickly hidden by the next arriving WGC
frame (~9 ms per the log). The dark background colour was briefly visible each time.
Fix: replaced `BoundsOutgrewLastBlurredFrame` with `OverlaySizeGrew(newBounds, previousBounds)`
which captures the previous DIP bounds before overwriting `_lastKnownBounds` and compares DIP
sizes directly. A pure position change keeps the same DIP width/height → returns false → no
placeholder. An actual WhatsApp resize increases the DIP size → returns true → placeholder
shown as before. The three dead fields (`_lastBlurredFrameWidth`, `_lastBlurredFrameHeight`,
`_lastBlurredFrameTimestamp`) were removed from `TrayOrchestrator`.

Test counts: Core 68/68, App 46/46 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

### 2026-05-02 — Edge-fade fix + true-region occlusion clipping

**Task 1 — Blur edge-fade (systematic debugging).**

Hypothesis from STATE.md was confirmed without needing to run the app:
`GaussianBlurEffect.BorderMode = Soft` (Win2D/D2D default) samples out-of-bounds pixels as
`(0,0,0,0)` transparent, so the blur output fades to transparent within `BlurRadius` pixels of
every edge of the render target. The 14 × 7 px mismatch between WGC ContentSize and overlay window
size means the faded edges are stretched and more visible. `BorderMode = Hard` was tried in the
previous session and reverted because it repeated the exact edge pixel, producing a crisper-than-interior
ring (the WGC edge pixels carry window chrome artifacts).

The correct fix: pad the source before blurring so the fade zone falls outside the output region.

Implementation: `Win2DBlurInterop.DrawPaddedBlur` (private static). For each call to `DrawBlur` or
`DrawBlurFromCache`:
1. Compute `pad = ceil(radius)` (minimum to push the Gaussian tail fully outside the original bounds).
2. Allocate a temporary `CanvasRenderTarget` of `(W + 2·pad) × (H + 2·pad)` pixels at 96 dpi
   (no `D3D11_RESOURCE_MISC_SHARED` needed — intermediate texture).
3. Draw the source into the padded texture via `BorderEffect.Clamp + DrawImage(sourceRect: (-pad,-pad,W+2pad,H+2pad))`.
   This maps the clamped border content (infinite-extent, via Clamp mode) into the padding band in one
   GPU draw call, without needing explicit strip-by-strip copies.
4. Apply `GaussianBlurEffect` to the padded source.
5. Draw `sourceRect: (pad,pad,W,H)` → `destRect: (0,0,W,H)` into the final D3D11 shared render target,
   discarding the now-faded padding border.

Per-frame intermediate texture allocation is accepted as negligible overhead. First-frame diagnostic
log added to `TrayOrchestrator.OnFrameArrived` (count == 1) to capture WGC ContentSize, overlay DIP
bounds, and BlurRadius for post-mortem verification in production.

**Task 2 — True-region occlusion clipping.**

Replaced the v0.1.0 center-point hide-all behavior with pixel-accurate partial occlusion clipping.

*Design*: `IWindowTracker` API change: `OcclusionChanged: EventHandler<bool>` + `bool IsOccluded`
→ `VisibleRegionChanged: EventHandler<IReadOnlyList<Rect>>` + `IReadOnlyList<Rect>? VisibleRegion`.
The region is in screen DIP coordinates. Empty = fully occluded (hide). Single full-bounds rect =
fully visible (no clip). Partial rects = apply WPF clip.

*Region computation*: `WindowTracker.ComputeVisibleRegion` (internal static, testable without the
poll loop). Starts with `[whatsappRect]`, then walks Z-order above WhatsApp upward using
`GetWindow(hwnd, GW_HWNDPREV)` via the new `IWin32Api.GetPreviousWindow`. For each visible,
non-minimized covering window, calls `SubtractRect` which produces up to 4 non-overlapping sub-rects
per overlap (top band, bottom band, left strip, right strip at intersection height). Overlay HWND
compared and skipped. Early exit when result is empty (fully occluded). New `IWin32Api` methods:
`GetPreviousWindow` and `IsWindowVisible`; `Win32Api` implements both as one-liners.

*Overlay clip*: `IOverlayWindow.SetClip(IReadOnlyList<Rect>?)`. `OverlayWindow` builds a frozen
`GeometryGroup` of `RectangleGeometry` instances and sets it on `_contentRoot.Clip`.
Null clears the clip.

*Orchestrator*: `OnOcclusionChanged` → `OnVisibleRegionChanged`. `ShowOverlay` now always calls
`ApplyRegionClip` after `MoveToBounds`, converting screen-DIP rects to overlay-local coordinates
by subtracting the overlay's top-left. `HideOverlay` clears the clip. `HasVisibleRegion()` helper
replaces `IsOccluded` property checks throughout.

*Tests*: 7 new tests for `ComputeVisibleRegion` (no-covering-window, full coverage, right-half
covered, top-right corner L-shape, overlay skip, minimized skip, invisible skip) + 1 integration
test for `VisibleRegionChanged` event transitions. 3 old `IsOccludedAtCenter` / `OcclusionChanged`
tests replaced. All TrayOrchestratorTests updated to use `VisibleRegion`.

Test counts: Core 66/66, App 46/46. Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

### 2026-05-02 — Idle repaint confirmed + fixed; WGC border suppressed; TFM 22621

**Context.** The blur intensity preset submenu had shipped in a prior session, but
switching presets while WhatsApp was idle produced no visible change (#23). Multiple
previous sessions had investigated this and provisionally closed it as a "known issue"
(WGC only delivers frames on content change). This session re-opened it with a full
log-based diagnostic.

**Root cause — intensity not visible (Bug A).**

The `TryRenderCurrentFrame` on-demand path runs entirely on the WPF UI thread (no
dispatcher hop). Win2D submits D3D11/D2D1 commands synchronously, but the D3D11 UMD
(user-mode driver) is free to batch CPU-side commands before submitting them to the
GPU command queue. Without an explicit `ID3D11DeviceContext::Flush()` the UMD held
those commands in its internal buffer. `D3DImageBridge` then created the D3D9Ex
shared-surface wrapper and handed it to WPF — before the GPU had seen the new blur
commands. WPF composited the previous frame, silently.

This appeared to work for regular capture frames because the multi-thread dispatch
from the WGC callback (background thread) through `Dispatcher.Invoke` to the UI
thread adds ~0.5–1 ms of latency, during which the UMD typically auto-submits.

Diagnostic evidence: `CompositionTarget.Rendering` fired 5 times within ~80 ms of
`InvalidateVisual`, confirming WPF's compositor was healthy. The diagnostic
`DiagnosticOverlayEnabled = true` flag (red 200×200 rectangle) had been added in a
prior session; the user confirmed it was also not visible, ruling out renderer timing
as the cause and pointing squarely at stale GPU content.

**Fix (Bug A).** Added `IBlurInterop.FlushDevice(IBlurCanvasDevice)` to the interop
interface. `Win2DBlurInterop.FlushDevice` implements it via raw vtable dispatch:
1. QI `IDirect3DDxgiInterfaceAccess` from the WinRT device wrapper.
2. `GetInterface(IID_ID3D11Device)` → vtable slot 40 (`GetImmediateContext`) → get
   `ID3D11DeviceContext*`.
3. Vtable slot 111 (`Flush`) on the context pointer.
`BlurPipeline.TryRenderCurrentFrame` calls `FlushDevice` after all Win2D drawing
sessions complete, immediately before returning `_renderTarget` for `PresentFrame` to
bridge into D3D9Ex. The `DiagnosticOverlayEnabled` constant and its dead `if` block
were removed.

**Root cause — yellow capture border (Bug B).**

`GraphicsCaptureSession.IsBorderRequired` defaults to `true`. Windows 11 draws a
yellow/amber system border around any window being screen-captured to notify the user.
The code never set it to `false`.

**Fix (Bug B).** Added `bool IsBorderRequired { set; }` to `ICaptureSession`.
`WinRTCaptureSession` proxies it to `_session.IsBorderRequired`. `CaptureEngine.Start`
sets it `false` before `StartCapture`. Because this API requires Windows 11 22H2
(build 22621), all five `.csproj` files were bumped from `net8.0-windows10.0.19041.0`
to `net8.0-windows10.0.22621.0`.

**Root cause — washed/faded blur edges (Bug C).**

`GaussianBlurEffect.BorderMode` defaults to `Soft`, which treats out-of-bounds kernel
samples as transparent. For a 1280 × 765 capture blurred into a 1280 × 765 render
target, the last ~`BlurRadius` pixels on each side fade to transparent rather than
showing a naturally blurred edge color. This is then stretched (via `Image.Stretch =
Fill`) to fill the 1294 × 772 overlay window (the 14 × 7 px size difference comes from
WindowTracker reporting full HWND bounds while WGC captures only the client content
area). Result: visible gradient fringe on all four sides of the overlay.

An attempt was made to fix this with `EffectBorderMode.Hard` (clamps/mirrors edge
pixels into the blur kernel). It eliminated the fade but created a different artifact:
at heavy blur radii the repeated-pixel clamping made the boundary look visually
"harder" (crisper/less blurry) than the interior, which also didn't match intensity
scaling. `Hard` mode was reverted. The correct fix — padding the source texture with
a clamped border of width ≥ `BlurRadius` before blurring — is deferred.

**Files modified:**
- `src/Gausslite.Core/Capture/ICaptureSession.cs` — `IsBorderRequired { set; }`
- `src/Gausslite.Core/Capture/WinRTCaptureSession.cs` — proxy to WGC session
- `src/Gausslite.Core/Capture/CaptureEngine.cs` — set `IsBorderRequired = false`
- `src/Gausslite.Core/Blur/IBlurInterop.cs` — `FlushDevice` method
- `src/Gausslite.Core/Blur/Win2DBlurInterop.cs` — `FlushDevice` + vtable Flush impl
- `src/Gausslite.Core/Blur/BlurPipeline.cs` — call `FlushDevice`, remove diag flag
- `src/Gausslite.Core/Gausslite.Core.csproj` — TFM 19041 → 22621
- `src/Gausslite.Overlay/Gausslite.Overlay.csproj` — TFM bump
- `src/Gausslite.App/Gausslite.App.csproj` — TFM bump
- `tests/Gausslite.Core.Tests/Gausslite.Core.Tests.csproj` — TFM bump
- `tests/Gausslite.App.Tests/Gausslite.App.Tests.csproj` — TFM bump
- `tests/Gausslite.Core.Tests/Blur/BlurPipelineTests.cs` — `FlushDevice` assertions

**Test counts:** Core 62/62, App 46/46 (x64). Build: 0 errors, 1 pre-existing
Win2D AnyCPU warning (harmless, documented in csproj).

---

### 2026-05-01 — Tray menu region scope submenu (UI scaffold)

**What shipped.** `BlurRegionScope` enum (`ChatList`, `Conversation`, `Both`) added to
`Gausslite.Core/Blur/` alongside `BlurIntensityPreset`. `ITrayOrchestrator` gains
`CurrentScope` (get) and `SetScope(BlurRegionScope)`. `TrayOrchestrator` implements both:
`CurrentScope` defaults to `Both`, `SetScope` stores the value and logs; no pipeline
interaction (the scope is a no-op until RegionDetector lands). `TrayIconHost` adds a
"Blur region" submenu with "Chat list", "Conversation", and "Both" items, following the
exact same pattern as the existing "Blur intensity" submenu (checkmark tracking, click
handler). Four new `TrayOrchestratorTests` cover default scope and all three `SetScope`
transitions; no `BlurPipeline` mock expectations since the pipeline is not touched.

Test counts: Core 62/62, App 46/46 (x64). Build: 0 warnings, 0 errors.

---

### 2026-05-01 — Blur intensity on-demand repaint investigation (multi-session arc)

**What shipped.** The blur intensity preset feature is complete: `BlurIntensityPreset`
enum (Light / Medium / Heavy), tray menu "Blur intensity" submenu with checkmark tracking,
runtime-configurable `BlurPipeline.BlurRadius` (`volatile float` backing field), and an
input-frame cache (`ICachedFrame` / `Win2DCachedFrame`) that supports on-demand re-render
via `TryRenderCurrentFrame()`. `D3DImage.AddDirtyRect(full rect)` and
`Image.InvalidateVisual()` are in place on every present. Diagnostic logging covers the
full re-render path. The feature works correctly while WhatsApp has active cursor input.

**The bug.** After the preset submenu shipped (2026-04-30 session), switching intensity
while WhatsApp was fully idle — sitting on screen with no cursor movement — had no visible
effect. The investigation arc below documents what was tried in order.

**Session 1 — Volatile fix (wrong hypothesis).** Initial investigation concluded the bug
was a stale-read of `BlurRadius` from the frame-processing thread. Made the backing field
`volatile`, added cross-thread tests. The fix appeared to work in testing. It did not
address the actual cause.

**Session 2 — Correct root cause identified.** Systematic testing confirmed cursor
movement always applies the new radius; idle periods of any duration do not. Root cause:
Windows Graphics Capture only delivers frames when the captured window's content changes.
WhatsApp idle → no WGC frames → `OnFrameArrived` never fires → the radius change is
never rendered. Fix: input-frame cache (`ICachedFrame` / `Win2DCachedFrame`) so
`TryRenderCurrentFrame()` can re-render from a cached surface without a new WGC frame.
`TrayOrchestrator.SetIntensity` calls `TryRenderCurrentFrame` + `PresentFrame`
immediately after writing the new radius.

**Session 3 — Diagnostic logging.** Instrumented `SetIntensity`, `TryRenderCurrentFrame`,
`UpdateD3DImage`, and `PresentFrame` with `gausslite-startup.log` entries. Confirmed the
chain executes on every preset change.

**Session 4 — WPF compositor repaint fix.** Logs confirmed the re-render chain ran but
no screen update appeared. `Image.InvalidateVisual()` was missing from
`OverlayWindow.PresentFrame`. Without it, WPF's render thread does not schedule a render
pass for on-demand presents (the natural 60fps capture path keeps the render thread busy
and was unaffected). Added `InvalidateVisual()` after `_bridge.UpdateD3DImage()`. Tested
and appeared to resolve the issue.

**Session 5 — Stale-exe build path (false negatives).** During this arc, at least one
session retested a stale Release binary that did not include the latest changes. This
produced false negatives — the fix appeared broken, but the correct binary had not been
run. Correct exe: `bin\x64\Release\net8.0-windows10.0.19041.0\Gausslite.App.exe`.

**Session 6 — Compositor timing diagnostics.** Added a `CompositionTarget.Rendering`
subscription in `OverlayWindow.PresentFrame` that logs elapsed time for the first 5
compositor ticks after each on-demand present. Logs showed the compositor fires within
4–10 ms of `InvalidateVisual`, 5 render passes complete within 70 ms. Compositor
scheduling is healthy.

**Session 7 — Confirmed partial fix only.** With the correct binary and compositor
confirmed healthy, retested against a genuinely idle WhatsApp: switching presets still
produced no visible update until cursor movement. `AddDirtyRect` + `InvalidateVisual`
are sufficient for the natural 60fps path; they are not sufficient to force a DWM pixel
update during idle periods when WhatsApp emits no WGC frames. Why `InvalidateVisual`
does not produce a visible screen update in this specific condition — despite the
compositor running — is not fully understood.

**Decision.** Ship as a known issue. Workaround: move the cursor over WhatsApp after
changing a preset. Deferred to v1.0 IDD architecture; the IDD compositor renders
continuously to a phantom framebuffer and does not depend on WGC frame delivery.

**Files created:** `ICachedFrame.cs`, `Win2DCachedFrame.cs`

**Files modified:** `IBlurInterop.cs`, `Win2DBlurInterop.cs`, `IBlurPipeline.cs`,
`BlurPipeline.cs`, `TrayOrchestrator.cs`, `App.xaml.cs`, `OverlayWindow.cs`,
`BlurPipelineTests.cs`, `TrayOrchestratorTests.cs`

**Final test counts:** Core 62/62, App 42/42 (x64). Build: 1 pre-existing Win2D warning, 0 errors.

---

### 2026-04-30 — Runtime-configurable blur radius + tray intensity submenu (v0.2.0)

**Goal:** Two coupled items from the v0.2.0 milestone: make `BlurPipeline`'s blur
radius runtime-configurable (it was hardcoded at 20 DIPs), and expose three presets
(Light / Medium / Heavy) via a new tray menu submenu. Default stays at Medium = 20 DIPs
to match the previous hardcoded behavior. No persistence — restarting resets to Medium.

**Design decisions:**

The pipeline's `BlurRadius` property already existed as a plain auto-property with no
threading consideration. Since it's written from the WPF UI thread (tray click →
`TrayOrchestrator.SetIntensity`) and read on the frame-processing background thread
(`BlurPipeline.BlurFrame` is called from `TrayOrchestrator.OnFrameArrived` which is on
a thread-pool thread), a `volatile` backing field was introduced. `volatile float` is
legal in C# (float is ≤32 bits on the allowed-types list). This prevents the JIT from
caching the value in a register between frame invocations and is consistent with how
`TrayOrchestrator` already uses `Interlocked` for all its own cross-thread fields.

`BlurIntensityPresets` is the single source of truth: `LightRadius = 10f`,
`MediumRadius = 20f`, `HeavyRadius = 35f`. `BlurPipeline.DefaultBlurRadius` now
`= BlurIntensityPresets.MediumRadius` so that the two constants always agree.
A unit test (`MediumRadius_MatchesBlurPipelineDefault`) locks this invariant in.
The pipeline is value-agnostic; the `ToRadius` helper and the enum live in the UI
concept layer of `Gausslite.Core/Blur/` for easy reuse in v0.4.0's slider path.

`TrayOrchestrator.SetIntensity(BlurIntensityPreset)` writes `BlurRadius` directly and
tracks `CurrentIntensity` as an auto-property. No lock needed since the method is
called on the UI thread (same thread that owns all public orchestrator methods).
`ITrayOrchestrator` exposes `SetIntensity` and `CurrentIntensity` so `TrayIconHost`
can wire up the submenu and set initial checkmarks without coupling to the concrete
class.

`TrayIconHost` builds the submenu by calling a `CreateIntensityItem` helper that
captures the preset in a lambda — a simple pattern that avoids repeating the click
handler logic three times. `UpdateIntensityCheckmarks` keeps only the clicked item
checked and clears the others, giving radio-button visual behavior without requiring
a custom `ItemsControl` or data binding.

**Files created:**
- `src/Gausslite.Core/Blur/BlurIntensityPreset.cs` — enum + `BlurIntensityPresets` static class
- `tests/Gausslite.Core.Tests/Blur/BlurIntensityPresetTests.cs` — 5 tests (3 theory + 2 fact)

**Files modified:**
- `src/Gausslite.Core/Blur/BlurPipeline.cs` — `DefaultBlurRadius` references `MediumRadius`; volatile backing field
- `src/Gausslite.App/Orchestration/ITrayOrchestrator.cs` — added `CurrentIntensity`, `SetIntensity`
- `src/Gausslite.App/Orchestration/TrayOrchestrator.cs` — implemented `CurrentIntensity`, `SetIntensity`
- `src/Gausslite.App/Tray/TrayIconHost.cs` — intensity submenu, `CreateIntensityItem`, `UpdateIntensityCheckmarks`
- `tests/Gausslite.Core.Tests/Blur/BlurPipelineTests.cs` — added `BlurRadius_Default_IsMediumPreset`
- `tests/Gausslite.App.Tests/TrayOrchestratorTests.cs` — added 4 `SetIntensity` tests

**Test counts:** Core 59/59, App 40/40 (x64). Build: 0 warnings, 0 errors.

---

### 2026-04-30 — IAppProfile abstraction (v0.2.0 refactor)

**Goal:** Pure refactor. Extract WhatsApp-specific window-detection knowledge
from generic infrastructure and place it behind a new `IAppProfile` interface.
`WhatsAppProfile` becomes the first (and currently only) concrete implementation.
Zero user-visible behavior change.

**What moved and where:**

- **New:** `src/Gausslite.Core/AppProfiles/IAppProfile.cs` — interface with
  three members: `string Name`, `bool IsAppWindow(processName, className, title)`,
  `IntPtr FindWindowHandle()`. Namespace `Gausslite.Core.AppProfiles`.

- **New:** `src/Gausslite.Core/AppProfiles/WhatsAppProfile.cs` — takes `IWin32Api`
  in its constructor. `IsAppWindow` contains the predicate logic previously split
  between `Win32Api.IsWhatsAppWindow` (authoritative) and
  `CaptureItemFactory.IsWhatsAppWindow` (a delegate that called the former).
  `FindWindowHandle` calls `_win32.FindWindowHandle(IsAppWindow)`.

- **`IWin32Api`:** `FindWhatsAppWindowHandle()` removed, replaced by
  `FindWindowHandle(Func<string,string,string,bool> predicate)` — generic
  window-enumeration helper that calls the supplied predicate per window.

- **`Win32Api`:** `FindWhatsAppWindowHandle()` renamed/genericized to
  `FindWindowHandle(predicate)`. `IsWhatsAppWindow` static method removed.

- **`WindowTracker`:** Constructor gains `IAppProfile profile` parameter (before
  the optional `pollInterval`). `SampleWindowState()` calls
  `_profile.FindWindowHandle()` instead of `_win32.FindWhatsAppWindowHandle()`.
  Log strings at "WhatsApp window detected", "minimized changed to False because
  WhatsApp", "WhatsApp not found after 5 seconds" all replaced with
  `_profile.Name` interpolations.

- **`CaptureItemFactory`:** Refactored to take `IAppProfile profile` (no longer
  needs `IWin32Api` — that field was assigned but unused; the class did its own
  P/Invoke enumeration). `TryCreateForWhatsApp` → `TryCreateForProfile`.
  `FindWhatsAppWindow` private method → `FindProfileWindow` (instance method,
  uses `_profile.IsAppWindow()`). `IsWhatsAppWindow` internal static removed.
  `ReasonFor` helper simplified (can no longer inspect predicate internals).
  The diagnostic per-window enumeration loop is preserved.

- **`ICaptureItemFactory`:** `TryCreateForWhatsApp` → `TryCreateForProfile`.

- **`TrayOrchestrator`:** Both constructors gain `IAppProfile profile` parameter.
  Log strings at "waiting for WhatsApp HWND" and "restore arrived while WhatsApp
  is still occluded" replaced with `_profile.Name` interpolations. Log strings
  that mentioned `TryCreateForWhatsApp` by method name naturally genericized by
  the rename.

- **`App.xaml.cs`:** Composition root creates `IAppProfile appProfile = new
  WhatsAppProfile(new Win32Api())` and passes it to `WindowTracker`,
  `CaptureItemFactory`, and `TrayOrchestrator`. A second `new Win32Api()` is
  passed directly to `WindowTracker` for its non-profile Win32 calls (GetWindowRect
  etc.) — two instances, both stateless, no concern.

**Tests:**

- **New:** `tests/Gausslite.Core.Tests/AppProfiles/WhatsAppProfileTests.cs` —
  18 `IsAppWindow` theory cases (moved verbatim from `CaptureItemFactoryTests`),
  plus `Name_ReturnsWhatsApp` and `FindWindowHandle_DelegatesToWin32`.

- **`WindowTrackerTests`:** `IAppProfile _profile = Substitute.For<IAppProfile>()` added.
  `CreateTracker()` passes `_profile`. All `_win32.FindWhatsAppWindowHandle()`
  mock setups replaced with `_profile.FindWindowHandle()`.

- **`WindowTrackerMinimizedTests`:** Same pattern — `_profile` mock added,
  constructor call updated, `FindWhatsAppWindowHandle` mock → `FindWindowHandle`.

- **`CaptureItemFactoryTests`:** Rewritten. The 18 predicate theory cases removed
  (moved to `WhatsAppProfileTests`). Two integration tests renamed and updated to
  construct `new CaptureItemFactory(new WhatsAppProfile(new Win32Api()))`.

- **`TrayOrchestratorTests`:** `IAppProfile _profile = Substitute.For<IAppProfile>()`
  added. Both `CreateSut()` / `CreateSutWithInlineDispatch()` pass `_profile`.
  All `TryCreateForWhatsApp` call sites renamed to `TryCreateForProfile`.

**Final test counts:** Core 53/53, App 36/36 (x64). Before: Core 33, App 54.
The 18 predicate tests moved from App.Tests to Core.Tests; 2 new tests added in
Core.Tests for `Name` and `FindWindowHandle`. Net: +2 tests across the solution.

**Non-obvious design decisions:**

1. `IWin32Api` got the generic `FindWindowHandle(predicate)` method rather than
   putting P/Invoke directly in `WhatsAppProfile`. This keeps all Win32 enumeration
   behind the interface seam, making `WhatsAppProfile.FindWindowHandle()` testable
   by mocking `IWin32Api`.

2. `CaptureItemFactory` keeps its own window enumeration (not delegating to
   `_profile.FindWindowHandle()`) because it has a detailed diagnostic per-window
   logging loop that runs only on the first call. Delegating to the profile would
   lose that logging; the task required log strings to stay at the same call sites.

3. `TrayOrchestrator` receives `IAppProfile` even though it has no window-detection
   logic, solely to genericize two log strings. This is the minimal change that
   removes the hardcoded "WhatsApp" references from it.

---

### 2026-04-30 — v0.1.1 rename + post-rename documentation pass

The project was renamed from internal codename "WAshed" to "Gausslite"
(single capital G, portmanteau of Gaussian + gaslighting). The rename
was a single Codex pass: solution file, all .csproj files, all C#
namespaces, folder names, assembly names, the executable name, log
file names (gausslite-startup.log, gausslite-crash.log), all in-code
references, and all forward-looking documentation. Project GUIDs were
preserved so Visual Studio's project identity stayed intact across
the rename. CHANGELOG historical entries describing v0.1.0 work were
kept verbatim — they describe what the code did under the name it had
at the time.

Verification gates all passed: `dotnet restore Gausslite.sln`,
`dotnet build -c Release` (0 warnings, 0 errors), Core tests 33/33,
App tests 54/54 (the prior baseline of 51/51 in earlier session notes
was stale; 54 is the actual current count). `git grep -in washed`
returned only CHANGELOG historical matches, confirming clean rename.

Smoke test post-rename: launched Gausslite.App.exe, enabled blur on
WhatsApp, behavior identical to v0.1.0. New log files confirmed
written under the new names.

After the rename merged, a separate small commit updated the tray
icon asset to reflect the new branding. The v0.1.1 tag was cut on
main with no functional changes from v0.1.0.

The rest of the session was a documentation pass to align the project
docs with both the new name and a restructured roadmap:

- **PLAN.md milestones** rewritten. Old v0.3.0 "Auto-activation"
  redefined as v0.3.0 "Knows when to blur" — covers screen-share
  client process scanning AND share-target detection (which monitor
  or window the sharing client has captured), with default-blur-if-
  uncertain as the privacy-first activation rule. v0.5.0 added for
  toast notification blur. v0.2.0 expanded to include the IAppProfile
  abstraction (clean separation of WhatsApp-specific knowledge from
  generic blur infrastructure), pixel-region occlusion clipping
  (deferred from v0.1.0), and runtime-configurable blur intensity via
  a tray-menu preset submenu (Light/Medium/Heavy). Multi-app support
  (Signal, Telegram, Discord, Slack) removed from the v0.x roadmap;
  decision was IDD-before-breadth, with multi-app deferred to
  possible post-v1.0 if user demand materializes.

- **PLAN.md vision and non-goals** updated to match the broader
  framing (sensitive chats, not just WhatsApp by name) while keeping
  WhatsApp as the explicit v0.x and v1.0 target.

- **PLAN.md decisions log** appended with the rename, roadmap
  restructure, multi-app deferral, repo-visibility policy (private
  through v0.x), and tray-menu vs. settings-window split.

- **PLAN.md architecture diagram and module map** updated from "WA"
  shorthand to "WhatsApp" full name now that the project no longer
  carries the WA prefix.

- **README.md** rewritten with Gausslite framing: new tagline ("Blur
  sensitive chats automatically when you're sharing your screen"),
  new "Why Gausslite?" section explaining the name origin, Features
  list describing the v0 target spec (Roadmap section is the source
  of truth for what's shipped), Roadmap matching new PLAN.md
  milestones, Installation section honest about pre-public state,
  Citation and Releases links updated to new GitHub URL. The
  WhatsApp/Meta trademark disclaimer paragraphs were kept verbatim
  since WhatsApp remains the v0.x target.

- **GitHub repo** renamed from `WAshed` to `Gausslite`. Local remote
  URL updated. Local repo folder renamed.

- **Repo visibility** stays private through v0.x. Public release
  deferred until v0.x is feature-complete with a proper signed
  installer. v0.1.1 is not the right front-door artifact for a
  public launch.

**Verification:** all builds clean, all tests green, all forward-
looking docs use the new name, only CHANGELOG historical entries
retain "WAshed" references.

---

### v0.1.0 dev — offscreen overlay parking privacy fix

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
`dotnet build WAshed.sln -c Debug -m:1 --no-restore`. Core tests passed
(33/33). App tests were written and attempted, but both the full x64 App test
run and a focused `TrayOrchestratorTests` filter still hung after discovery in
this shell, matching the documented App testhost issue. No manual WhatsApp
smoke test was run in this session, so no real `event-to-move` numbers were
recorded.

---

### v0.1.0 dev — eager armed setup privacy fix

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
session, so no real `washed-startup.log` event-to-flip number was recorded.

---

### v0.1.0 dev — armed-restore dispatcher priority privacy fix

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
`washed-startup.log`.

#### Smoke test note
- Reproduction: minimize WhatsApp, enable blur, restore WhatsApp from the
  taskbar. The opaque placeholder should cover WhatsApp as soon as the restored
  window appears, and the log gap between `received minimized=False` and
  `applying minimized=False on UI thread` should be under 5ms.

**Verification:** Debug solution build passed with 0 warnings/errors. Core tests
passed (33/33). Focused `WAshed.App.Tests` execution for
`TrayOrchestratorTests` still hangs after discovery in this shell, matching the
pre-existing App testhost blocker from prior sessions.

---

### v0.1.0 dev — armed-restore privacy fix and documented occlusion limitation

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

#### Smoke test note
- Reproduction for armed->restore privacy fix: minimize WhatsApp, enable blur,
  restore WhatsApp. Opaque placeholder must appear within one poll cycle (~33ms)
  of WhatsApp becoming visible. Live blur replaces placeholder when first frame
  arrives. WhatsApp content must never be readable.

**Verification:** Debug solution build passed with 0 warnings/errors. Core tests
passed (33/33). Focused `WAshed.App.Tests` execution still hangs after
discovery via `dotnet vstest`, matching the pre-existing App testhost blocker
from prior sessions.

---

### v0.1.0 dev — final visibility/privacy fixes: occlusion-aware overlay and reliable restore placeholder

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

#### Fixed
- **Occluded WhatsApp:** overlay visibility now follows whether WhatsApp is
  actually visible at its center point, including overlay-self-exclusion.
- **Armed -> active privacy gap:** restore from minimized/armed state now paints
  an opaque dark rectangle immediately until a fresh blurred frame is presented.

#### Smoke test sequence for armed restore placeholder
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

### v0.1.0 dev — final privacy blockers: active-resize frame-pool recreation and armed-state placeholder cover

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

#### Fixed
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

### v0.1.0 dev — final blocker fixes: native overlay resizing and stale-frame restore privacy

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

#### Fixed
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
overlay test filter. Release build was blocked because a running `WAshed.App`
process held the Release output `WAshed.Overlay.dll` open.

---

### v0.1.0 dev — smoke-test fixes: maximize tracking, drag lag, and armed blur activation

`WindowTracker` now polls at 33ms (~30 Hz) instead of 100ms, so fast title-bar drags should no longer visibly outrun the overlay. Its bounds path now distinguishes normal window rectangles from maximized rectangles: normal windows still report the raw visual window bounds converted from physical pixels to WPF DIPs, while maximized windows are normalized to the current monitor work area before DPI conversion. This clips Windows' invisible maximized frame extension (for example `-8,-8` / monitor+8 raw `GetWindowRect` values) and keeps multi-monitor per-monitor-DPI behavior intact.

`TrayOrchestrator` now uses an explicit blur activation state machine:

- `Idle`: blur is disabled.
- `Armed`: blur is enabled but waiting for a visible, non-minimized WhatsApp window. No overlay is shown and capture is not started.
- `Active`: capture is running and the overlay is shown/following bounds.

Enabling blur while WhatsApp is missing or minimized now enters `Armed` silently. Tracker polling continues, and the orchestrator transitions to `Active` when WhatsApp appears/restores with valid bounds. At the time of that session, closing or minimizing WhatsApp while blur was active stopped capture and returned to `Armed`; the final blocker fix above changed minimize specifically to keep capture alive while the overlay is hidden. This replaces the previous implicit "retry on BoundsChanged" path.

#### Fixed
- **Maximize overlay position:** `WindowTracker` normalizes maximized `GetWindowRect` output to `MonitorFromWindow`/`GetMonitorInfo.rcWork` before converting to WPF DIPs, preventing the overlay from staying offset by the invisible extended frame.
- **Drag lag:** default `WindowTracker` polling interval reduced from 100ms to 33ms.
- **Arming while minimized/not running:** `TrayOrchestrator.EnableBlur` no longer starts capture or shows overlay unless the tracker reports WhatsApp present, non-minimized, and with current bounds.

#### Added
- **Window presence tracking:** `IWindowTracker.WindowPresenceChanged`, `IsWindowPresent`, and `IsMinimized` let orchestration react to WhatsApp close/reopen and minimized/restore transitions explicitly.
- **State-machine tests:** added tests for `Idle -> Armed -> Active`, `Idle -> Active`, and `Active -> Armed -> Active`.
- **Coordinate-normalization tests:** added maximized bounds normalization coverage at 100%, 125%, and 150% DPI, including negative-coordinate monitor layouts.

**Build:** `dotnet build WAshed.sln --no-restore -v:minimal -maxcpucount:1` passed with 0 warnings/errors. `WindowTracking` tests passed (12/12). Full `dotnet test` is currently blocked in this shell by pre-existing environment issues: Windows Graphics Capture tests throw `COMException: The specified service does not exist as an installed service`, and the App testhost hangs before entering even a pure state-machine test. See notes below.

---

### v0.1.0 dev — WindowTracker/TrayOrchestrator hardening after overlay smoke diagnostics

`WindowTracker` events are raised from its polling thread, but `TrayOrchestrator` was touching the WPF overlay directly from those callbacks. Bounds updates and minimize/restore overlay operations are now marshalled to `Application.Current.Dispatcher`; the handlers still log immediately on the originating polling thread, and shutdown races where `Application.Current` is null or the dispatcher is shutting down are logged and dropped.

The tracker also now logs bounds changes with throttling instead of logging only the first change. This keeps drag diagnostics visible (`#1`, `#10`, `#20`, ...) without flooding `washed-startup.log`.

Finally, the DPI path was corrected. The app manifest is `PerMonitorV2`, so `GetWindowRect` returns physical pixels. Because `OverlayWindow.SetBounds` assigns WPF `Window.Left/Top/Width/Height`, `WindowTracker` converts physical pixels to WPF DIPs by dividing by `GetDpiForWindow(hwnd) / 96.0`; the previous multiply path double-scaled high-DPI displays.

#### Fixed
- **`TrayOrchestrator`** now dispatches `OnBoundsChanged` and `OnMinimizedChanged` UI work via the WPF dispatcher before calling `IOverlayWindow.SetBounds`, `Hide`, or `Show`.
- **`WindowTracker`** replaced the one-shot `_firstBoundsChangeLogged` guard with throttled bounds-change logging every 10 changes.
- **`WindowTracker`** now returns WPF DIP bounds from physical `GetWindowRect` pixels by dividing by the HWND DPI scale.
- Updated the 150% DPI unit test to catch double-scaling regressions.
- Updated `app.manifest` comments to document the PerMonitorV2 physical-pixel-to-WPF-DIP contract.

**61 tests green (38 WAshed.App.Tests + 23 WAshed.Core.Tests).**

---

### v0.1.0 dev — WhatsApp detection unified for WinUI 3 Store build

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

#### Changed
- **`IWin32Api`**: Replaced `FindStoreWhatsAppWindowHandle()` with `FindWhatsAppWindowHandle()`.
- **`Win32Api`**: Removed `FindStoreWhatsAppWindowHandle()`. Added `internal static IsWhatsAppWindow(processName, className, title)` (the single authoritative predicate, accessible to `WAshed.App` via existing `InternalsVisibleTo`). Added `FindWhatsAppWindowHandle()` using that predicate.
- **`WindowTracker`**: `SampleBoundsWithHandle` now calls `_win32.FindWhatsAppWindowHandle()` only — no more process-name loop or two-strategy fallback. Removed `WhatsAppProcessNames` array.
- **`CaptureItemFactory`**: Replaced `FindWin32WhatsAppWindow`/`FindStoreWhatsAppWindow` and Strategy A/B with single `FindWhatsAppWindow()`. Removed `IsWin32WhatsAppProcess`/`IsStoreWhatsAppWindow`; added `IsWhatsAppWindow` wrapper to `Win32Api.IsWhatsAppWindow`. Added per-candidate `DiagLog` tracing (up to 20 candidates, with `match` + `reason` per entry).
- **`CaptureItemFactoryTests`**: Replaced `IsWin32WhatsAppProcess` + `IsStoreWhatsAppWindow` predicate tests with 16 `IsWhatsAppWindow` tests covering all match/reject cases. Integration test `TryCreateForWhatsApp_WhenWhatsAppNotRunning` now skips gracefully when WhatsApp IS running (detection success correctly returns true in that case).
- **`WindowTrackerTests`**: Updated mock setup from `GetWindowHandlesForProcessName` to `FindWhatsAppWindowHandle()`.

**57 tests green (35 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

---

### v0.1.0 dev — diagnostic tracing across the full blur pipeline

Added step-by-step `StartupLog`/`DiagLog` tracing to every layer of the blur path so a top-to-bottom read of `washed-startup.log` reveals exactly where things go silent when "Enable blur" is clicked with no visible result.

#### Added
- **`src/WAshed.Core/Diagnostics/DiagLog.cs`** — new internal static logger (same washed-startup.log file, same ISO-8601 timestamp format as `StartupLog`). Used by WAshed.Core and WAshed.Overlay, which cannot reference WAshed.App. `InternalsVisibleTo(WAshed.Overlay)` added to WAshed.Core.csproj.
- **`TrayOrchestrator`** — `EnableBlur` now logs entry, WindowTracker start/started, TryCreateForWhatsApp call and result, abort if WhatsApp not found, CaptureEngine start, OverlayWindow show, SetBounds call, complete. `DisableBlur` logs entry/complete. `OnFrameArrived` logs first-frame dimensions (Interlocked flag), a per-60-frame heartbeat, and any exception (logged then re-thrown).
- **`CaptureEngine.Start`** — logs entry, `GraphicsCaptureSession.IsSupported()` result, frame-pool creation, FrameArrived subscription, session start. First WGC frame arrival logged once via Interlocked flag.
- **`WindowTracker`** — logs first WhatsApp window detected (HWND + bounds), first bounds change, and a one-time warning if WhatsApp is not found within 5 seconds of the poll loop starting. Refactored `SampleBounds` → `SampleBoundsWithHandle` to return the HWND alongside the rect for logging.
- **`OverlayWindow.Show`** — logs entry (current Visibility), and HWND + IsVisible after `_window.Show()`. **`SetBounds`** logs all four coordinates. **`PresentFrame`** logs first-call source dimensions (Interlocked flag); exceptions are caught, logged via DiagLog, and swallowed so one bad frame cannot crash the app.
- **`D3DImageBridge.UpdateD3DImage`** — first-call only (Interlocked flag) logs every step of the WinRT→DXGI→D3D9Ex bridge: surface acquisition, QI for IDirect3DDxgiInterfaceAccess (with HR), GetInterface for ID3D11Texture2D (HR), QI for IDXGIResource (HR), GetSharedHandle result (HR + handle hex value). **CRITICAL log**: if shared handle is zero logs "ABORT — render target was not created with DXGI_RESOURCE_MISC_SHARED". All exceptions caught, logged, swallowed (no re-throw).

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

---

### v0.1.0 dev — tray library swap from H.NotifyIcon to Hardcodet.NotifyIcon

H.NotifyIcon.Wpf replaced with Hardcodet.NotifyIcon.Wpf 2.0.1.

Diagnostic evidence from the previous session's `washed-startup.log` proved conclusively that our code was correct: the .ico file existed on disk, `BitmapImage` loaded with real 32×32 dimensions, `TaskbarIcon` was constructed, `IconSource` was assigned, `Visibility` was forced to `Visible`, and the `System.Drawing.Icon` fallback also succeeded — yet no icon ever appeared in the system tray. The bug is inside H.NotifyIcon or its interaction with the Windows shell notification area on at least one tested machine. Swapped to Hardcodet.NotifyIcon.Wpf (the original project that H.NotifyIcon forked from), which is more mature and widely deployed.

#### Changed
- `WAshed.App.csproj`: Replaced `H.NotifyIcon.Wpf 2.1.0` with `Hardcodet.NotifyIcon.Wpf 2.0.1`.
- `TrayIconHost.cs`: Updated `using H.NotifyIcon;` → `using Hardcodet.Wpf.TaskbarNotification;`. All other code is unchanged — the `TaskbarIcon` API (`IconSource`, `Icon`, `ToolTipText`, `ContextMenu`, `Visibility`, `Dispose`) is identical between the two libraries.
- All diagnostic logging and the `System.Drawing.Icon` fallback retained unchanged.

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings (1 platform warning only when building project directly, not via sln).**

---

### v0.1.0 dev — tray icon debugging session and infrastructure fixes

Tray icon still not visible during v0.1.0 smoke testing despite prior fixes (icon file present on disk, no crash log written, process alive at ~164 MB). Added step-by-step diagnostic instrumentation to pinpoint exactly where initialization silently stalls.

#### Added — Startup diagnostic log (`washed-startup.log`)
- New `src/WAshed.App/Diagnostics/StartupLog.cs`: `Info`/`Warn` methods, ISO 8601 timestamps with milliseconds, append-mode with flush-per-write, truncated on each app start so the file always reflects the most recent run.
- `App.xaml.cs` `OnStartup` now logs "OnStartup begin / TrayOrchestrator constructed / TrayIconHost constructed / Initialize() returned / OnStartup complete" with try/catch around each step; exceptions are logged then re-thrown so the existing crash-log behaviour is preserved.
- `TrayIconHost.Initialize` logs every step: resolved icon path, `File.Exists`, `BitmapImage` (IsFrozen, PixelWidth, PixelHeight), `TaskbarIcon` creation, `IconSource` assignment, `Visibility` before and after forcing it to `Visible`.

#### Added — `System.Drawing.Icon` native fallback in `TrayIconHost.Initialize`
After the `BitmapImage`/`IconSource` path, also sets `taskbarIcon.Icon = new System.Drawing.Icon(iconPath)`. This is the legacy HICON path that bypasses WPF imaging entirely; H.NotifyIcon accepts either. If the WPF path silently fails, the native path may succeed. Success/failure of this fallback is both logged and non-fatal (wrapped in try/catch). Added `System.Drawing.Common 8.0.7` to `WAshed.App.csproj` (version pinned to match the transitive version required by `H.GeneratedIcons.System.Drawing 2.1.0`).

#### Added — Force `Visibility.Visible` on `TaskbarIcon`
Explicitly sets `_taskbarIcon.Visibility = Visibility.Visible` after construction. H.NotifyIcon's default `Visibility` can vary by version; this ensures the icon is always shown regardless of the default.

#### Fix — Tray icon never appeared (silent failure)
**Root cause:** `Assets\tray-icon.ico` was declared as `<Resource>` in `WAshed.App.csproj`. The file IS embedded into the WPF `.g.resources` bundle (confirmed by enumerating the bundle — key `assets/tray-icon.ico` was present). However, H.NotifyIcon cannot convert a `BitmapImage` loaded from a pack URI to a native `HICON`; its internal conversion path requires a real file path or a `System.Drawing.Icon`. The conversion throws silently on the WPF dispatcher, the app continues with no icon, and there is no console output.

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

#### Added — Global exception logger
Hooked `AppDomain.CurrentDomain.UnhandledException` and `Application.DispatcherUnhandledException` in `App.xaml.cs`. All unhandled exceptions (including full `InnerException` chain and stack trace) are written to `washed-crash.log` in `AppContext.BaseDirectory` AND to Debug output. For dispatcher exceptions, `e.Handled = true` is set so a bad icon load cannot take down the whole app, but the failure IS recorded.

**Known fragility (do not revert):** Using `<Resource>` for .ico files in H.NotifyIcon projects is unreliable — H.NotifyIcon silently fails to create the native icon from a pack-URI BitmapImage. Always use `<Content CopyToOutputDirectory>` + file-path loading for tray icon assets.

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

#### Fix — Win2D AnyCPU build warning
- Added `<Platforms>x64</Platforms>` and `<PlatformTarget>x64</PlatformTarget>` to all five .csproj files (WAshed.Core, WAshed.Overlay, WAshed.App, WAshed.Core.Tests, WAshed.App.Tests).
- Updated `.sln` project config mappings to route the `Any CPU` solution platform to `x64` per-project configs; solution-level platform name kept as `Any CPU` so `dotnet build` works without `--arch x64`.
- Build now produces zero warnings; `Microsoft.Graphics.Canvas.dll` lands under `runtimes/win-x64/native/` in the app output.

**All 39 tests still green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors, 0 warnings.**

---

### v0.1.0 dev — first end-to-end blur path: CaptureItemFactory, Win2D shared texture, composition root

Shipped the two remaining pieces blocking the first end-to-end smoke test.

#### Part 1 — CaptureItemFactory (real WinRT activation)
- Replaced the stub in `CaptureItemFactory.TryCreateForWhatsApp` with a full
  `IGraphicsCaptureItemInterop` / `RoGetActivationFactory` P/Invoke path
  (combase.dll: `RoGetActivationFactory`, `WindowsCreateString`, `WindowsDeleteString`).
- `IGraphicsCaptureItemInterop` (IID `3628E81B-…`) declared as an internal
  `[ComImport]` interface; `CreateForWindow` marshals the ABI pointer to a managed
  `GraphicsCaptureItem` via `MarshalInterface<GraphicsCaptureItem>.FromAbi`.
- Failure handling: `GraphicsCaptureSession.IsSupported()` guard, non-zero HRESULT
  logged in hex and returns false (no throw), WhatsApp not found returns false.
- Added `InternalsVisibleTo(WAshed.App.Tests)` to `WAshed.App.csproj`.
- Integration test: `TryCreateForWhatsApp_WhenWhatsAppNotRunning_ReturnsFalseAndNull`
  and `_CalledTwice_NeverThrows` in `tests/WAshed.App.Tests/Orchestration/`.

#### Part 2 — Win2DBlurRenderTarget (concrete GPU render target with shared texture)
- **`Win2DBlurRenderTarget`** (internal sealed, `WAshed.Core.Blur`):
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
- Added `InternalsVisibleTo(WAshed.Core.Tests)` to `WAshed.Core.csproj`.
- GPU-gated tests in `tests/WAshed.Core.Tests/Blur/Win2DBlurRenderTargetTests.cs`
  (skip when `GraphicsCaptureSession.IsSupported()` is false; CI has no GPU).

#### Part 3 — WinRT capture concrete wrappers
- Created `WinRTCaptureFrame`, `WinRTCaptureSession`, `WinRTCaptureFramePool`,
  `WinRTCaptureInterop` in `WAshed.Core.Capture` so the real `CaptureEngine`
  can be wired up without the GPU-level stubs.

#### Part 4 — Composition root
- `App.xaml.cs`: replaced `NullCaptureEngine` / `NullBlurPipeline` with
  real `CaptureEngine` + `BlurPipeline`. Shared D3D11 device created via
  `D3D11CreateDevice` → QI for `IDXGIDevice` → `WinRTCaptureInterop.CreateDirect3DDevice`.
  `BlurPipeline.Initialize(d3dDevice)` called immediately so Win2D's CanvasDevice
  is ready before any frame arrives. `NullCaptureEngine` and `NullBlurPipeline`
  retained as `internal` test fakes.

**All 39 tests green (17 WAshed.App.Tests + 22 WAshed.Core.Tests). Build 0 errors.**
