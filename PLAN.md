# Gausslite — Project Plan

> **For Claude Code:** Read this file at the start of every session, alongside
> `STATE.md`. This file changes rarely. `STATE.md` changes every session.

## Vision

Gausslite automatically blurs sensitive chat content when the user is
sharing their screen, so private messages aren't accidentally broadcast
in meetings.

The product targets a real gap: existing solutions are browser
extensions that only work on web versions of chat apps, or generic
advice ("use a second monitor", "share a specific app"). Nobody has
built a polished desktop app that does this for the major chat
clients people use day-to-day.

v0.x focuses on WhatsApp Desktop as the first and most-requested
target. v0.6.0 expands to Signal, Telegram, Discord, and Slack via the
IAppProfile interface introduced in v0.2.0.

## Architecture

### v0 — Overlay (current target)
```
┌──────────────────────────────────────────────────────┐
│  Tray App (WPF, hidden main window)                  │
│  ├─ ScreenShareDetector (process scan, ~1s)          │
│  ├─ WindowTracker (Win32, tracks WhatsApp window)    │
│  ├─ RegionDetector (UIA + CV fallback)               │
│  └─ OverlayWindow (transparent, topmost)             │
│         ↓                                            │
│  ┌────────────────────────────────────────────────┐  │
│  │ Capture pipeline (per frame, ~60fps)           │  │
│  │   1. GraphicsCaptureSession on WhatsApp window │  │
│  │   2. Get D3D11Texture2D frame                  │  │
│  │   3. Win2D: draw frame, blur chat region       │  │
│  │   4. Present via D3DImage                      │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
```
Tradeoff: the overlay is visible to both the user and viewers. Same
limitation as browser blur extensions. Acceptable for v0 — when sharing,
the user is presenting, not reading WhatsApp.

### v1 — Indirect Display Driver

Adds a phantom monitor (IDD, user-mode WDDM driver, C++). The user-mode app
renders the desktop with selective blur to the phantom monitor. User shares
the phantom monitor in Zoom/Teams. Real monitor untouched.

Requires code signing (EV cert). Reuses v0's region detection and blur
pipeline; only adds the driver and a framebuffer compositor.

## Module map

All under `src/`. Platform-specific code lives behind interfaces so a
hypothetical macOS port (parked, see Non-goals) can swap implementations.

| Module                                    | Lang  | Purpose                                                    |
| ----------------------------------------- | ----- | ---------------------------------------------------------- |
| `Gausslite.Core/WindowTracking`              | C#    | Find & track WA Desktop window, DPI-aware bounds           |
| `Gausslite.Core/Capture`                     | C#    | Wraps Windows.Graphics.Capture, exposes per-frame textures |
| `Gausslite.Core/Blur`                        | C#    | Win2D blur pipeline, region masking                        |
| `Gausslite.Core/Detection`                   | C#    | Find chat list & conversation rects (UIA primary, CV fallback) |
| `Gausslite.Core/ScreenShare`                 | C#    | Process-based detection of capture clients                 |
| `Gausslite.Overlay`                          | C#    | Transparent always-on-top window, D3DImage host            |
| `Gausslite.App`                              | C#    | WPF tray app, settings, hotkey, orchestration              |
| `Gausslite.Driver` *(v1)*                    | C++   | IDD driver, framebuffer pipe to user-mode app              |
| `Gausslite.Compositor` *(v1)*                | C#    | Composites desktop + selective blur for phantom monitor    |

Each `Core` module is testable through interface seams (`IWin32Api`,
`IUIAutomation`, `IProcessEnumerator`). UI/GPU code is not unit-tested;
covered by manual smoke tests.

## Milestones

### v0.1.0 — "Hello, blur" ✅ shipped
- WindowTracker finds WhatsApp Desktop, returns DPI-correct bounds
- CaptureEngine produces frames at 30+ fps
- BlurPipeline blurs the entire WA window (not yet region-aware)
- OverlayWindow renders the blurred output via D3DImage + D3D9Ex shared
  surface bridge
- TrayApp toggles blur on/off via tray menu and global hotkey
- Eager armed setup: capture+overlay prepared the moment WhatsApp's
  HWND is first seen, so restore/unocclude is a single SetWindowPos
  move and not a fresh capture session
- Occlusion-aware overlay: hides when WhatsApp is behind another
  foreground window, reappears when WhatsApp is on top again
- Manual smoke test passed

### v0.1.1 — "Gausslite" ✅ shipped
- Renamed from internal codename WAshed to Gausslite
- No functional changes
- Updated tray icon to match new branding

