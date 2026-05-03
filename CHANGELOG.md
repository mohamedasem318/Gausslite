# Changelog

All notable changes to Gausslite will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Security
- Diagnostic logs (`gausslite-startup.log`) no longer record the contents of window
  titles for windows that aren't WhatsApp.  The per-window enumeration log written
  during capture initialisation (up to the first 20 visible windows) used to record
  process name, class name, and full title for each examined window — meaning browser
  tab titles, document filenames, and similar data could land on disk.  Now records
  only `title.len={n}` for non-matching windows; process and class are kept because
  both are needed to debug "why didn't WhatsApp match?".  The screen-share-detected
  transition log also no longer records the matched share-control window's title
  (which for the browser signature contained the meeting host's domain); the app
  name and window class already uniquely identify which signature fired.
- `gausslite-startup.log` is now bounded at 5 MB within a session.  Both writers
  (the `Gausslite.App` startup log and the `Gausslite.Core` diagnostic log target the
  same file) check size before each append and truncate-then-restart if the file is
  over the cap.  Earlier behaviour truncated only on each app launch, so a tray
  session running for days could in principle accumulate megabytes of logs.
- The screen-capture path now re-validates the WhatsApp window handle immediately
  before passing it to `IGraphicsCaptureItemInterop.CreateForWindow` (`IsWindow` plus
  a class-name re-check).  Closes a small race window where the kernel could destroy
  the window and recycle its handle to another process between window enumeration
  and capture-item creation; capturing a recycled handle would have meant blurring
  some unrelated app's window thinking it was WhatsApp.
- The full transitive NuGet dependency graph is now pinned via committed
  `packages.lock.json` files (`<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`
  on the 5 production + test csprojs).  Direct package versions were already pinned;
  this closes the supply-chain gap where transitive dependencies could float across
  `dotnet restore` runs.
- Belt-and-suspenders: root `.gitignore` now covers `*.log` globally, so any future
  log file that escapes its expected location can't accidentally be committed.

## [0.3.0] - 2026-05-03

### Changed
- Cold-start placeholder color softened from dark slate (RGB 32,44,51) to light
  neutral gray (RGB 220,222,220).  The placeholder is still fully opaque (privacy
  contract preserved); the new color approximates the average tone of a heavily-blurred
  bright UI (WhatsApp's mostly-white background dominates), so the brief
  ShowPlaceholder→first-blurred-frame transition during cold start reads as "blur
  fading in" rather than a jarring dark flash before blur.

### Fixed
- Auto-blur cold-start now nudges the tracked window to repaint on share-start, so the
  first blurred frame arrives quickly even when WhatsApp was idle (cursor not over it,
  no animations) at the moment the share started.  Without this, the user would see
  the privacy placeholder until WhatsApp painted on its own (which can be seconds for
  an idle window).  Same `InvalidateRect`-via-`IWindowTracker.RequestRepaintOfTrackedWindow()`
  pattern v0.2.0 introduced for snap-resize bounds changes.
- Privacy bug exposed by auto-blur: during a screen share, the v0.2.0 bounds-based
  occlusion-clipping logic incorrectly reported WhatsApp as fully covered when sharing
  apps (Zoom in particular) dropped many small floating overlays on top — the Z-order
  walk subtracted each overlay's full bounds even though those overlays were small or
  semi-transparent and WhatsApp pixels were leaking around / through them into the
  shared stream.  The orchestrator now overrides the occlusion logic during an active
  screen share: blur covers the full WhatsApp window regardless of what's stacked on
  top.  Worst case is some over-blur where Zoom's overlays really do cover WhatsApp,
  but viewers see Zoom's overlay there anyway, so the over-blur is invisible to them.
  v0.2.0's normal occlusion-clipping behavior is preserved when no share is active.

### Added
- Auto-blur when you start screen sharing. Gausslite now watches for active share-control
  windows from Zoom, Microsoft Teams, and any browser-based share (Google Meet, web Zoom,
  web Teams, web Discord, anything using `getDisplayMedia`). When a share starts, blur
  turns on within ~2 seconds; when the share ends, blur returns to whatever state it was
  in before. Polling cadence is 1 Hz on the UI thread.
- Friendly tray balloon when you manually disable blur during an active share: "Blur is
  off for this share — we'll turn it back on automatically the next time you share your
  screen." Fires once per share so it doesn't nag.
- Manual override during a share now sticks for the rest of THAT share. Disable blur
  mid-share → it stays off until the share ends and the next one begins. Re-enabling
  during a share also counts as taking manual control — auto-restore at share-end won't
  undo what you set.
- Left-click on the system tray icon now toggles blur on/off. Same behavior as the tray
  menu's "Enable blur" entry and the global hotkey (Ctrl+Shift+B).
- `tools/ShareProbe` and `tools/DiscordProbe` recon utilities: dump every visible window
  (and child windows for the Discord probe) so we can capture share-control window
  signatures from real share sessions. Used to derive the active set of detection
  signatures; not part of the shipped app.

### Known limitation
- **Discord desktop** screen sharing is NOT auto-detected. Discord renders its share
  controls as web content inside its Electron window — invisible to GDI window
  enumeration at any depth. Workarounds: use the global hotkey (Ctrl+Shift+B), the tray
  left-click toggle, or share via Discord-in-browser (which IS detected via the generic
  Chrome/Edge signature). Tracked for a v0.3.x follow-up that does UIA tree-walking on
  Discord's main window.

## [0.2.0] - 2026-05-03

### Known limitation
- Internal WhatsApp layout shifts (e.g. dragging the chat-list/conversation divider
  inside WhatsApp itself) may take a few seconds to update the scope clip if WhatsApp
  is otherwise idle and not delivering capture frames. Workaround: hover the cursor
  over WhatsApp — the resulting paint provokes a fresh capture frame and the clip
  updates within ~1 second. Tracked in
  https://github.com/mohamedasem318/Gausslite/issues/35; planned fix is an opt-in
  wall-time forced-repaint timer in v0.4.0 once the broader settings UI lands.

### Fixed
- Scope-aware clip now follows WhatsApp through left-edge resize, maximize/restore,
  snap-resize, and external bounds changes without needing the user to hover over
  WhatsApp afterwards. Five layered fixes: (1) `RunDetection` validates the readback frame's dimensions
  against the current window bounds and skips when they're inconsistent (rejects stale
  cached frames during the bounds-change → post-resize-frame race window — these caused
  the clip to map old rects through a wrong scale ratio, e.g. 1.596 instead of 1.000
  on maximize); (2) detection runs continuously on a 30-frame cadence (~1 s at 30 fps)
  instead of as a one-shot per capture session, catching internal WhatsApp layout
  shifts (e.g. divider drags) that emit no resize event; (3) `RecomputeAndApplyClip`
  self-validates cached content size against current bounds and auto-clears stale
  state — covers the race where `OnVisibleRegionChanged` updates bounds before
  `OnBoundsChanged`'s size-change handler runs; (4) every `BoundsChanged` schedules a
  debounced ~400 ms delayed re-detection, so the post-responsive-layout steady state
  gets re-detected even when WhatsApp goes static and stops emitting WGC frames; (5)
  every `BoundsChanged` invalidates WhatsApp's client area via `InvalidateRect`,
  forcing it to repaint and produce a fresh WGC frame the delayed-retry can detect on
  — closes the residual case where WGC's `contentSize` doesn't update in lockstep with
  the window's bounds (e.g. snap resizes).

### Added
- Diagnostic `clip-compose` log line in `RecomputeAndApplyClip` prints bounds, content
  size, scale ratios, and the input/output rect pair on every clip recomputation —
  pinned out as the single point that makes coordinate-conversion bugs visible in
  smoke-test logs.
- Internal: region detection now runs on the first captured frame and on every WhatsApp
  resize; results are stored but not yet used to drive blur scope or clip behavior.
- Blur region scope submenu: tray menu "Blur region" with three options — "Chat list",
  "Conversation", and "Both" (default). Selected scope is stored in the orchestrator;
  region-aware blur behavior will be wired in when RegionDetector lands in v0.2.0.
- Blur intensity presets: `BlurIntensityPreset` enum (Light / Medium / Heavy) with a
  new tray menu "Blur intensity" submenu; default is Medium (20 DIPs), preserving the
  previous hardcoded behavior. `BlurPipeline.BlurRadius` is now runtime-configurable;
  a cached input frame (`ICachedFrame`) enables on-demand re-render when the preset
  changes.
- `WhatsAppRegionDetector` internal module: pure-C# CV detector that locates the
  chat-list/conversation boundary in captured BGRA frames via vertical-divider edge
  consensus combined with rail-side detection. Lays the groundwork for region-aware blur;
  not yet wired into the blur pipeline or tray menu.
- `tools/UiaDump`: diagnostic utility that dumps WhatsApp's UI Automation tree;
  used to confirm that WhatsApp Desktop is a WebView2 shell whose chat content is
  invisible to UIA.
- `tools/RegionDump`: smoke-test utility that captures one WhatsApp frame, runs the
  region detector, and saves raw and annotated PNGs for visual regression verification.

### Changed
- Internal refactor: WhatsApp-specific window-detection knowledge extracted behind
  a new `IAppProfile` interface (`WhatsAppProfile` is the first implementation),
  separating app identity from the generic blur infrastructure in preparation for
  region-aware blur.
- Minimum supported Windows version raised from 10.0.19041 to 10.0.22621 (Windows 11
  22H2) to access `GraphicsCaptureSession.IsBorderRequired`.

### Added
- Pixel-region occlusion clipping: when another window covers part of WhatsApp, the
  overlay clips to the visible portion instead of hiding entirely. `WindowTracker`
  walks the Z-order above WhatsApp (`GW_HWNDPREV`), filters out same-process HWNDs
  (WhatsApp's own WinUI 3 helper windows) and `WS_EX_TOOLWINDOW` system UI, then
  subtracts each remaining covering window's rect. `IWindowTracker` replaces the
  `IsOccluded` bool with `VisibleRegion: IReadOnlyList<Rect>?`; `IOverlayWindow` gains
  `SetClip` to apply the region as a WPF `GeometryGroup` clip. Fully-occluded case
  still hides the overlay; fully-visible case applies no clip.

### Fixed
- E_NOINTERFACE bug class fully eliminated across the blur module. Six
  `Marshal.GetObjectForIUnknown + managed-cast` sites in `Win2DBlurInterop` and
  `Win2DBlurRenderTarget` were converted to raw vtable dispatch. Sites D/E/F
  (`Win2DBlurRenderTarget` constructor, `CreateCachedFrame`, `CreateStagingTexture`) threw
  `InvalidCastException` in production: on a hardware GPU the `ID3D11Device*` returned by
  `IDirect3DDxgiInterfaceAccess::GetInterface` shares IUnknown identity with the registered
  CsWinRT `IDirect3DDevice` projection, so `Marshal.GetObjectForIUnknown` returns that
  projection and the managed cast to `ID3D11Device` fails. Sites A/B/C (`GetD3D11DevicePtr`
  ×2, `FlushD3D11Context`) had the same structure but worked by luck — `IDirect3DDxgiInterfaceAccess`
  is a COM tear-off on `IDirect3DDevice` with a different IUnknown identity, so the lookup
  missed the registered projection. All six sites now use `CreateTexture2DRaw` (vtable slot 5)
  or `CallGetInterface` (vtable slot 3); both dead `[ComImport]` interface definitions removed.
  New integration test `AllConvertedCallSites_WhileDeviceIsAlive_DoNotThrow` covers all six paths.

- Region detection now correctly identifies the chat list and conversation panes in RTL
  languages (Arabic, Hebrew) by detecting which side of the window has the navigation
  rail. The detector walks inward from each outer edge and measures how far it can travel
  before hitting vertically non-uniform (content) columns; the side with the wider
  uniform zone is the rail side. Previously the detector always labelled the left panel
  as chat list, which was wrong when WhatsApp's in-app language is set to an RTL
  language and the chat list appears on the right. Validated on live WhatsApp in both
  LTR (English) and RTL (Arabic) modes across default, narrow, and wide layouts.

- Solid-colour flash eliminated when dragging WhatsApp. The overlay used to briefly
  show the dark placeholder colour on every position change because
  `BoundsOutgrewLastBlurredFrame` compared DIP overlay width (1294) against the WGC
  physical-pixel frame width (1280); the 14 px structural gap always exceeded the `+1`
  threshold. Replaced with `OverlaySizeGrew` which compares consecutive DIP sizes — a
  pure move keeps the same size and never triggers the placeholder; an actual resize
  still shows the placeholder until the new WGC frame arrives.
- Title-bar "notch" eliminated: WhatsApp's WinUI 3 `InputNonClientPointerSource` HWND
  sits above the main window in Z-order and covers the title-bar area. `ComputeVisibleRegion`
  now skips windows that share WhatsApp's process ID, removing this internal helper from
  the covering calculation.
- Spurious clip patches during movement eliminated: system UI windows (`WS_EX_TOOLWINDOW`
  — taskbar strips, tray overflow popups, DWM helpers) were being counted as covering apps
  and fragmenting the visible region. `ComputeVisibleRegion` now skips any window with
  `WS_EX_TOOLWINDOW` in its extended style.
- Blur edge fade eliminated: `GaussianBlurEffect` default `BorderMode = Soft` was
  fading the blur to transparent within `BlurRadius` pixels of every edge, creating a
  visible gradient fringe on all four sides of the overlay. Fix: before blurring, the
  source frame is placed in a padded intermediate texture (edge pixels clamped via
  `BorderEffect.Clamp`); the GaussianBlur is applied to the padded texture; and only
  the center crop (original frame dimensions) is written to the final render target, so
  the fade zone falls entirely within the discarded padding border.
- `D3DImage.AddDirtyRect` (full rect, between `SetBackBuffer` and `Unlock`) and
  `Image.InvalidateVisual` (after `Unlock`) are now called on every present, improving
  repaint reliability for the natural 60fps capture path.
- Blur intensity preset changes (Light / Medium / Heavy) now take effect immediately
  when WhatsApp is idle and producing no new WGC frames. Root cause: `BlurPipeline
  .TryRenderCurrentFrame` re-renders on the synchronous UI thread; without an explicit
  `ID3D11DeviceContext::Flush()`, the D3D11 UMD driver held pending commands in its
  CPU-side buffer, so D3D9Ex read the shared texture before the GPU saw the new blur.
  Fixed by calling `Flush()` via vtable dispatch after Win2D drawing sessions complete.
  https://github.com/mohamedasem318/Gausslite/issues/23
- Yellow/amber capture-indicator border no longer appears around WhatsApp while blur
  is active. `GraphicsCaptureSession.IsBorderRequired` is now set to `false` on
  Windows 11 22H2+ at session start; silently skipped on older OS versions.

## [0.1.1] - 2026-04-30

### Changed
- Project renamed from WAshed to Gausslite. Solution file, project
  files, namespaces, folders, assembly names, executable name, and
  log file names all updated. No functional changes.
- Documentation restructured: PLAN.md roadmap rewritten (v0.3.0
  redefined as share-target awareness, v0.5.0 added for toast
  notification blur, v0.2.0 expanded, multi-app deferred to post-v1.0);
  README rewritten with Gausslite framing; STATE.md split — compact
  forward-looking state stays in STATE.md, verbose session archive
  moved to new HISTORY.md.

## [0.1.0] - 2026-04-29

### Added
- Project scaffolding and planning documents.
- WindowTracker module: tracks WhatsApp Desktop window bounds at 10 Hz with per-monitor DPI awareness.
- CaptureEngine module: wraps Windows.Graphics.Capture to deliver per-frame textures via a free-threaded FrameArrived event; all WinRT factory calls are behind ICaptureInterop for unit-test isolation.
- BlurPipeline module: applies configurable Gaussian blur to captured frames using Win2D, with a default radius of 20 DIPs; render target is reused across frames and reallocated only when frame dimensions change.
- OverlayWindow module: transparent, always-on-top, click-through WPF window that renders blurred output via D3DImage; uses the D3D9Ex shared-surface bridge (IDirect3DDxgiInterfaceAccess → IDXGIResource shared handle → D3D9Ex texture) to GPU-accelerate display without a CPU readback; click-through and hidden from taskbar/Alt-Tab at the Win32 level (WS_EX_TRANSPARENT + WS_EX_TOOLWINDOW).
- TrayApp: system-tray icon with "Enable blur" toggle and global hotkey (Ctrl+Shift+B); blur state is persisted across hotkey and menu interactions; overlay and capture pipeline start/stop automatically when the toggle changes.
- Capture pipeline now activates against the real WhatsApp Desktop window via Windows Graphics Capture: `CaptureItemFactory` resolves the HWND and creates a `GraphicsCaptureItem` through `IGraphicsCaptureItemInterop` / `RoGetActivationFactory` P/Invoke (replaces the earlier stub that always returned false).
- BlurPipeline now produces a real GPU-shared blurred frame consumable by the overlay window: `Win2DBlurRenderTarget` wraps a D3D11 texture created with `DXGI_RESOURCE_MISC_SHARED`, enabling the D3D9Ex shared-surface bridge in `D3DImageBridge` to present frames without a CPU copy.
- Diagnostic logging across the full blur pipeline (capture, frame processing, overlay rendering, shared-handle bridge) written to `washed-startup.log`.
- Per-frame diagnostic logging in the blur pipeline to diagnose why frames stop after the first arrives.
- Startup diagnostic log written to `washed-startup.log` to trace tray icon initialization: every step of `TrayIconHost.Initialize` and `App.OnStartup` is timestamped so the exact failure point is visible on the next run.
- Global exception logger writes uncaught errors to `washed-crash.log` next to the executable, so silent failures are no longer silent. Hooks both `AppDomain.UnhandledException` (fatal) and `DispatcherUnhandledException` (non-fatal, sets `Handled = true` to keep the app running).
- Explicit blur activation state handling for idle, armed, and active states, with tests for delayed activation and WhatsApp close/reopen flows.
- WhatsApp detection updated to match the current Microsoft Store WinUI 3 build (`WhatsApp.Root` process). Win32 builds still detected; WebView2 child windows (`msedgewebview2`) correctly excluded.

### Changed
- Build now targets x64 explicitly (was AnyCPU); Win2D's native dependencies now deploy correctly (`Microsoft.Graphics.Canvas.dll` at `runtimes/win-x64/native/`).
- Tray icon library swapped from H.NotifyIcon.Wpf to Hardcodet.NotifyIcon.Wpf for reliability.
- WindowTracker bounds-change diagnostics now log the first change and then every tenth change, so window dragging remains visible in logs without flooding `washed-startup.log`.

### Fixed
- TrayApp now loads a real .ico file for the system tray icon, fixing a launch crash on Windows (`H.NotifyIcon` does not support `RenderTargetBitmap` as an `IconSource`).
- Tray icon now loads reliably from disk instead of an embedded resource that wasn't being packaged correctly — H.NotifyIcon silently failed to convert a pack-URI `BitmapImage` to a native `HICON`, leaving the app running with no visible icon.
- OverlayWindow Image element now correctly stretches to fill the window; previously the D3DImage was feeding pixels into a 0x0 element, so the blurred frame was rendered but invisible.
- OverlayWindow now correctly sizes itself to the tracked window's bounds; previously it was rendering blur into a 14x14 area in the top-left corner.
- Overlay now hides automatically when WhatsApp is minimized and reappears when restored. Previously a stale blurred frame would float over the desktop until blur was manually toggled off.
- Title bar drag and other window-frame operations on WhatsApp now work correctly while blur is on; the overlay was intercepting `WM_NCHITTEST` and reporting itself as a non-transparent client surface, which prevented Windows from initiating drag gestures.
- WindowTracker now reports DPI-correct WPF bounds on high-DPI displays; previously it double-scaled `GetWindowRect` coordinates at 125%/150%/175% scaling.
- Overlay updates from window tracking are now marshalled to the WPF UI thread before moving, hiding, or showing the overlay, preventing cross-thread WPF access during bounds and minimize/restore events.
- WindowTracker now polls at ~30 Hz for smoother overlay tracking during fast title-bar drags.
- Enabling blur while WhatsApp is minimized or not running now arms blur silently and waits for a visible WhatsApp window instead of showing stale overlay content.
- Overlay now follows WhatsApp correctly when the window is maximized, including monitor work-area clipping and per-monitor DPI conversion.
- Restoring WhatsApp from minimize now shows the overlay immediately with the last blurred frame while Windows Graphics Capture waits for WhatsApp to repaint.
- Overlay bounds are now applied through Win32 `SetWindowPos` as well as WPF sizing, so the blur overlay can follow maximized and screen-edge WhatsApp windows instead of being clamped to its previous normal bounds.
- Enabling blur while WhatsApp is minimized or not running now shows an opaque placeholder over WhatsApp as soon as it becomes visible, hiding raw content until the first blurred frame is ready.
- Maximizing WhatsApp while blur is already active now recreates the Windows Graphics Capture frame pool on content-size changes so blurred frames fill the resized overlay instead of staying at the pre-maximize size.
- Restore and armed activation paths now show an opaque placeholder whenever no fresh blurred frame is available, preventing readable WhatsApp content during capture startup.
- Overlay now hides when WhatsApp is behind another foreground window, so blurred WhatsApp pixels no longer float over unrelated apps.
- Restoring WhatsApp while blur is armed now starts capture from the restore event itself, so the opaque placeholder covers WhatsApp within one tracker poll instead of waiting for a later bounds retry.
- Restoring or unoccluding WhatsApp while blur is armed now prioritizes the privacy-critical overlay update on the WPF dispatcher and logs dispatch queue latency, preventing readable content during restore.
- Armed-state restore now pre-creates capture and an offscreen parked overlay when WhatsApp's HWND is first seen, so restore/unocclude only moves the overlay instead of exposing WhatsApp during setup work.
- First restore from minimize in a blur session now moves the pre-created overlay on-screen instead of flipping WPF visibility, avoiding the first-show layout cost.