# WAshed — Project Plan

> **For Claude Code:** Read this file at the start of every session, alongside
> `STATE.md`. This file changes rarely. `STATE.md` changes every session.

## Vision

WAshed automatically blurs WhatsApp chat content when the user is sharing
their screen, so private messages aren't accidentally broadcast in meetings.

The product targets a real gap: existing solutions are browser extensions
that only work on WhatsApp Web, or generic advice ("use a second monitor",
"share a specific app"). Nobody has built a polished desktop app that does
this for WhatsApp Desktop.

## Architecture

### v0 — Overlay (current target)
```
┌─────────────────────────────────────────────────┐
│  Tray App (WPF, hidden main window)             │
│  ├─ ScreenShareDetector (process scan, ~1s)     │
│  ├─ WindowTracker (Win32, tracks WA window)     │
│  ├─ RegionDetector (UIA + CV fallback)          │
│  └─ OverlayWindow (transparent, topmost)        │
│         ↓                                       │
│  ┌──────────────────────────────────────────┐   │
│  │ Capture pipeline (per frame, ~60fps)     │   │
│  │   1. GraphicsCaptureSession on WA window │   │
│  │   2. Get D3D11Texture2D frame            │   │
│  │   3. Win2D: draw frame, blur chat region │   │
│  │   4. Present via D3DImage                │   │
│  └──────────────────────────────────────────┘   │
└─────────────────────────────────────────────────┘
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
| `WAshed.Core/WindowTracking`              | C#    | Find & track WA Desktop window, DPI-aware bounds           |
| `WAshed.Core/Capture`                     | C#    | Wraps Windows.Graphics.Capture, exposes per-frame textures |
| `WAshed.Core/Blur`                        | C#    | Win2D blur pipeline, region masking                        |
| `WAshed.Core/Detection`                   | C#    | Find chat list & conversation rects (UIA primary, CV fallback) |
| `WAshed.Core/ScreenShare`                 | C#    | Process-based detection of capture clients                 |
| `WAshed.Overlay`                          | C#    | Transparent always-on-top window, D3DImage host            |
| `WAshed.App`                              | C#    | WPF tray app, settings, hotkey, orchestration              |
| `WAshed.Driver` *(v1)*                    | C++   | IDD driver, framebuffer pipe to user-mode app              |
| `WAshed.Compositor` *(v1)*                | C#    | Composites desktop + selective blur for phantom monitor    |

Each `Core` module is testable through interface seams (`IWin32Api`,
`IUIAutomation`, `IProcessEnumerator`). UI/GPU code is not unit-tested;
covered by manual smoke tests.

## Milestones

### v0.1.0 — "Hello, blur"
- WindowTracker finds WhatsApp Desktop, returns DPI-correct bounds
- CaptureEngine produces frames at 30+ fps
- BlurPipeline blurs the entire WA window (not yet region-aware)
- OverlayWindow renders the blurred output
- TrayApp toggles blur on/off via tray menu and hotkey
- Manual smoke test passes

### v0.2.0 — "The right regions"
- RegionDetector identifies chat list & conversation rects via UIA
- CV fallback if UIA fails
- Tray menu: "Blur chat list", "Blur conversation", "Blur both"
- Smoke test on 3 different WA Desktop layouts (default, narrow, wide)

### v0.3.0 — "Auto-activation"
- ScreenShareDetector identifies Zoom, Teams, Meet (Chrome/Edge), Discord, OBS
- Blur activates within 2s of share start, deactivates within 2s of share end
- Manual override still works

### v0.4.0 — "Polish"
- Settings persistence (which screen-share clients to watch, blur intensity, regions)
- Auto-start with Windows option
- Updater check (manual link to GitHub Releases for v0; auto-update is post-v1)

### v1.0.0 — "Real privacy"
- IDD driver: phantom monitor
- Compositor: real desktop in, selective-blur desktop out
- Installer signs driver and app with EV cert
- Smoke test: blur invisible on real monitor, visible only in phantom share

## Non-goals

- **macOS port.** Parked, not rejected. The architecture splits naturally:
  core logic is platform-agnostic; capture/render/OS-integration layers are
  Windows-specific. A future Mac port (or contributor) reimplements the
  platform layer behind the same interfaces. We design with this seam in
  mind but do not build the Mac side.
- **Mobile (iOS/Android).** Out of scope. Mobile screen-sharing privacy is a
  different product.
- **Blurring arbitrary apps.** WhatsApp-specific by design. A generic
  "blur any window region during share" tool is a different product with
  different UX.
- **Cloud sync, account system, telemetry.** None. Local-only.
- **WhatsApp Web in this repo.** A Chrome extension is planned in a
  separate repo (WAshed-Web) post-v0.

## Open questions

- Should v0 ship without auto-activation (manual hotkey only) to compress
  the timeline, then add auto-activation in v0.2.0? **Tentative: yes,
  v0.1.0 is hotkey-only.**
- For v1, which IDD sample to fork: Microsoft's IddCx sample or a community
  fork? **Decide at v0.4.0.**
- Code signing cost vs. ship-without-signing-and-accept-SmartScreen-warning
  for early v1 alphas. **Decide before v1 starts.**

## Decisions log

Append-only. One line per decision with date and rationale.

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

## Per-session checklist

Every Claude Code session that ships a module must end with:

1. All tests pass (`dotnet test` green)
2. `STATE.md` updated:
   - "Last session summary" reflects what was just built
   - "Next up" set to the next module per the module map
   - Decisions/notes added if anything non-obvious happened
3. `CHANGELOG.md` updated:
   - One concise entry under `[Unreleased]` → `### Added` (or `### Changed`, `### Fixed`, etc.)
   - User-facing language, not implementation detail
   - Example: `WindowTracker module: tracks WhatsApp Desktop window bounds at 10 Hz with DPI awareness`
4. Conventional-commit message proposed for the changes
5. No auto-commits — user reviews and commits manually