### v0.2.0 — "The right regions" ✅ shipped
- RegionDetector identifies chat list & conversation rects (CV-only;
  UIA path was scoped out after recon confirmed WhatsApp Desktop is a
  WebView2 shell whose chat content is invisible to UIA — see decisions
  log entry from 2026-05-02)
- Tray menu options: "Blur chat list", "Blur conversation", "Blur both"
- Tray menu intensity submenu: "Light", "Medium", "Heavy" presets
  (mapping to fixed blur-radius values). Use case: Light keeps
  chat silhouettes readable for the user while staying unreadable
  for viewers, useful since the overlay is visible to both
- BlurPipeline accepts a runtime-configurable blur radius (was
  hardcoded at 20 DIPs in v0.1.x)
- Pixel-region occlusion clipping: when WhatsApp is partially behind
  another window, blur only the visible portion instead of hiding the
  whole overlay (replaces the v0.1.0 center-point hide-all behavior)
- IAppProfile abstraction: separate WhatsApp-specific knowledge
  (window detection rules, region identification logic) from generic
  blur infrastructure. WhatsApp becomes the first concrete
  IAppProfile implementation. Code-quality refactor; no user-visible
  behavior change beyond the cleaner internal seam. If multi-app
  support is added post-v1.0, this is the interface it plugs into
- Smoke test passed on default, snap, and maximized layouts (LTR + RTL)
- **Known limitation:** internal WhatsApp layout shifts during
  otherwise-static periods (e.g. dragging the chat-list/conversation
  divider with no other interaction) may delay scope-clip update by a
  few seconds. Workaround: hover the cursor over WhatsApp. Tracked in
  issue #35; planned fix is an opt-in wall-time forced-repaint timer
  in v0.4.0 once the broader settings UI lands

### v0.3.0 — "Knows when to blur"
- ScreenShareDetector identifies which app is sharing: Zoom, Teams,
  Meet (Chrome/Edge), Discord, OBS, more as they come up
- Share-target detection: which monitor or window the sharing client
  has captured. If WhatsApp is on a monitor that is not being shared,
  no blur is needed; if the sharing client is sharing a specific
  application window other than WhatsApp, no blur is needed
- Default-blur-if-uncertain: when the share target cannot be
  determined, or when detection partially fails, blur is enabled
  rather than disabled. Privacy-first fallback
- Blur activates within 2s of share start, deactivates within 2s of
  share end
- Manual override (hotkey/tray toggle) still works in all states

**v0.3.0 first slice (in progress on branch
`v0.3.0-screen-share-detection`)**: detect "the user is *actively
sharing the screen* right now" via well-known share-control window
signatures, and auto-enable / restore blur on the share's start /
end transitions. Apps covered: Zoom desktop, Microsoft Teams desktop,
and any Chromium-based browser share (Meet, browser-Zoom, browser-Teams,
browser-Discord). Discord desktop is a known limitation — its
share-control UI is rendered as Chromium web content invisible to
window enumeration; deferred to v0.3.1 (UIA tree-walking) or, for
users who use Discord desktop primarily, the v0.4.0 "blur whenever
any sharing app is running" opt-in toggle. Share-target detection
(which monitor / window is being captured) deferred to a v0.3.x
follow-up.

### v0.4.0 — "Polish"
- Settings window (opened via tray menu → "Settings..."), separate
  from the tray menu's quick toggles. Tray menu stays as the primary
  surface for frequent actions (enable/disable, region scope,
  intensity preset); settings window covers configuration depth
- Continuous blur intensity slider in settings (replaces the v0.2.0
  Light/Medium/Heavy presets, or augments them with a custom value)
- Screen-share client checklist: which sharing apps Gausslite should
  watch for (Zoom, Teams, Meet, Discord, OBS, etc.)
- **Opt-in "blur whenever any sharing app is running" toggle.** v0.3.0's
  default is precise: blur turns on only when there's positive evidence
  of an *active* share (specific share-control window visible). This
  toggle relaxes that to "if Zoom/Teams/Discord is running at all, blur
  is on" — a heavy-handed but safe fallback. **Especially relevant for
  Discord desktop**, whose active-share state is invisible to window
  enumeration; users who use Discord desktop primarily can flip this
  on to get coverage at the cost of blur being on whenever Discord is
  open. Off by default
- Auto-start with Windows toggle (off by default)
- Optional "notify when blur is armed" toggle (off by default —
  armed-state silence remains the v0.1.0 default behavior; this
  surfaces it for users who want feedback)
- Settings persistence (currently nothing persists across launches)
- Opt-in wall-time forced-repaint timer to close issue #35 (the v0.2.0
  internal-divider-drag-during-static-periods limitation): periodic
  `IWindowTracker.RequestRepaintOfTrackedWindow()` call while blur is
  active so internal layout shifts converge without user hover. Off by
  default — small steady CPU/battery cost is the tradeoff
- Updater check (manual link to GitHub Releases for v0; auto-update
  is post-v1)

### v0.5.0 — "Notifications too"
- Blur incoming WhatsApp toast notifications during screen sharing
  (currently only the WhatsApp window is blurred; toasts that pop
  from the system tray area are not)
- Phase 1: live toast notifications. Phase 2: Action Center entries
  if the user opens it during a share

### v0.6.0 — "More than WhatsApp"
- Multi-app support via the IAppProfile interface introduced in
  v0.2.0
- Initial app set: Signal Desktop, Telegram Desktop, Discord, Slack
- Each app gets its own IAppProfile implementation handling: window
  detection, region identification (chat list / conversation), and
  any app-specific quirks
- Tray UI gains an apps section: per-app enable/disable, per-app
  region preferences

### v1.0.0 — "Real privacy"
- IDD driver: phantom monitor as a separate user-mode WDDM driver
  (C++)
- Compositor: real desktop in, selective-blur desktop out
- User shares the phantom monitor in Zoom/Teams; real monitor stays
  untouched
- Installer signs driver and app with EV cert
- Smoke test: blur invisible on real monitor, visible only in
  phantom share

## Non-goals

- **macOS port.** Parked, not rejected. The architecture splits naturally:
  core logic is platform-agnostic; capture/render/OS-integration layers are
  Windows-specific. A future Mac port (or contributor) reimplements the
  platform layer behind the same interfaces. We design with this seam in
  mind but do not build the Mac side.
- **Mobile (iOS/Android).** Out of scope. Mobile screen-sharing privacy is a
  different product.
- **Blurring arbitrary apps.** WhatsApp-specific by design through
  v0.x and v1.0. The IAppProfile abstraction introduced in v0.2.0
  could in principle support other chat clients (Signal, Telegram,
  Discord, Slack) but is not committed v0.x roadmap work; multi-app
  is a possible post-v1.0 expansion based on actual user demand.
  A generic "blur any window region during share" tool is a
  different product with different UX.
- **Cloud sync, account system, telemetry.** None. Local-only.
- **WhatsApp Web in this repo.** A Chrome extension is planned in a
  separate repo (Gausslite-Web) post-v0.

## Open questions

- For v1, which IDD sample to fork: Microsoft's IddCx sample or a
  community fork? **Decide at v0.4.0.**
- Code signing cost vs. ship-without-signing-and-accept-SmartScreen-warning
  for early v1 alphas. **Decide before v1 starts.**
- Post-v1 multi-app support: should the IAppProfile interface allow
  user-defined profiles via a simple config file, or stay code-only
  with one profile per supported app? **Defer until/unless multi-app
  becomes committed work.**

## Decisions log

Append-only. One line per decision with date and rationale.
- 2026-04-30: Renamed project from WAshed to Gausslite. Single capital
  G. Rationale: portmanteau of Gaussian + gaslighting, cleaner branding,
  drops the descriptive WhatsApp prefix. v0.1.1 tagged as the rename
  release with no functional changes.
- 2026-04-30: Roadmap restructured. v0.3.0 redefined from
  "auto-activation" to "knows when to blur" — covers both
  screen-share-client process scanning AND share-target detection
  (which monitor/window is being shared). Default-blur-if-uncertain
  is the privacy-first activation rule. v0.5.0 added (notification
  blur). v0.2.0 expanded to include the IAppProfile abstraction as
  a code-quality refactor that cleanly separates app-specific
  knowledge from blur infrastructure, plus pixel-region occlusion
  clipping deferred from v0.1.0, plus runtime-configurable blur
  intensity via a tray menu preset submenu.
- 2026-04-30: Multi-app support (Signal, Telegram, Discord, Slack)
  removed from the v0.x roadmap. Decision: IDD-before-breadth.
  Rationale: better to ship one well-supported app with the polished
  IDD privacy story than several apps on the visibly-imperfect
  overlay. If multi-app demand materializes post-v1.0, IAppProfile
  is ready; if it doesn't, no investment was wasted on a feature
  nobody wanted.
- 2026-04-30: Repo visibility = stay private through v0.x. Public
  release deferred until v0.x is feature-complete with a proper
  installer. Rationale: first impressions matter; "v0.1.1 rename
  release" is not the right front-door artifact.
- 2026-04-30: Tray menu vs. settings window split. Tray menu stays
  as the primary surface for frequent toggles through v0.x. A real
  settings window arrives in v0.4.0 for configuration depth (sliders,
  checklists, persistence). Avoid building the settings window
  earlier than v0.4.0; doing so risks designing UI before knowing
  what configuration is actually needed.
- 2026-04-28: License = AGPL-3.0. Rationale: strongest "must cite" + closes
  SaaS loophole that GPL has.
- 2026-04-28: Stack = C# (WPF) for v0, C++ for v1 driver. Rationale: WPF
  has the most mature transparent-overlay + tray app story; CsWinRT exposes
  Windows.Graphics.Capture cleanly.
- 2026-04-28: IDE = VS2022 throughout. Rationale: v1 driver development
  requires VS2022; mid-project IDE migration is worse than the friction of
  Claude Code running in a separate terminal.
- 2026-04-28: Repo private until v0.1.0 ships, then public.
- 2026-04-28: macOS parked-but-architected. Platform-specific code lives
  behind interfaces so a future port doesn't require core rewrites.
- 2026-04-29: OverlayWindow Image element must use Stretch=Fill and stretch
  alignments; default Image layout produces 0x0 element which prevents D3DImage
  from ever being painted, even though D3DImage.PixelWidth/Height are correct.
- 2026-04-30: Renamed project to Gausslite. Single capital G. Rationale:
  portmanteau of Gaussian + gaslighting, cleaner branding, drops the
  descriptive WhatsApp prefix.
- 2026-05-02: Dropped the UIA path from RegionDetector; ship CV-only.
  Original v0.2.0 milestone spec read "RegionDetector identifies chat list &
  conversation rects via UIA, with a computer-vision fallback if UIA fails."
  `tools/UiaDump` recon proved WhatsApp Desktop is a WebView2 shell — the UIA
  tree stops at the WebView2 boundary and chat content is entirely invisible to
  UIA on the current build. There is no partial-UIA scenario; the fallback path
  would be the only path. Dead-code dual paths with no tested UIA branch are
  not worth shipping. `WhatsAppRegionDetector` is CV-only.
- 2026-05-02: Region detector wiring deferred; tray "Blur region" submenu
  remains a no-op for now. The detector works correctly on the vertical divider
  but the heuristic that assigns which rect is chat-list and which is
  conversation (narrower side = chat list) fails in wide-mode layouts where the
  conversation pane is the wider one. Filed as issue #30 with a proposed fix
  (horizontal-edge density analysis). Shipping region-aware blur with a broken
  heuristic would mis-blur the wrong pane; deferring wiring is the right call.
- 2026-05-03: v0.3.0 split into smaller slices. First slice ships active-share
  detection for Zoom desktop, Microsoft Teams desktop, and Chromium-based
  browser shares (Meet, web Zoom, web Teams, web Discord). Detection is
  positive-evidence only — process running alone is NOT enough; a
  share-control window signature must be visible. Rationale: a
  "process running" rule would mean blur is on all day for users who keep
  Zoom in the system tray. Share-target detection (which monitor / window
  is being captured) deferred to v0.3.x. The "blur whenever any sharing app
  is running" alternative is deferred to v0.4.0 as an opt-in setting.
- 2026-05-03: Discord desktop sharing is a known limitation in v0.3.0 first
  slice. Recon confirmed Discord renders share controls as Chromium web
  content (`Chrome_RenderWidgetHostHWND` legacy bridge), invisible to
  EnumWindows / EnumChildWindows at any depth. UIA tree-walking can see the
  content but adds polling CPU and complexity disproportionate to the
  use-case priority. Deferred to v0.3.1 follow-up; users primarily on Discord
  desktop can flip the v0.4.0 "blur whenever any sharing app is running"
  toggle when it lands.

## Per-session checklist

Every Claude Code session that ships a module must end with:

1. All tests pass (`dotnet test` green)
2. `STATE.md` updated:
   - "Last session summary" reflects what was just built (compact;
     1-2 paragraphs)
   - "Next up" set to the next module per the module map
   - Decisions/notes added if anything non-obvious happened
3. `HISTORY.md` updated:
   - Append a new dated session entry at the top, above the previous
     entry, separated by `---`
   - Verbose narrative — full context for future reference
4. `CHANGELOG.md` updated:
   - One concise entry under `[Unreleased]` → `### Added` (or `### Changed`, `### Fixed`, etc.)
   - User-facing language, not implementation detail
   - Example: `WindowTracker module: tracks WhatsApp Desktop window bounds at 10 Hz with DPI awareness`
5. Conventional-commit message proposed for the changes
6. No auto-commits — user reviews and commits manually
