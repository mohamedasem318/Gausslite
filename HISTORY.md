# Gausslite — Session history

> Verbose archive of session-by-session development notes. Reference-only.
> For current state and forward-looking work, see STATE.md.
> For user-facing release notes, see CHANGELOG.md.

## Session history

### 2026-05-04 — v0.3.5 polish-and-ship: metadata, icon, installer, tag + Release

**Context.** v0.3.5 minimum-settings code (settings persistence, "Auto-start with Windows" toggle, "Blur on any sharing app" toggle, plus the visible-region correctness fix) was squash-merged on `main` but **not yet tagged or installable**. Six gaps stood between that state and a tagged public-beta release: no project metadata in any csproj, no embedded app icon, no Win10 support story, no Inno installer, no release-build script, no docs updates for the actual ship. This session closed all six and tagged `v0.3.5` with both artifacts on a public GitHub Release.

**Method.** Largely incremental — no architectural shifts. The session was a sequence of small targeted changes, smoke-test, fix-from-feedback, repeat, with three real bug discoveries layered on top of the metadata/installer plumbing.

---

**Project metadata via `Directory.Build.props`.** New file at the repo root sets `Authors=Mohamed Assem`, `Company=Gausslite`, `Product=Gausslite`, `Copyright=(c) 2026 Mohamed Assem`, `Version=0.3.5`, `AssemblyVersion/FileVersion=0.3.5.0`, `Description`, `NeutralLanguage=en-US`. SDK auto-generated assembly attributes pick all of this up — propagates to all 9 csprojs without any per-project changes. The `app.manifest` `assemblyIdentity version` was bumped from the placeholder `1.0.0.0` to `0.3.5.0` to match. `Gausslite.App.exe`'s Properties → Details dialog now reads `Gausslite / Gausslite / 0.3.5.0 / (c) 2026 Mohamed Assem` instead of "Unknown publisher" / blank.

**Embedded app icon.** `<ApplicationIcon>Assets\tray-icon.ico</ApplicationIcon>` added to `Gausslite.App.csproj`. The icon already shipped as a `<Content>` item (so the tray loaded it from disk at runtime); this also embeds it in the `.exe` resource table, so Explorer / Alt-Tab / installer wizard show the Gausslite icon instead of the generic .NET one.

**Windows 10 support — attempted, dropped.** Lowered TFM in 5 csprojs to `net8.0-windows10.0.19041.0` and tried gating `_session.IsBorderRequired = false` on `ApiInformation.IsPropertyPresent` plus a reflection-based setter (`typeof(GraphicsCaptureSession).GetProperty(...)?.SetValue(...)`). Smoke test on Win11 22H2 surfaced the issue: the reflection no-opped because `typeof(GraphicsCaptureSession)` resolves at *compile time* using the lowered SDK projection, which doesn't have the property; `GetProperty()` returned null even on Win11 22H2 where the runtime API exists. Result: yellow capture-indicator border was visible on Win11 — a regression vs the previous behavior. **Reverted.** TFM is back to `net8.0-windows10.0.22621.0` across all 5 production+test csprojs, `WinRTCaptureSession.IsBorderRequired` calls the property directly, the `ApiInformation` guard and reflection wrapper are gone. Win10 is **not supported** in v0.3.5; if a user asks, the principled fix is COM QI to `IGraphicsCaptureSession3` (the Win11 22H2 interface that exposes `put_IsBorderRequired`) — a v0.3.x follow-up worth ~30 minutes if real demand materializes.

**Auto-start self-heal log line.** `RegistryAutoStartManager.Enable()` now logs a `StartupLog.Info` line on successful registry write — makes the install-elsewhere self-heal (`if (settings.AutoStart && !autoStart.IsEnabled()) autoStart.Enable()` in `App.xaml.cs`) visible in `gausslite-startup.log`. The reconciliation logic itself was already correct; this is diagnostics-only.

**Inno installer (`installer/Gausslite.iss`).** Per-user install at `{localappdata}\Programs\Gausslite` (no admin / UAC). Fixed `AppId={4BEC16D5-721D-4C2B-9B71-03CBF83653C6}` baked in once and **never changed** — future installer versions will auto-upgrade old installs in place via the AppId match. `MinVersion=10.0.22621` matches the TFM floor. `[Registry]` removes the auto-start entry on uninstall (`HKCU\...\Run\Gausslite` with `Flags: dontcreatekey deletevalue uninsdeletevalue` — the installer never *creates* the value, only the in-app toggle does, but always cleans it up). `[UninstallDelete]` removes `{localappdata}\Gausslite\` (per-user state dir holding `settings.json`) plus explicit `gausslite-startup.log` / `gausslite-crash.log` entries inside `{app}` (runtime-created files Inno's `[Files]` cleanup doesn't track). `WizardSmallImageFile=wizard-small.bmp` sets the wizard top-right corner image (the user provided a 2048×2048 source which I resized to 110×106 via PowerShell + System.Drawing).

**Release build script (`build-release.ps1`).** PowerShell 7+ pipeline: reads version from `Directory.Build.props`, runs `dotnet publish -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en` (multi-file — `PublishSingleFile=true` conflicts with `EnableCoreMrtTooling=false` which the project sets to avoid VS2022 AppxPackage tooling not in the dotnet CLI SDK; multi-file publish gives a ~200 MB folder that Inno's LZMA2 ultra compresses to ~52 MB), invokes ISCC.exe (auto-discovered via `HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1`), packs the publish output as `Gausslite-0.3.5-portable.zip` via `Compress-Archive`, prints SHA-256 hashes for both artifacts.

**Bug 1 (caught in smoke test): rail-side detector flips on Shift+Alt input-language switch.** With scope=Conversation, pressing Shift+Alt to switch Windows input language caused the blur to silently jump to the chat list. Root cause: `WhatsAppRegionDetector.DetermineRailSide` uses a quiet-zone heuristic on the outer edges of the captured frame; small visual changes (a language indicator near the message-input area on the right side) push the right-edge walk past its busy threshold for a single frame, and the rail decision flips Left → Right. The detector's decision is correct for the frame it sees but wrong as a session-level signal. **Fix**: `TrayOrchestrator` now locks the rail side on the first successful detection per capture session in a new `_lockedRailSide` field; subsequent detections that disagree have their `chatListRect` and `conversationRect` swapped back into the locked orientation before storage. The lock resets on capture-session teardown or window-bounds size change — both legitimate "layout might really have changed" signals — so a real WhatsApp UI-language change is still picked up after a blur off / on toggle. 5 new tests in `TrayOrchestratorRailSideLockTests.cs` cover lock-on-first, agreement, swap-on-disagreement, no-lock-on-failure, reset-on-teardown.

**Bug 2 (caught in second smoke test): rail-side lock latches on stale frame after WhatsApp restart in different UI direction.** User restarted WhatsApp in Arabic (RTL), toggled blur off+on. Lock latched as Left (LTR) anyway, then the actual RTL detection results were swapped back to Left, mislabelling the panes. Root cause: `OnBoundsChanged` ran `RunDetection` shortly after `EnableBlur`, on whatever frame `BlurPipeline.TryReadLatestFrameAsBgra` returned — which was the previous session's last LTR frame, still in the cache because `BlurPipeline` doesn't clear `_cachedInputFrame` on capture-session teardown. Lock engaged on stale data; new RTL frames couldn't override. **Fix**: new `IBlurPipeline.ClearCachedFrame()` method that disposes the cached input frame, staging texture, AND render target. Called from `TrayOrchestrator.TearDownCaptureAndOverlay`. After teardown, `TryReadLatestFrameAsBgra` returns false until a fresh new-session frame is blurred, so RunDetection skips with "no frame available for readback" and the lock only engages on real fresh data. 5 new tests in `BlurPipelineTests` for the new method.

**Bug 3 (caught in third smoke test): NRE on every frame after DisableBlur → EnableBlur cycle.** After fix #2 shipped, the gray placeholder started appearing during normal share/restart cycles. Root cause: my first cut of `ClearCachedFrame` kept `_renderTarget` alive while disposing `_cachedInputFrame` and `_stagingTexture`, framed as a "hot-path optimization to avoid reallocation when blur re-enables on the same window". This broke an invariant in `BlurFrame`: its allocation guard is `if (_renderTarget is null || dims-mismatch)` — sees the kept render target with matching dims, decides nothing needs to be allocated, then calls `UpdateCachedFrame(_canvasDevice, _cachedInputFrame, frame)` with `_cachedInputFrame = null`. NRE on every frame for the rest of the session, no frames ever blurred, the placeholder stays up. **Fix**: dispose all three of `_renderTarget`, `_cachedInputFrame`, `_stagingTexture` in `ClearCachedFrame`. Restores the "all three or none" invariant. The "hot-path optimization" claim was wrong and added a new test (`ClearCachedFrame_DisposesRenderTargetToo_AndNextBlurFrameReallocatesCleanly`) that directly exercises the clear-then-blur cycle and asserts no exception, so this regression class can't escape silently again.

**Defender ASR finding.** Fourth-iteration smoke test surfaced that Microsoft Defender's *"Use advanced protection against ransomware"* Attack Surface Reduction rule (GUID `c1db55ab-c21a-4637-bb3f-a12568109d35`) silently blocks the unsigned binary on machines that have the rule enabled. The block manifests as a generic Windows *"Windows cannot access the specified device, path, or file"* dialog with no SmartScreen-style "Run anyway" path. ASR is **off by default** on consumer Windows; only on in security-managed enterprise setups. **The block is independent of Microsoft's malware engine** — submitting `GaussliteSetup-0.3.5.exe` to https://www.microsoft.com/en-us/wdsi/filesubmission cleared the file ("scanners show no positive detection") but ASR's proactive heuristic is a separate gate that blocks unsigned, low-prevalence executables doing ransomware-shaped things (writing to `%LOCALAPPDATA%`, writing to `HKCU\...\Run`, enumerating windows, capturing screen content). Documented in CHANGELOG known-limitations + README's SmartScreen blockquote with the `Add-MpPreference -AttackSurfaceReductionOnlyExclusions` workaround. Real fix is code signing; SignPath.io OSS application submitted as part of this session.

**Recording-during-share limitation discovered.** When the user attempted to record a demo gif of Gausslite blurring during a Zoom share, the recording tool's region-selection overlay (ScreenToGif-style) sat on top of WhatsApp in Z-order. `ComputeVisibleRegion` correctly subtracted its rect from the visible region (it's not `WS_EX_TRANSPARENT`), so visible region went to 0 → orchestrator hid the overlay → recording captured unblurred WhatsApp content. Decided **not** to fix in v0.3.5 because every fix would either re-introduce a privacy regression (any "ignore Z-order during share" override re-introduces the v0.3.5 minimum-settings privacy bug) or require maintenance overhead (recording-tool window-class allowlist). Documented as a v0.4.0 candidate ("Demo recording mode" toggle, off by default, with a tray balloon explaining it disables privacy protections). For the GIF demo itself, recommended OBS / Game Bar / Snipping Tool which capture without an on-top overlay.

**Locale-trim publish optimization.** Added `-p:SatelliteResourceLanguages=en` to `dotnet publish`. Drops the ~25 WPF framework satellite resource DLLs that the app would never use (zh-Hans, zh-Hant, ja, ko, ru, fr, de, es, ...). Net savings: ~6 MB on the portable zip, ~2 MB on the installer. Trade-off: WPF system-dialog strings on non-English Windows fall back to English instead of the user's locale. Acceptable for v0.x.

**README + CHANGELOG + STATE updates.** README got a three-badge bar (license, latest release, Windows version), the ASR caveat in the SmartScreen blockquote, v0.3.5 added to the roadmap, and the placeholder hints removed (real `demo.gif` + `tray-menu.png` now live in `docs/media/`). CHANGELOG `[Unreleased]` → `[0.3.5] - 2026-05-04` with Added / Changed / Fixed / Known limitations / Security sections. STATE got a fresh "Current milestone" header and a polish-and-ship session summary.

**Tests.** +5 `TrayOrchestratorRailSideLockTests` (lock semantics: latch, agree, swap-on-disagree, no-lock-on-failure, reset-on-teardown). +5 `BlurPipelineTests` for `ClearCachedFrame` (no-op when empty, disposes correctly, render-target invariant, dispose-after-dispose safety, full clear-then-reallocate cycle). Total: Core 145/145, App 116/116 (x64). Build clean (0 errors, 1 expected Win2D AnyCPU warning).

**Final artifacts.** `installer/Output/GaussliteSetup-0.3.5.exe` (~52 MB, SHA-256 `5E8745…`) + `Gausslite-0.3.5-portable.zip` (~74 MB, SHA-256 `DF1D27…`).

**Tagged + released.** `v0.3.5` annotated tag pushed to origin. GitHub Release at https://github.com/mohamedasem318/Gausslite/releases/tag/v0.3.5 with both artifacts attached and SHA-256 hashes in the notes body. SignPath.io OSS application submitted at https://signpath.org/apply (not the URL I had previously — `about.signpath.io/product/open-source` was outdated; the actual form lives at signpath.org/apply and is a HubSpot-embedded multi-field application). 1-2 week vetting; signed v0.3.6 release follows when approved.

**Per-release checklist (added to STATE).** Every public release should: (1) build artifacts via `build-release.ps1`; (2) submit *both* `GaussliteSetup-X.Y.Z.exe` *and* `Gausslite.App.exe` (from `publish/`) to Microsoft for analysis (different file hashes; both need clearing); (3) tag and create the GitHub Release with both artifacts attached + SHA-256 hashes in the notes; (4) submit signed binaries to Microsoft after v0.3.6 if any user reports their AV blocking.

---

### 2026-05-03 — v0.3.5 minimum settings for beta

**Context.** v0.3.0 has shipped (PR #37) and the security audit landed (PR #42).
The original sequence was "audit → installer → tag → public push", but on
reflection there are three gaps that would make a beta release frustrating
for testers:

1. **Nothing persists across launches.** The user picks intensity Heavy and
   region scope Conversation; next launch they're back to Medium / Both.
2. **Discord desktop has no detection workaround.** Issue #38 (Discord
   share-controls invisible to window enumeration) is real, the proposed UIA
   fix is non-trivial, and the v0.4.0 "blur whenever any sharing app is
   running" toggle was the documented escape hatch for users who use Discord
   primarily. Without that toggle, Discord-desktop users have nothing.
3. **No auto-start.** Beta testers expect the privacy app they've installed
   to be running when they sit down at their machine.

The full v0.4.0 polish pass (continuous slider, settings *window*, share-client
checklist, armed-state notification toggle, repaint timer, updater) is
4-6 sessions of work and would design those things before having any real
user feedback. So we carved the three load-bearing items out as v0.3.5,
ship the installer after this PR, and hold v0.4.0 until first beta feedback.

Distribution plan: low-key beta — friends, possibly LinkedIn. Not Reddit /
HN. Public GitHub repo, public GitHub Release, but no aggressive promotion.

**Method.** v0.3.5 is wholly incremental — no architectural shifts, no new
abstractions, just plumbing three settings through the existing tray surface.

---

**Settings persistence layer.**

- `Gausslite.Core.Settings.Settings` — immutable record with init-only
  properties. Defaults: Intensity=Medium, Scope=Both, AutoStart=false,
  ProcessRunningHeuristicEnabled=false. `with`-style updates (`_settings with
  { AutoStart = true }`) keep mutations explicit.
- `ISettingsStore.Load() / Save(Settings)` — the persistence seam. Lives in
  Core because consumers are platform-agnostic; implementations live in App
  because storage location is OS-specific.
- `JsonSettingsStore` (in `Gausslite.App.Persistence`) writes to
  `%LOCALAPPDATA%\Gausslite\settings.json`. Per-user, no admin, survives
  reinstalls. JSON with camelCase property names + `JsonStringEnumConverter`
  so users sending the file with bug reports can read it. WriteIndented for
  the same reason.
- Error handling: missing file / empty file / corrupt JSON / literal `null`
  all degrade to `new Settings()` defaults via try/catch in `Load`. Save
  failures return `false` and log via `StartupLog`. Settings IO must never
  crash the app; first-run-on-corrupt is the worst case and is acceptable.

**Namespace gotcha.** Initially put `JsonSettingsStore` in `Gausslite.App.Settings`
to mirror the Core namespace. Compiler refused: `Gausslite.App.Settings` is the
current namespace, so the unqualified token `Settings` resolved to the
namespace, not the type from `using Gausslite.Core.Settings;`. Renamed the
App-side folder + namespace to `Persistence` (which better describes the
concern anyway — App is about *how* settings are persisted, not about *what*
they are).

7 new tests in `JsonSettingsStoreTests`: defaults on missing file, full
round-trip, corrupt JSON returns defaults, empty file returns defaults,
literal "null" returns defaults, partial JSON fills with defaults, parent
directory auto-created on save. All use a temp-file path via the internal
ctor so the tests never touch the real `%LOCALAPPDATA%` folder.

---

**Auto-start with Windows.**

- `IAutoStartManager.IsEnabled() / Enable() / Disable()` interface in
  `Gausslite.App.Persistence`.
- `RegistryAutoStartManager` writes value `Gausslite` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` = `"<full path to
  current .exe>"`. Per-user hive — no UAC, no admin needed.
- `IsEnabled()` does an exact case-insensitive match against the current
  executable path, so a moved or renamed binary reads as "not enabled" and
  the user can re-toggle to update. This handles the "user reinstalled to a
  different folder" case without any complex migration logic.
- `Enable()` is idempotent (overwrites any existing value); `Disable()` is
  idempotent (no-op if the value isn't there).
- All registry exceptions caught — failures log via `StartupLog.Warn` and
  return `false`. The tray menu reverts the visual checkbox if the registry
  write fails.
- Reconciliation at startup: in `App.OnStartup`, after loading Settings, if
  `Settings.AutoStart != autoStart.IsEnabled()`, call `Enable()` or
  `Disable()` to align. Cheap (one registry read + maybe one write); ensures
  the registry never silently diverges from the user's stored intent.

No unit test for `RegistryAutoStartManager` — it's nearly pure delegation to
`Microsoft.Win32.Registry`, and adding a test would mean either touching the
real `HKCU` (fragile, could leave junk behind) or extracting an
`IRegistryKey` abstraction just to mock it. The smoke test ("toggle on →
restart → app is running") covers the path.

---

**"Blur on any sharing app" toggle.**

The tricky decision was whether to write a separate detector or extend the
existing one. The existing `WindowSignalScreenShareDetector` already
enumerates all visible windows on each tick — we'd be enumerating twice if
we ran a separate process-list detector in parallel. So extension wins:

- New static `WindowSignalScreenShareDetector.DefaultTriggerProcessNames`
  constant: `{ "Zoom", "ms-teams", "Discord" }`. Browsers explicitly
  excluded; the constant is exported so the tray-menu code uses the same
  source of truth.
- New mutable field `_triggerProcessNames` (default empty). Toggle on flips
  it to `DefaultTriggerProcessNames`; toggle off flips it back to empty.
  Read once at the top of each `Poll()` so a flip mid-poll can't tear.
- `Poll()` now has two phases per visible window: phase 1 = strict
  signature match (highest-quality evidence, never false-positive); phase 2
  = process-name match against the trigger list (heavy-handed; only
  consulted when the toggle is on). Phase 1 always wins if both fire on the
  same window — the signature match produces a more informative AppName
  ("Zoom") vs the heuristic AppName ("Zoom (process heuristic)").
- No change to the transition-only event semantics: `StateChanged` still
  fires only on Idle↔Active transitions.

Trade-off: when the toggle is on, the user pays a process-name string
comparison loop per visible window per tick. With ~50 visible windows and 3
trigger names, that's 150 case-insensitive equality checks per second. Free.

7 new detector tests: heuristic-off respects signature only; heuristic-on
triggers on process match; heuristic-on with no matching process stays
idle; case-insensitive process matching; signature precedence over process
match when both apply; mid-session flip-off transitions Active→Idle on next
poll; default trigger list includes Zoom/Teams/Discord and excludes
chrome/msedge.

---

**Tray menu wiring.**

`TrayIconHost`'s constructor went from 2 args (`orchestrator, notifier`) to 6
(`orchestrator, settingsStore, autoStart, detector, initialSettings,
notifier`). No existing tests break because there are no `TrayIconHost`
unit tests — it's pure WPF wiring.

Menu layout:

```
Enable blur            ✓
Blur intensity         ►
Blur region            ►
─────────────
Auto-start with Windows
Blur on any sharing app
─────────────
Exit
```

The intensity / scope click handlers also save `Settings` now via
`PersistSettings(_settings with { Intensity = preset })`. Toggle-with-failure
handling for auto-start: WPF's checkable `MenuItem` flips `IsChecked` *before*
the click event fires, so we read the new state, attempt the registry write,
and revert `IsChecked` if the write failed. Visual stays in sync with reality.

The "Blur on any sharing app" menu item has a tooltip explaining the
trade-off: "Treats any running Zoom / Teams / Discord desktop as an active
share. Catches Discord desktop sharing (which is invisible to window
detection); downside: blur turns on whenever those apps are running."

---

**Composition root changes (`App.xaml.cs`).**

Order of construction now matters because settings drive other components:

1. `JsonSettingsStore` → `settings = settingsStore.Load()`.
2. `RegistryAutoStartManager` → reconcile with `settings.AutoStart`.
3. (Existing: D3D11 device, profile, tracker, capture, blur, overlay,
   region detector.)
4. `TrayOrchestrator` constructed.
5. `WindowSignalScreenShareDetector` constructed.
6. `_orchestrator.SetIntensity(settings.Intensity)` and `SetScope(settings.Scope)`
   apply persisted values to runtime state.
7. If `settings.ProcessRunningHeuristicEnabled`, call
   `screenShareDetector.SetTriggerProcessNames(DefaultTriggerProcessNames)`.
8. `TrayIconHost(_orchestrator, settingsStore, autoStart, detector, settings, …)`
   reads everything to build the menu with correct check states.

---

**Tests.** 14 new (7 Core for heuristic, 7 App for JSON store). Total now
Core 135/135, App 105/105 on x64. Build clean (0 errors, 1 expected Win2D
AnyCPU warning). `dotnet restore --locked-mode` clean.

---

**Smoke-found bug: overlay leaks on top of unrelated app when WhatsApp is hidden during share.**

Smoke test of the new heuristic toggle exposed a privacy regression. Repro:

1. WhatsApp open and visible.
2. Toggle "Blur on any sharing app" ON. ms-teams is running (not actually sharing).
3. Heuristic fires → auto-enable blur. Overlay shows on WhatsApp.
4. Click another app (Edge, etc.) that fully covers WhatsApp's screen area.
5. **Bug:** the always-on-top blur overlay stays on screen, painting blurred
   WhatsApp pixels on top of Edge — and those leaked pixels would be in the
   shared stream during a real share.

Root cause: the v0.3.0 occlusion-override (`if (fullyOccluded && !_shareIsActive)
{ hide; }` — "during share, ignore the fully-occluded report") was designed to
handle Zoom's many small tile overlays stacking and producing a false-positive
"fully occluded" Z-order subtraction. But it fires for ALL fully-occluded cases
during share — including legitimate full occlusion (Edge fullscreen, another
fullscreen app, virtual desktop switch). The override's intended privacy
benefit (over-blur Zoom-tile-covered areas) becomes an *outward* leak when
WhatsApp is genuinely hidden behind something else.

Plus: at line 736 of `OnVisibleRegionChanged`, when minimized-during-share, the
code transitions to Armed state but forgets to call `HideOverlay` — the stale
blurred frame stays on screen at WhatsApp's last-known coordinates.

**Fix (final, after FIVE iterations — this took embarrassingly long).**
The first four iterations all added increasingly-clever heuristics in the
orchestrator to work around a fundamentally wrong visible-region. The fifth
iteration finally fixed it at the source.

**Root cause:** `ComputeVisibleRegion` walked the Z-order subtracting every
covering window's `GetWindowRect`, but didn't filter `WS_EX_TRANSPARENT`
windows. Win32's `WindowFromPoint` skips `WS_EX_TRANSPARENT` (it's the same
flag our own overlay uses for click-through), and *visually* those windows
don't block what's beneath them. Zoom drops transparent annotation/share-host
overlays during a share whose `GetWindowRect` spans the screen; the Z-order
subtraction wrongly took those as opaque coverage and reported WhatsApp's
visible region as empty. The v0.3.0 orchestrator override ("during share,
ignore the empty region") was a workaround for that.

**Final fix:** add `WS_EX_TRANSPARENT` to the same filter that already skips
`WS_EX_TOOLWINDOW`. Now the visible region matches what `WindowFromPoint`
sees and what the user sees: Zoom transparent overlays are skipped → region
stays full → blur covers WhatsApp. Spotify opaque covers are subtracted →
region shrinks or empties → overlay clips or hides. **No orchestrator
override needed; removed all of them.**

`EffectiveVisibleRegion()` is now just `_windowTracker.VisibleRegion`.
`OnVisibleRegionChanged` hides whenever the region is empty OR
`IsLikelyFullyHidden` OR not-present-or-minimized — no more `_shareIsActive`
branches. `HasVisibleRegion()` is `IsLikelyFullyHidden ? false : region.Count > 0`.

`IsLikelyFullyHidden` (the `WindowFromPoint` sampling I added in earlier
iterations) is kept as a defensive backup for the rare case the Z-order
walk silently misses a covering window entirely (some Chromium-compositor
apps have non-standard rendering). Primary signal is the visible region;
this is the safety net.

The first attempt used a *geometric* heuristic: "set the flag when any single
covering window's `GetWindowRect` fully contains WhatsApp's bounds." That
seemed reasonable on paper — Edge fullscreen does cover WhatsApp's bounds, a
single Zoom tile does not. But the second smoke test (real Zoom share) showed
this re-introduced the original v0.3.0 bug: during a Zoom share Zoom drops
multiple `WS_EX_TRANSPARENT` overlays (the annotation layer, the share-host
wrapper, etc.) whose `GetWindowRect` spans the entire monitor. Geometrically
those *do* fully contain WhatsApp; visually they're click-through. The flag
flipped true and the overlay stayed parked offscreen for the entire share.

Switched to `WindowFromPoint` sampling. `WindowFromPoint` is Win32's
authoritative "what window is visually on top here?" — and it automatically
skips `WS_EX_TRANSPARENT` windows (this is what `IsHitTestVisible=false`
ultimately maps to). Sample 5 points inside WhatsApp's rect (4 inset corners
+ center). If any sample resolves to a window owned by WhatsApp's process,
WhatsApp has at least some pixels visible to viewers — treat it as not fully
hidden. If none do, WhatsApp is genuinely behind a fullscreen non-transparent
window — hide the overlay.

This works correctly for all three real-world cases:

- **Edge fullscreen** (every sample returns Edge): `IsLikelyFullyHidden = true`
  → overlay hides. ✓
- **WhatsApp minimized** (window is at -32000, samples are off-screen,
  WindowFromPoint returns IntPtr.Zero or another window):
  `IsLikelyFullyHidden = true` → overlay hides. ✓ (also caught by IsMinimized
  check.)
- **Zoom-during-share with transparent overlays + small tiles** (samples
  pass through transparent overlays and resolve to WhatsApp):
  `IsLikelyFullyHidden = false` → override keeps overlay visible. ✓

Implementation:

- `WindowTracker.ComputeVisibleRegion` returns `(visibleRects, likelyFullyHidden)`
  tuple. Z-order subtraction logic unchanged. The flag is computed afterward
  via `AnySamplePointResolvesToWhatsApp` which calls `WindowFromPoint` for 5
  inset/center points and checks if any resolves to WhatsApp's root.
- `WindowTracker.PollLoop` writes `IsLikelyFullyHidden` on every sample, and
  fires `VisibleRegionChanged` on its transitions even if the rect list itself
  didn't change. (Third-iteration fix — see "Spotify case" below.)
- `IWindowTracker.IsLikelyFullyHidden` exposed as a property.
- `TrayOrchestrator.OnVisibleRegionChanged` hides whenever
  `_windowTracker.IsLikelyFullyHidden` is true, regardless of share state or
  rect count. The v0.3.0 share-active "ignore fully-occluded" override is
  removed — `WindowFromPoint` handles the Zoom transparent-overlay case
  correctly by construction (transparent overlays are skipped, sample resolves
  to WhatsApp, flag stays false). Also fixed the missing `HideOverlay` at the
  minimize-during-share path (orig line 736).
- `TrayOrchestrator.HasVisibleRegion` returns false whenever
  `IsLikelyFullyHidden` is true, so the rest of the orchestration pipeline
  (clip composition, `ApplyVisibilityForCurrentWindow`) sees consistent state.

**Third iteration: the Spotify case (variant 1, full cover).**  Second smoke
test exposed yet another case: with Zoom share active and Spotify opened to
cover WhatsApp, the overlay stayed on top covering Spotify.  The log showed
*zero* `VisibleRegionChanged` events for ~20 seconds while Spotify was
covering — the Z-order walk in `ComputeVisibleRegion` silently failed to
subtract Spotify's bounds (Chromium-based apps with compositor-mediated
rendering can confuse `GetWindowRect`-based subtraction).  Two changes:

1. `WindowTracker` fires `VisibleRegionChanged` whenever `IsLikelyFullyHidden`
   transitions, not only when the rect list changes.
2. `TrayOrchestrator.OnVisibleRegionChanged` hides whenever
   `IsLikelyFullyHidden` is true at the top of the handler.

**Fourth iteration: the Spotify case (variant 2, PARTIAL cover) — the real
underlying bug all along.**  Third smoke test, third Spotify report.  The
log showed:

```
visible region changed to 1 rect(s) (likelyFullyHidden=False)
OnVisibleRegionChanged: applying region (1 rect(s))
clip-compose bounds=192,217,1316,818 ... captureRect=(none) scopeRect=(none)
OverlayWindow.SetClip: clip cleared
OnVisibleRegionChanged: full visibility, no scope — clip cleared
```

This time the Z-order walk DID see Spotify — visible region went to a single
rect representing the uncovered area.  `IsLikelyFullyHidden` correctly stayed
false (samples in uncovered area resolved to WhatsApp).  Orchestrator
correctly didn't hide.  But then `RecomputeAndApplyClip` *cleared the clip*
and reported "full visibility".  The overlay then painted across the entire
WhatsApp area, including over Spotify.

Root cause: `EffectiveVisibleRegion()` had the v0.3.0 share-active override
firing for ALL share-active states:

```csharp
// OLD (wrong):
if (_shareIsActive && !_windowTracker.IsLikelyFullyHidden && _lastKnownBounds.HasValue)
    return new[] { _lastKnownBounds.Value };
return _windowTracker.VisibleRegion;
```

Designed for the Zoom-tile false-positive case where the tracker's *empty*
region was wrong.  But it discards the tracker's region unconditionally
during share — including legitimate partial-occlusion reports like "Spotify
covers half of WhatsApp."  The clip composition then sees full bounds,
optimises to "no clip," and the overlay paints everywhere.

Fix: override only the empty-region case.

```csharp
// NEW:
var trackerRegion = _windowTracker.VisibleRegion;
bool emptyRegion = trackerRegion is null || trackerRegion.Count == 0;
if (emptyRegion
    && _shareIsActive
    && !_windowTracker.IsLikelyFullyHidden
    && _lastKnownBounds.HasValue)
{
    return new[] { _lastKnownBounds.Value };  // Zoom-tile false-positive only
}
return trackerRegion;
```

Now: tracker reports `[1 rect]` for the Spotify case → that rect goes through
unchanged → `SetClip` receives it → overlay only paints in the uncovered area
→ Spotify shows through clean.  Same logic in non-share manual blur (which
the user explicitly required works).  Aligned `HasVisibleRegion()` with the
same priority.

Tests for THIS iteration:
- `ShareActive_NonEmptyRegionButLikelyFullyHidden_OverlayHides` (3rd-iter)
- `NoShare_NonEmptyRegionButLikelyFullyHidden_OverlayHides` (3rd-iter)
- `ShareActive_PartialOcclusion_AppliesClipToVisibleRegion_NotFullBounds`
  (4th-iter — locks down "tracker partial region must reach SetClip during
  share, not get discarded")
- `ManualBlur_PartialOcclusion_AppliesClipToVisibleRegion` (4th-iter — same
  for manual blur, the user's "even in normal manual cases" requirement)

Bonus fix: `WindowTracker.Stop()` now waits for the poll task to drain (1 s
timeout) before clearing observable state. Previously it cancelled the CTS
and returned immediately, which let the poll thread fire one more event after
the caller had already started reading event-list state — causing a
pre-existing `Collection was modified during enumeration` flake in
`WindowTrackerMinimizedTests` that the timing shift in this PR exposed
reliably.

4 new Core tests in `WindowTrackerTests` (no-covering → false, partial-cover
→ false, full-cover-by-opaque → true, transparent-fullscreen-overlay → false
locks down the v3 regression that bit the geometric heuristic). 5 new App
tests in `TrayOrchestrator*Tests` covering the v3 hide path (Edge / Spotify
full cover, share + manual) and the v4 clip path (Spotify partial cover,
share + manual).  Existing 16 `TrayOrchestratorScreenShareTests` all still
pass.

Bumped two timing-sensitive `WindowTrackerTests`/`WindowTrackerMinimizedTests`
delays from 60 ms → 150 ms; the extra `WindowFromPoint` work per poll made
them flake under thread-pool contention with the suite running concurrently.

Final test count: Core 139/139, App 110/110. (Was 135 + 105 before this fix.)

---

**Files modified:**
- New: `src/Gausslite.Core/Settings/Settings.cs`,
  `src/Gausslite.Core/Settings/ISettingsStore.cs`.
- New: `src/Gausslite.App/Persistence/JsonSettingsStore.cs`,
  `IAutoStartManager.cs`, `RegistryAutoStartManager.cs`.
- Modified: `src/Gausslite.Core/ScreenShare/WindowSignalScreenShareDetector.cs`
  (process-name heuristic phase 2).
- Modified: `src/Gausslite.App/Tray/TrayIconHost.cs` (constructor expansion +
  two new menu items).
- Modified: `src/Gausslite.App/App.xaml.cs` (composition root).
- New: `tests/Gausslite.App.Tests/Persistence/JsonSettingsStoreTests.cs`.
- Modified: `tests/Gausslite.Core.Tests/ScreenShare/WindowSignalScreenShareDetectorTests.cs`
  (7 new heuristic tests).
- Modified: `src/Gausslite.Core/WindowTracking/IWindowTracker.cs` (new
  `IsLikelyFullyHidden` property).
- Modified: `src/Gausslite.Core/WindowTracking/WindowTracker.cs`
  (`ComputeVisibleRegion` returns tuple with flag; `Stop()` awaits poll task).
- Modified: `src/Gausslite.App/Orchestration/TrayOrchestrator.cs` (gated
  occlusion override; missing `HideOverlay` at minimize-during-share path).
- Modified: `tests/Gausslite.Core.Tests/WindowTracking/WindowTrackerTests.cs`
  (destructure new tuple return; 3 new tests for the flag).
- Modified: `tests/Gausslite.App.Tests/TrayOrchestratorScreenShareTests.cs`
  (1 new test locking down the bug fix).
- Modified: `PLAN.md`, `STATE.md`, `HISTORY.md`, `CHANGELOG.md`.

---

### 2026-05-03 — Pre-public-release security audit + privacy hardening

**Context.** v0.3.0 first slice ("auto-blur on active screen share detection")
is on `main` (PR #37) but not yet tagged or publicly released.  Per the
pre-release sequence in STATE.md, the next step before the installer + tag
+ GitHub Release work is a security audit.  The reasoning: this is a privacy
app — a log file that leaks browser tab titles is worse than no app at all,
and we don't want to package binaries we haven't reviewed.

**Method.** Three Explore subagents in parallel:
- one mapped every `[DllImport]`, `Marshal.*`, vtable-dispatch site, and COM
  pointer lifecycle in `src/Gausslite.*`;
- one inventoried all disk writes, tracked what each log line records,
  and checked the privilege model + `app.manifest`;
- one inventoried NuGet dependencies, scanned for secrets-in-repo and
  secrets-in-history, and audited `.gitignore` coverage.

Their findings were then critically reviewed before deciding the punch list —
several agent severities turned out to be overstated, and one finding
("phantom" HSTRING leak on `WindowsCreateString` failure) didn't survive
contact with the MSDN docs.  The user's standing instruction is "be critical
and push back on weak findings"; that filter was applied throughout.

---

**[H1] Privacy leakage in `gausslite-startup.log`.**

`gausslite-startup.log` ships next to the .exe and travels with the user — if
they zip up the install dir to email a bug report, the log goes with it.  Two
log lines were leaking content of windows the user owns but Gausslite doesn't:

- `CaptureItemFactory.FindProfileWindow` (line 138) was logging the full
  window title of up to 20 examined non-matching visible windows during the
  first-call diagnostic enumeration.  Browser titles include page titles
  ("Inbox - jane@…"); Office titles include document filenames; chat apps
  include the most recent conversation name.  None of that needs to be on
  disk.
- `WindowSignalScreenShareDetector.Poll` (line 97) was logging the full
  window title of the matched share-control window on every Idle→Active
  transition.  For Zoom and Teams that title is a stable signature string;
  for the Browser signature it's `<host>.com is sharing your screen.` —
  which leaks the meeting host's domain.

**Fix — keep diagnostic value, drop the leaky bits.**  For the per-examined-
window log, replaced `title={title}` with `title.len={n}`; process and class
remain because both are needed to debug "why didn't WhatsApp match" (the
profile predicate inspects all three).  For the share-detected log, dropped
the title field entirely; AppName + WindowClass already uniquely identify
which signature fired (each signature has a distinct `(AppName, ClassName)`
pair).  The "active share ended" log was already a fixed string, no change.

Worth noting: the matched-WhatsApp log line in `CaptureItemFactory`
(`if (match)` branch) still logs `process={procName}, class={className}` —
that's WhatsApp by definition (the only window the profile predicate matches),
so it's non-leaky.

---

**[H2] HWND validation gap before `CreateForWindow`.**

Between `FindProfileWindow` returning an HWND and the next line consuming it
in `interop.CreateForWindow`, the kernel can in principle destroy the window
and reuse the HWND value for an entirely different process's window.  The
practical probability is low (HWND recycling typically takes longer than the
microsecond gap between two adjacent statements), but the impact for a privacy
app is severe: capturing a recycled HWND means blurring some unrelated app's
window thinking it's WhatsApp — the exact inverse of the privacy contract.

**Fix.**  Added `NativeMethods.IsWindow(hwnd)` plus a `GetClassName` re-check
immediately before `CreateForWindow`.  If `IsWindow` returns false (HWND is
no longer a valid window) or the class differs from what `FindProfileWindow`
saw (HWND has been recycled to a same-PID-different-class window or a
different process's window), `TryCreateForProfile` returns false and the
calling polling loop retries on the next tick.  No new public surface added —
the check uses the existing `CaptureItemFactory.NativeMethods` private
P/Invoke wrapper.

No unit test added.  `CaptureItemFactory` does P/Invoke directly today;
making it test-mockable would be a much bigger refactor than the fix itself.
The smoke test exercises the happy path, and the failure-path branch is a
straightforward early return.

---

**[M1] Startup log unbounded within a single session.**

`StartupLog` (and the parallel `DiagLog` in `Gausslite.Core` writing to the
same file) truncates the log on each app launch (`File.WriteAllText(LogPath,
string.Empty)` in the static ctor).  But there's no in-session cap.  A tray
app running for days with frequent screen-share transitions could in
principle accumulate megabytes of log content.  Realistic write rate is
low (transition-driven, not per-frame), so this is a misbehavior safety net,
not an active concern.

**Fix.**  Both classes now run a `FileInfo.Length` check under their own
intra-process lock before each append; when the existing file is over 5 MB
they truncate it and write a single header line indicating the truncation,
then continue with the new entry.  The cross-class race window (App's
`StartupLog` and Core's `DiagLog` racing on the same file) is harmless: worst
case is two truncate-headers in rapid succession, which is fine.  No
cross-assembly synchronisation needed — the OS handles file-level concurrency
via `FileShare.Read` + `FileMode.Append`.

The crash log (`gausslite-crash.log`, written from `App.xaml.cs.LogCrash`) is
left alone.  Crashes are rare enough that a multi-MB crash log would itself be
a separate bug worth investigating; truncating it would lose information
needed to diagnose the recurring crash.

---

**[M2] NuGet dependency graph not locked.**

Direct package references (Win2D, Hardcodet.NotifyIcon, System.Drawing.Common,
SharpDX, NSubstitute, xUnit, etc.) were pinned to specific versions, but
transitive dependencies could float across `dotnet restore` runs — opening a
small supply-chain attack window if a transitive package version were ever
compromised on NuGet.

**Fix.**  Set `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`
in the 5 production + test csprojs (`Gausslite.App`, `Gausslite.Core`,
`Gausslite.Overlay`, `Gausslite.App.Tests`, `Gausslite.Core.Tests`).  Ran
`dotnet restore --force`, generated `packages.lock.json` in each project
(982/34/956/1127/1075 lines respectively), committed all 5 lock files.
`dotnet restore --locked-mode` now succeeds; `dotnet build --configuration
Release` is clean (0 errors, 1 expected Win2D AnyCPU warning); tests pass
(Core 128/128, App 98/98).

Tools projects (`tools/ShareProbe`, `tools/DiscordProbe`, `tools/UiaDump`,
`tools/RegionDump`) are intentionally NOT locked — they're diagnostic-only,
never shipped, and changes there are rare; lockfile maintenance burden isn't
worth it.

---

**[L2] Belt-and-suspenders `*.log` ignore.**

Runtime logs live in `bin/.../` which is already covered by the `[Bb]in/`
gitignore entry.  Tool recon outputs are covered by `tools/.gitignore`'s
`*.log` rule.  Both already work; this is theoretical defence against a
developer dropping a `.log` somewhere unexpected.  Added a single `*.log`
line to root `.gitignore`.

---

**[M3] `D3DImageBridge` `GetObjectForIUnknown` consistency — investigated, deferred.**

This was the only finding flagged as a potential code change, and the user
chose option C ("investigate first, decide after") rather than committing
to either fix-now or defer-now up front.

The two sites in question:
- `D3DImageBridge.cs:149` — `(IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(dxgiAccessPtr)`
- `D3DImageBridge.cs:166` — `(IDXGIResource)Marshal.GetObjectForIUnknown(dxgiResPtr)`

Both use the `GetObjectForIUnknown` + managed-cast pattern that v0.2.0 banned
in `src/Gausslite.Core/Blur/` after fixing six analogous sites there.  The
v0.2.0 commit (#33, `aa74da6`) explained the bug class:

> CsWinRT IDirect3DSurface projection is alive and registered in the
> ComWrappers global table, so GetObjectForIUnknown returns the managed
> projection rather than a raw RCW.  Casting that projection to a private
> COM interface routes through its CCW which has no entry → E_NOINTERFACE
> → InvalidCastException ~90% of calls (GC-timing-dependent).

But that commit also identified TWO subclasses:

- **Sites A/B/C** (cast to `IDirect3DDxgiInterfaceAccess`): "worked by luck
  in WARP/production (tear-off has different IUnknown) but violated the
  pattern."
- **Sites D/E/F** (cast to `ID3D11Device` after `GetInterface`): "throws in
  production because the `ID3D11Device*` returned by `GetInterface` shares
  IUnknown with the registered WinRT device on hardware GPU."

Sites A/B/C didn't actually fail because `IDirect3DDxgiInterfaceAccess` is
a documented Microsoft tear-off — its `QueryInterface` returns a pointer with
a distinct IUnknown identity, so `GetObjectForIUnknown` returns a fresh RCW
(not the registered surface projection), and the cast succeeds.  Sites D/E/F
DID fail because `IDirect3DDevice`'s identity collapses with `ID3D11Device`'s
IUnknown by D3D runtime design (one device per process).

Mapping the two `D3DImageBridge` sites onto these classes:

- **Site 149 = same as A/B/C.**  Cast to `IDirect3DDxgiInterfaceAccess`
  after `Marshal.QueryInterface(surfacePtr, IDirect3DDxgiInterfaceAccess_GUID)`.
  Tear-off semantics apply identically.  The "luck" here is documented design,
  not actual luck.

- **Site 166 ≠ D/E/F.**  Cast is to `IDXGIResource`, not `ID3D11Device`.
  The pointer comes from `Marshal.QueryInterface(texture2DPtr, IID_IDXGIResource)`
  where `texture2DPtr` is a `ID3D11Texture2D*` from `dxgiAccess.GetInterface`.
  The texture is a distinct COM object with its own IUnknown identity (not the
  device — each texture allocation is independent).  Per COM rules QI preserves
  identity within an object, so `dxgiResPtr` shares IUnknown with `texture2DPtr`,
  which is the texture's IUnknown — distinct from the WinRT `IDirect3DSurface`
  projection's IUnknown.  `GetObjectForIUnknown(dxgiResPtr)` returns a fresh
  RCW, not the surface projection, and the `IDXGIResource` cast succeeds.

Empirical confirmation: `D3DImageBridge.GetSharedHandleFromSurface` runs on
every captured frame (essentially: 30+ times per second while blur is active).
This code has shipped through v0.1.0, v0.2.0, and v0.3.0 on the user's real
hardware GPU — three releases of constant exercise.  An `InvalidCastException`
in this path would manifest as visible blur failure within seconds of every
launch; that's never been observed.

**Conclusion: defer to v0.3.1 as a consistency improvement, not a bug fix.**

A regression-guard integration test would require setting up a live
`Win2D` `CanvasRenderTarget` *and* a `D3D9Ex` `Direct3DEx` device, then
invoking `GetSharedHandleFromSurface` and asserting "no exception."  That's
~50–100 LOC of test infrastructure for a code path that's been stable for
three releases — high ratio of test maintenance burden to bug-prevention
value.  The smoke test already exercises this path live every time the app
runs.  Tracking issue (to be filed by the user when the audit PR merges) in
v0.3.1 will do the actual conversion + add a small test alongside.

---

**Rejected agent findings (critical pushback that didn't go in the punch list).**

- "Critical: HSTRING leak on `WindowsCreateString` failure" — false alarm.
  Per MSDN the function sets `hstring` to `NULL` on failure (`hr < 0`), so
  the early-return path doesn't have anything to leak.  Original code is
  correct.
- "High: missing `SetLastError = true` on `EnumWindows` / `IsIconic` /
  `GetWindow` / `IsWindowVisible` / `MonitorFromWindow` / etc." — code-quality
  nit, no security impact.  These functions' failure modes are obvious from
  the return value (`false` / `IntPtr.Zero`); there's no diagnostic loss.
  Adding `SetLastError = true` everywhere just for cosmetic consistency would
  be churn.
- "Critical: `(IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr)`
  in `CaptureItemFactory:74`" — different risk class than `D3DImageBridge`.
  `IGraphicsCaptureItemInterop` is a documented WinRT-projected interop
  interface and the canonical pattern in Microsoft's own
  `Windows.Graphics.Capture` samples; CsWinRT explicitly handles it.  No
  collision risk.
- "Medium: `RowDelta` / `ColumnDelta` out-of-bounds read in
  `WhatsAppRegionDetector`" — agent didn't trace the call sites.
  `RowDelta` accesses pixel row `y - 1`, but `y` starts at `TitleBarIgnore`
  (a positive constant) so `y - 1 ≥ 0`.  `ColumnDelta` accesses column
  `x - 1`, but `x` starts at `EdgeIgnorePixels = 5` so `x - 1 ≥ 4`.  Bounds
  invariants hold by construction.

---

**Verified non-findings (audited and clean — documented for future reference).**

- **Network calls / telemetry / update checks**: zero.  No `HttpClient`,
  `WebClient`, `Socket`, `NamedPipe`, `gRPC`, no off-machine endpoints
  anywhere in the source tree.
- **Registry writes**: zero.  No `Microsoft.Win32.Registry` usage.
- **GPU pixel persistence**: traced the full readback path —
  `BlurPipeline.TryReadLatestFrameAsBgra` → `_regionDetector.Detect(pixels, …)`
  → only the resulting `Rect` coordinates and a couple of integer scalars
  are logged.  Pixel data lives in CPU memory only as long as a single
  `Detect()` call, then GC'd.  Never written to disk, never sent over the
  wire (which doesn't exist anyway, per above).
- **Manifest privilege model**: `app.manifest` declares
  `requestedExecutionLevel level="asInvoker" uiAccess="false"` — no
  elevation, no UIAccess.  No installer-level elevation either (installer
  not yet written; Inno installer audit deferred to the next PR).
- **Crash log content**: `App.xaml.cs.LogCrash` writes type name + message +
  stack trace per inner-exception chain, plus a timestamp.  No local
  variables, no captured frame buffers, no third-party window state.  Safe.
- **Secrets in repo / git history**: clean.  No `.env`, no `.pfx` / `.pem`,
  no PEM-shaped private keys, no JWT-shaped strings, no AWS / GCP / Azure
  keys.  Email addresses appear only in expected places (LICENSE,
  COMMERCIAL.md, README.md, git author metadata).
- **`tools/` recon outputs**: `git ls-files tools/` shows only source files
  and READMEs — no `.txt`, no `.log`.  The recon outputs containing
  third-party process / window data are correctly gitignored via
  `tools/.gitignore`.
- **SPDX headers**: confirmed present on all 66 .cs files in `src/` and on
  the 5 production csproj's `LICENSE` machine identifier.  Compliance
  surface for AGPL is in good shape.

---

**Files modified.**

- `src/Gausslite.App/Orchestration/CaptureItemFactory.cs` — [H1] log
  redaction (line 138 title→title.len), [H2] HWND re-validation block
  before `CreateForWindow`, plus an `IsWindow` P/Invoke addition.
- `src/Gausslite.Core/ScreenShare/WindowSignalScreenShareDetector.cs` —
  [H1] log redaction (line 97 title field dropped).
- `src/Gausslite.App/Diagnostics/StartupLog.cs` — [M1] 5 MB cap with
  per-write `FileInfo.Length` check + intra-process lock.
- `src/Gausslite.Core/Diagnostics/DiagLog.cs` — [M1] same 5 MB cap (mirror
  of StartupLog).
- `src/Gausslite.App/Gausslite.App.csproj`,
  `src/Gausslite.Core/Gausslite.Core.csproj`,
  `src/Gausslite.Overlay/Gausslite.Overlay.csproj`,
  `tests/Gausslite.App.Tests/Gausslite.App.Tests.csproj`,
  `tests/Gausslite.Core.Tests/Gausslite.Core.Tests.csproj` — [M2]
  `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`.
- `src/Gausslite.App/packages.lock.json`,
  `src/Gausslite.Core/packages.lock.json`,
  `src/Gausslite.Overlay/packages.lock.json`,
  `tests/Gausslite.App.Tests/packages.lock.json`,
  `tests/Gausslite.Core.Tests/packages.lock.json` — [M2] generated by
  `dotnet restore --force`.
- `.gitignore` — [L2] `*.log` line added.
- `STATE.md` — this session summary + recent decisions entries.
- `HISTORY.md` — this verbose narrative.
- `CHANGELOG.md` — `[Unreleased]` `### Security` block.

**Tests / build.** Test counts unchanged (Core 128/128, App 98/98 on x64).
Build clean (0 errors, 1 pre-existing Win2D AnyCPU warning).
`dotnet restore --locked-mode` clean.

---

### 2026-05-03 — v0.3.0 first slice: screen-share auto-detection (Zoom, Teams, browser)

**Context.** v0.2.0 has shipped (PR #34). Next milestone per PLAN.md is v0.3.0
"Knows when to blur" — auto-activate blur when the user starts screen sharing.
The full milestone bundles three layers of difficulty: (1) detect a sharing
client is running, (2) detect they're *actively* sharing, (3) detect *what*
they're sharing (which monitor / window). This session ships (1)+(2) for the
high-value apps; (3) and Discord-desktop are deferred to follow-ups.

---

**Recon-driven signatures.** Wrote two console probe utilities and walked through
real share sessions to capture ground-truth window signatures:

- `tools/ShareProbe` — enumerates every visible top-level window and dumps
  `(hwnd, pid, processName, className, title, isVisible, ownerHwnd, exStyle)` as
  TSV. User ran it before/during a real share for Zoom, Teams, Discord (desktop),
  and a Chrome-based Meet session.
- `tools/DiscordProbe` — same idea but recursively walks child windows under each
  Discord top-level window (depth-12 cap). Built after Discord's top-level recon
  showed zero share-only signal — the theory being maybe Discord's share controls
  are child windows.

The diff (during − before) gave us:
- **Zoom desktop**: a new top-level window appears during share, class
  `ZPFloatToolbarClass` and title `"Screen sharing meeting controls"`. The class
  is unique to Zoom; the title nails the specific share toolbar (vs other
  ZPFloatToolbarClass instances Zoom uses for non-share controls like
  `"Video layouts"`).
- **Microsoft Teams desktop**: a new top-level window appears during share, class
  `TeamsWebView` and title `"Sharing control bar | Microsoft Teams | Pinned window"`.
  Class is the generic Teams shell (used for the main app window too); title is
  the discriminator (anchor on `"Sharing control bar"`).
- **Browser (Chrome / Edge / Chromium)**: a new top-level window appears during
  share, class `Chrome_WidgetWin_1` and title `"meet.google.com is sharing your
  screen."` (Meet) or `"<domain> is sharing your screen."` for any browser-based
  WebRTC share. Process is `chrome` or `msedge` depending on the browser. One
  signature catches Google Meet, browser-Zoom, browser-Teams, browser-Discord,
  and anything else using `getDisplayMedia()`.
- **Discord desktop**: zero diff. Both top-level enumeration AND recursive
  child-window enumeration (depth 12) showed identical sets of windows during
  and outside an active share. Discord renders its share UI as Chromium web
  content inside the main `Chrome_WidgetWin_1` → `Chrome_RenderWidgetHostHWND`
  bridge — invisible to GDI window enumeration at any depth. Decision: defer
  Discord desktop to v0.3.1, with UIA tree-walking as the proposed approach.

---

**Architecture.** New `Gausslite.Core/ScreenShare/` module:

- `ScreenShareState` enum — `Idle` / `Active`.
- `ActiveShareEvidence` record — diagnostic payload `(AppName, ProcessName,
  WindowClass, WindowTitle, Hwnd)`.
- `IScreenShareDetector` — `event StateChanged`, `CurrentState`, `CurrentEvidence`,
  `Start()` / `Stop()`. Disposable.
- `ShareSignature` (internal) — three predicates (`ProcessNameMatches`,
  `ClassNameMatches`, `TitleMatches`) plus an `AppName` label. `Matches(WindowInfo w)`
  is `true` when all three predicates match.
- `KnownShareSignatures` (internal) — three baked-in signatures (Zoom, Teams,
  Browser) and an `All` array.
- `WindowSignalScreenShareDetector` — concrete detector. Polls via an injected
  `PollScheduler` delegate (production: `DispatcherTimer` on the WPF UI thread;
  tests: capture-and-fire-manually). On each tick, calls
  `IWin32Api.EnumerateVisibleWindows()`, matches against the signature set,
  flips `CurrentState` only on transitions, and fires `StateChanged` only on
  flips. Stop / Dispose halt the scheduler. Post-Dispose Start throws.

`IWin32Api` gains `EnumerateVisibleWindows()` returning `IReadOnlyList<WindowInfo>`
where `WindowInfo` is a new record `(Hwnd, ProcessId, ProcessName, ClassName, Title)`.
The implementation reuses the existing `EnumWindows`+`GetClassName`+
`GetWindowText`+`Process.GetProcessById` plumbing pattern from `FindWindowHandle`.

---

**TrayOrchestrator state machine.** Four new fields plus a re-entry guard:

- `_shareIsActive` — mirrors detector's last-emitted state.
- `_autoEnabledForCurrentShare` — set when the detector flipped `Idle→Active`
  AND blur was off, so we turned it on. Cleared on `Active→Idle`.
- `_userOverrodeForCurrentShare` — set when the user manually toggles blur during
  an active share. Cleared on `Active→Idle`. The override balloon fires once per
  share, on the first user disable.
- `_preShareBlurWasOn` — captured at `Idle→Active`; informs whether share-end
  should auto-restore.
- `_isAutoToggle` — re-entry guard. Set to `true` while the auto-enable / auto-restore
  paths call into `EnableBlur` / `DisableBlur`, so those public methods don't account
  the call as a user override.

**Transition logic:**
- **`Idle → Active`** (`HandleShareStarted`): snapshot `_preShareBlurWasOn`. If
  blur was off, set `_isAutoToggle`, call `EnableBlur` (suppresses the user-override
  marking), clear `_isAutoToggle`, set `_autoEnabledForCurrentShare = true`. If
  blur was already on, no auto action.
- **`Active`** + user toggles blur (any direction): the public `EnableBlur` /
  `DisableBlur` see `_shareIsActive && !_isAutoToggle` and set
  `_userOverrodeForCurrentShare = true`. The disable path additionally checks
  `_autoEnabledForCurrentShare && !_userOverrodeForCurrentShare` (state at entry —
  i.e. first user disable) and fires the once-per-share balloon. Subsequent
  toggles don't re-fire it because the override flag is now set.
- **`Active → Idle`** (`HandleShareEnded`): snapshot `wasAutoEnabled` and
  `userOverrode`, clear all three flags. If `(wasAutoEnabled && !userOverrode &&
  IsBlurEnabled)`, auto-disable using the `_isAutoToggle` guard. Otherwise leave
  blur as-is.

This semantics gives the right behavior for all six scenarios:
1. Pre-share off, share starts, no user action, share ends → blur off (restored).
2. Pre-share off, share starts, user disables, share ends → blur off (user kept).
3. Pre-share off, share starts, user disables, user re-enables, share ends →
   blur on (last user action wins; override flag stays set).
4. Pre-share on, share starts, no user action, share ends → blur on (no auto-action
   was taken, nothing to restore).
5. Pre-share on, share starts, user disables, share ends → blur off (no balloon
   because `_autoEnabledForCurrentShare` is false).
6. Two consecutive shares, override in #1 → flags reset at #1's end, balloon
   can fire fresh in #2.

The `_isAutoToggle` flag was added after a first-pass implementation produced
two test failures: my initial `EnableBlur` set the override flag whenever
`_shareIsActive` was true, which incorrectly marked the auto-enable's own
`EnableBlur` call as a user override. The re-entry guard cleanly distinguishes
auto-initiated from user-initiated toggles without splitting the public API.

---

**Tray UX.** `TrayIconHost` now wires `TaskbarIcon.LeftClickCommand` to a new
`ToggleBlurCommand : ICommand` wrapper that calls
`ITrayOrchestrator.ToggleBlur()`. Same code path as the tray menu's "Enable blur"
item and the global hotkey, so override semantics, balloon firing, and state-machine
behavior are identical regardless of which surface the user used.

The override balloon copy: title `"Blur is off for this share"`, body `"We'll turn
it back on automatically the next time you share your screen."` Fired via the
existing `ITrayNotifier` from v0.2.0 — no interface change needed.

Distinct on/off tray icon images is captured as a future cosmetic improvement
in STATE.md "Next up"; not load-bearing for v0.3.0.

---

**App composition root.** `App.xaml.cs` constructs a
`WindowSignalScreenShareDetector` after `Win32Api`, wires it into the orchestrator
via the new `SetScreenShareDetector` post-construction setter (mirrors the existing
`SetTrayNotifier` pattern), and starts it after `TrayIconHost.Initialize`. The
production poll scheduler creates a `DispatcherTimer` at `DispatcherPriority.Background`
on the UI dispatcher with the requested interval. `OnExit` disposes the detector
before the orchestrator. Detector start failures are caught and logged as warnings
— non-fatal, so a detector failure doesn't kill the app, it just falls back to
manual-only blur control.

---

**Occlusion override during active share — discovered via smoke test.** First-pass
smoke test of the Zoom share path exposed a privacy bug. The detector correctly fired
`Active` and the orchestrator correctly called auto-`EnableBlur`, but the user reported
no visible blur during the share. Log analysis showed the orchestrator's state stayed
at `Armed` (overlay parked offscreen) for the entire ~12-second share, only briefly
flipping to `Active` for ~70 ms at share-end before the auto-disable fired.

Root cause: Zoom drops many small floating overlays on top of WhatsApp during a share
— share-control toolbar (`ZPFloatToolbarClass`), video tiles (`VideoFrameWndClass`),
layout selector (`ZPFloatControlPanelMgrClass`), annotation panel
(`ZPAnnotatePanelClass`), `TransparentOverlayWindow`, and others. v0.2.0's
`WindowTracker.ComputeVisibleRegion` walks the Z-order above WhatsApp and subtracts
each window's bounds. With Zoom's overlay zoo above WhatsApp, the subtraction
fragments the visible region to zero — even though most of those overlays are small
or visually translucent and WhatsApp's pixels are clearly visible around / through
them. Result: bounds-based occlusion incorrectly reported "fully occluded", the
overlay was hidden, and WhatsApp pixels leaked unblurred into the shared stream.
Worse: this is a privacy bug, not just a UX bug — viewers of the share would see
WhatsApp content despite blur being "on".

Fix: while `_shareIsActive` is true, the orchestrator overrides occlusion logic and
treats WhatsApp as fully visible regardless of Z-order:
- `HasVisibleRegion()` returns `true` unconditionally during a share.
- `OnVisibleRegionChanged` skips the `fullyOccluded → TransitionToArmed + HideOverlay`
  branch when `_shareIsActive`.
- New `EffectiveVisibleRegion()` helper returns `[_lastKnownBounds]` during a share,
  used by `RecomputeAndApplyClip` so the clip computation sees full coverage.

Privacy-first rationale: worst case is some over-blur in regions truly covered by
Zoom's UI — but viewers of the share see Zoom's UI there anyway, so the over-blur
is invisible to them. Outside of an active share, `_shareIsActive` is false and the
v0.2.0 occlusion behavior is preserved unchanged.

Two new tests (`ShareActive_VisibleRegionDropsToZero_OverlayStaysOn`,
`NoShare_VisibleRegionDropsToZero_OverlayHides`) lock down both the override and the
preserved v0.2.0 behavior. Existing v0.2.0 occlusion-clip tests still pass —
`_shareIsActive = false` is the default, so they exercise the unchanged path.

---

**Cold-start repaint nudge — discovered via second smoke test.** Second smoke (after
the occlusion-override fix) confirmed visible blur during share for all 4 apps, but
the user reported a perceived "extra delay" on Teams: "needs an actual change on
WhatsApp's frame to actually apply the blur". The blur-intensity bug fixed in v0.2.0
had the same shape — visual update lagged until WhatsApp produced a fresh WGC frame.

Log analysis ruled out Teams-specific slowness: the placeholder→first-blurred-frame
transition timing was consistent across apps (Zoom 432 ms, Teams session 1 241 ms,
Teams session 2 184 ms, Meet 322 ms), with Teams actually being fastest. Root cause
was WGC's lazy frame delivery: WGC only emits a frame when the captured window
actually paints. If WhatsApp is idle at the moment the capture session is set up
(no animations, cursor not over it), the user sees the privacy placeholder until
WhatsApp paints on its own — typically when the cursor enters it or it receives a
new message.

Fix: `HandleShareStarted` calls `_windowTracker.RequestRepaintOfTrackedWindow()`
after auto-`EnableBlur` returns. This invalidates WhatsApp's client area
cross-process, queueing a paint that WGC can capture as soon as the session
subscribes to FrameArrived. Same mechanism as v0.2.0's snap-resize fix. The nudge
runs only on the auto-enable code path; when the share starts and blur was already
on, the existing capture session is producing frames and no nudge is needed.

Two new tests (`ShareStarts_AutoEnable_RequestsRepaintOfTrackedWindow`,
`ShareStarts_BlurAlreadyOn_DoesNotRequestRepaint`) lock down the nudge condition.

---

**Tests.** 30 new tests bringing the suite from 102 + 84 = 186 to 128 + 98 = 226:

- `WindowSignalScreenShareDetectorTests` (9): initial state is Idle with no
  evidence; `Start` is idempotent (second call doesn't double-schedule); poll
  with no matching window stays Idle and fires no event; poll with matching
  window transitions to Active and fires once; stable Active across polls fires
  no second event; Active→absent → fires Idle transition with cleared evidence;
  empty signature set always reports Idle; `Stop` disposes the scheduled ticker;
  `Dispose` after `Start` stops the ticker; `Start` after `Dispose` throws
  `ObjectDisposedException`.
- `KnownShareSignaturesTests` (16): per-app positive matches against synthetic
  windows derived from the recon TSVs, plus negative cases (Zoom non-share
  windows like `"Zoom Workplace"` / `"Zoom Meeting"`; Teams in-meeting windows;
  Browser regular-tab titles; Browser process predicate excludes Discord even
  though Discord uses the same Chromium widget class). Process-name comparisons
  are case-insensitive (verified). Sanity test: `All` contains all three
  signatures.
- `TrayOrchestratorScreenShareTests` (16): the 12 baseline + 2 occlusion-override
  + 2 cold-start-nudge tests. Driven by a `FakeScreenShareDetector` that lets each
  test fire transitions deterministically without a real polling loop; uses the
  existing inline-dispatch overload pattern.

Counts: Core 128/128, App 98/98 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU
warning. Smoke test cycle: first smoke exposed the occlusion bug (fixed); second
smoke confirmed visible blur but exposed cold-start placeholder latency (fixed via
repaint nudge); third smoke pending to verify the latency improvement.

---

**Known limitation — Discord desktop.** Documented in CHANGELOG.md, STATE.md, and
PLAN.md decisions log. Workarounds for users:
- Discord-in-browser IS detected (Chrome signature).
- Global hotkey (Ctrl+Shift+B) still works.
- Tray left-click (newly wired) still works.
- A future v0.4.0 "blur whenever any sharing app is running" opt-in setting will
  cover this case heavy-handedly for users who use Discord desktop primarily.

Path forward for v0.3.1 follow-up: write a `tools/UiaShareProbe` that walks
Discord's main window UIA tree during a share, find a stable share-only element
(likely a button named "Stop streaming" or a region named "You're sharing your
screen"), and build a `UiaScreenShareDetector` that only runs when Discord is
the active candidate (to limit CPU cost).

---

### 2026-05-03 — v0.2.0 clip composition: forced-repaint nudge + known limitation

**Context.** Smoke-test of the prior round (race-safe self-validation + delayed-retry) showed scope-aware clip mostly converges correctly on resize/maximize, but a residual "user must hover the cursor over WhatsApp" issue remained for some snap/resize scenarios. The log evidence: WhatsApp's `GetWindowRect` (used by `WindowTracker`) reported one size, but WGC kept delivering frames at a different size. Without a fresh WGC frame at the new size, the envelope check rejected every readback and detection couldn't run until the user provoked WhatsApp into emitting a paint by hovering.

---

**Fix 5 — `IWindowTracker.RequestRepaintOfTrackedWindow()`.** Added `IWin32Api.InvalidateClientArea(hwnd)` wrapping `InvalidateRect(hwnd, NULL, FALSE)` (allowed cross-process, marks the entire client area dirty). Added `IWindowTracker.RequestRepaintOfTrackedWindow()` — resolves the tracked HWND via `_profile.FindWindowHandle()` and calls `InvalidateClientArea`. `TrayOrchestrator.OnBoundsChanged` calls this at the end of every dispatch — explicitly NOT gated on `OverlaySizeChanged`, because the `OnVisibleRegionChanged → OnBoundsChanged` race (Bug 3 from the previous round) means `OverlaySizeChanged` can return false even after a real size change. The repaint usually produces a fresh WGC frame within ~50–200 ms; the existing 400 ms delayed-retry then runs detection on the freshly-captured frame.

**Tests.** 3 new tests: orchestrator calls `RequestRepaintOfTrackedWindow` on every `BoundsChanged`; tracker's `RequestRepaintOfTrackedWindow` calls `InvalidateClientArea` on the resolved HWND; tracker's no-op when no HWND can be found.

---

**Known limitation — internal-divider drags during static periods.** Smoke-test confirmed `OnBoundsChanged` paths now converge without user hover. A separate residual scenario remains: internal WhatsApp layout changes that emit no `BoundsChanged` event (e.g. dragging the chat-list/conversation divider inside WhatsApp itself, or WhatsApp finishing a content-load reflow). Log evidence: chat-list rect changed from 329 to 610 px between two cadence ticks separated by ~4 seconds; during 3 seconds of that window WGC delivered no frames at all (WhatsApp content was static). The cadence (every 30 frames) doesn't tick when no frames arrive. The user's hover provokes WhatsApp into emitting a paint, WGC starts delivering again, the next cadence tick re-detects.

**Deferred fix.** A wall-time `DispatcherTimer` (~3 s interval) calling `RequestRepaintOfTrackedWindow` while blur is active would force WGC to deliver fresh frames even when WhatsApp is static. Deferred from v0.2.0 because (a) it imposes a steady CPU/battery cost on a privacy-first background app, and (b) the limitation is narrow: requires active blur + WhatsApp foreground + no user input + internal layout change. Better suited as an opt-in v0.4.0 setting once the broader settings UI lands.

Test counts: Core 102/102, App 84/84 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

### 2026-05-03 — v0.2.0 clip composition: race-safe self-validation + delayed-retry

**Context.** Smoke-test of the previous round's stale-frame validation + continuous detection fix exposed two more failure modes the cadence-only model didn't cover. With scope=ChatList, maximizing WhatsApp produced a wrong clip; the user had to hover the cursor over WhatsApp before the clip snapped to the correct chat-list rect.

---

**Bug 3 — `OnVisibleRegionChanged` updates bounds before `OnBoundsChanged` size-change clear.** Maximize fires both events. `OnVisibleRegionChanged`'s UI handler lands first and calls `ShowOverlay` → `CacheCurrentOrDefaultBounds`, which overwrites `_lastKnownBounds` with the new size. When `OnBoundsChanged`'s UI handler finally runs, `previousBounds = _lastKnownBounds = newBounds` already; `OverlaySizeChanged` returns false; the size-change clear DOES NOT run. `RecomputeAndApplyClip` then runs with new bounds and stale cached content size, computing a wrong clip (e.g. 1920/1318 = 1.457 horizontal stretch). Confirmed in the smoke log at lines 1208 and 1218.

**Bug 4 — first post-resize WGC frame captures WhatsApp mid-responsive-layout.** Even when detection re-runs after maximize, the FIRST post-resize WGC frame captures WhatsApp during its responsive-layout transition (chat list at intermediate width). Detection on it produces transitional rects. WhatsApp content then goes static, no further frames arrive, and the cadence (every 30 frames) doesn't tick until the user nudges WhatsApp into repainting.

---

**Fix 3 — self-validating `RecomputeAndApplyClip`.** Top of the method now runs the same envelope check used by readback validation (extracted as `IsScaleRatioConsistent`: maximized ≈ 1.000 or windowed ≈ 1.012/1.009, ±0.03). When `_lastContentWidth × _lastContentHeight` produces a ratio against `_lastKnownBounds` outside the envelope, clears `_lastDetectionResult` / `_lastContentWidth/Height` and falls back to full coverage. Centralizes the staleness check so it covers any path that mutates bounds — not just `OnBoundsChanged`.

**Fix 4 — debounced delayed-retry RunDetection.** New `internal delegate IDisposable DelayedUiDispatch(TimeSpan delay, Action action)` plumbed through the orchestrator constructor, defaulting to a `DispatcherTimer` wrapper in production and to a no-op in tests (with an opt-in capturing override for the new tests). Every `OnBoundsChanged` handler ends with `ScheduleDelayedDetectionRetry`: it disposes any pending pre-existing timer (debounce — drag fires many events, only the last retry remains) and schedules a fresh `RunDetection` 400 ms later. By that time WhatsApp's responsive-layout transition has settled and the cached input frame holds the steady state. The pending timer is also disposed in `TearDownCaptureAndOverlay` so a torn-down session doesn't leak retries.

**Tests.** Three new App tests: (a) self-validating clip auto-clears when an `OnVisibleRegionChanged` race produces an inconsistent bounds/content ratio; (b) delayed retry runs `RunDetection` when its scheduled action fires; (c) rapid `BoundsChanged` events dispose the prior pending retry (debounce). All existing tests still pass.

Test counts: Core 100/100, App 81/81 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

### 2026-05-03 — v0.2.0 clip composition: stale-frame validation + continuous detection

**Context.** Three previous fix attempts on the v0.2.0-clip-composition branch did not converge the scope clip in three failure modes: left-edge resize, maximize, and internal-divider drag (chat-list/conversation divider in WhatsApp itself). An Opus recon session traced the residual symptoms to two layered bugs.

---

**Bug 1 — stale-frame readback in `RunDetection`.** Sequence: `BoundsChanged` fires on a resize. The Session B fix correctly clears `_lastDetectionResult` and `_lastContentWidth/Height`. The orchestrator then calls `RunDetection("OnBoundsChanged")` as a best-effort fast path. That call reads back whatever frame is currently cached in the blur pipeline — at this moment, the *pre-resize* frame. Detection runs on stale pixels, then writes the stale rects + stale content size into the cache. The very next `RecomputeAndApplyClip` pairs *new bounds* with *old content size*, computing a clip rect through the wrong scale ratio (e.g. 1.596 instead of 1.000 on maximize from default to full-screen).

**Bug 2 — detection gated by one-shot `_detectionDone`.** Detection only ran on the first frame of a capture session and on `BoundsChanged`. WhatsApp's internal divider can be dragged without the window resizing — no `BoundsChanged` fires, so `_detectionDone` stays at 1, no re-detection ever fires, the clip stays pinned to the old divider position forever.

---

**Fix 1 — validate readback frame dimensions before caching.** New `IsReadbackFrameConsistentWithBounds` helper in `TrayOrchestrator`. Before writing `_lastContentWidth/Height/_lastDetectionResult`, it computes the bounds-to-content scale ratio and checks it lies within ±0.03 of either the maximized envelope (≈1.000) or the windowed envelope (≈1.012 horizontal, ≈1.009 vertical — the 14×7 px DWM gap that `NormalizeWindowRect` strips on maximize). If not, the frame is from BEFORE the latest size change; detection is skipped, no cached state is written, and a diagnostic log line records bounds, frame size, and the computed scale ratios. The privacy-safe full-coverage fallback in `RecomputeAndApplyClip` holds until the next post-resize frame arrives via `OnFrameArrived`.

**Fix 2 — continuous detection.** `_detectionDone` field and all reset paths removed. `OnFrameArrived` now triggers detection when `count == 1 || count % DetectionCadenceFrames == 0` with `DetectionCadenceFrames = 30`. `_frameCount` is reset in `TearDownCaptureAndOverlay` so each new capture session's first frame triggers detection. The existing `RunDetection("OnBoundsChanged")` best-effort fast path stays — Fix 1 makes it safe to call even when the cache may hold a stale frame, and when the cache is fresh it converges sooner than waiting for the next cadence tick. Latency budget for the worst case (cadence-only convergence at 30 fps) is ~1 s.

**Diagnostic log.** Single-line `clip-compose` log added at the join point in `RecomputeAndApplyClip`, printing `_lastKnownBounds`, `_lastContentWidth × _lastContentHeight`, computed `scaleX × scaleY`, the converter's input `captureRect`, and the output `scopeRect`. Pinned out as the single point that lets smoke tests for Layouts B (left-edge resize) and C (maximize) verify the new bounds and the cached content size are paired with the correct ratio.

**Tests.** Five new App tests: stale-frame readback rejected (no detector call, no cached-state write); fresh readback accepted; maximized ratio (1.000) accepted; cadence-driven detection fires at frames 1 + 30 (verified by firing 30 frames and asserting `CallCount == 2`); internal-divider scenario (same bounds across many frames, detector returns different rects between cadence ticks, `_lastDetectionResult.ChatListRect` updates after the second cadence tick). The earlier `BoundsChanged_SizeChange_RearmsFirstFrameDetection_NextFrameDetects` test was replaced by `BoundsChanged_SizeChange_FastPath_RecoversWhenReadbackMatchesNewBounds` to reflect the cadence-only model. All existing privacy-invariant + size-change-clears-state tests still pass.

Test counts: Core 100/100, App 78/78 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

---

### 2026-05-03 — E_NOINTERFACE audit: six sites eliminated across Blur module

**Context.** A previous session fixed one `Marshal.GetObjectForIUnknown + managed-cast` site
in `Win2DBlurInterop.TryReadBgra` (the IDirect3DSurface-path bug). An integration test was
added and passed on WARP. Post-fix, production still exhibited `InvalidCastException` in
`Win2DBlurRenderTarget..ctor` at line 90 — a different site, same root cause.

---

**Bug class.**

Any WinRT object registered via `MarshalInterface<T>.FromAbi` is entered into CsWinRT's
ComWrappers global instance table keyed by IUnknown identity. `Marshal.GetObjectForIUnknown(ptr)`
for any pointer sharing that IUnknown returns the registered managed projection, not a raw BCL
RCW. Casting the managed projection to a private COM interface (not declared in Windows metadata
— `ID3D11Device`, `IDirect3DDxgiInterfaceAccess`, etc.) fails with `InvalidCastException`
because the managed projection doesn't implement that type in the .NET type system.

---

**Audit — 6 sites found.**

**Sites A/B/C** — `(IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(dxgiAccessPtr)`:

| Site | File | Method | Line |
|---|---|---|---|
| A | `Win2DBlurInterop.cs` | `GetD3D11DevicePtr` | 513 |
| B | `Win2DBlurRenderTarget.cs` | `GetD3D11DevicePtr` | 149 |
| C | `Win2DBlurInterop.cs` | `FlushD3D11Context` | 457 |

`dxgiAccessPtr` is obtained by `Marshal.QueryInterface(inspectablePtr, IID_IDirect3DDxgiInterfaceAccess)`
where `inspectablePtr` is `IWinRTObject.NativeObject.ThisPtr` of a registered `IDirect3DDevice`.

**Work by luck.** In both WARP and production, `IDirect3DDxgiInterfaceAccess` is a COM
**tear-off** on `IDirect3DDevice` — a separate COM object with a different IUnknown identity
from the WinRT device wrapper. `GetObjectForIUnknown` doesn't find the registered projection,
creates a fresh BCL RCW, and the managed cast succeeds via traditional COM QI. Would break if
Windows changes the implementation.

**Sites D/E/F** — `(ID3D11Device)Marshal.GetObjectForIUnknown(d3d11DevicePtr)`:

| Site | File | Method | Line | Status |
|---|---|---|---|---|
| D | `Win2DBlurRenderTarget.cs` | constructor | 90 | **Throwing in production** |
| E | `Win2DBlurInterop.cs` | `CreateCachedFrame` | 93 | Would throw in production |
| F | `Win2DBlurInterop.cs` | `CreateStagingTexture` | 182 | Would throw in production |

`d3d11DevicePtr` is the raw `ID3D11Device*` returned by `IDirect3DDxgiInterfaceAccess::GetInterface`.

**Production (hardware GPU):** Windows implements `IDirect3DDevice` via COM aggregation — the
WinRT wrapper's IUnknown **is** the ID3D11Device's IUnknown. `GetObjectForIUnknown` returns the
registered CsWinRT `IDirect3DDevice` projection. The managed cast to `ID3D11Device` fails
(CsWinRT projection doesn't implement this .NET interface type) → `InvalidCastException`.

**WARP (integration test):** The WARP D3D11 device and WinRT wrapper are distinct COM objects
(no aggregation, different IUnknown). `GetObjectForIUnknown` creates a new BCL RCW, the cast
succeeds via COM QI. **This is why the previous integration test missed Sites D/E/F.**

---

**Fixes.**

All 6 sites converted to raw vtable dispatch:

- Sites A/B/C: `CallGetInterface(dxgiAccessPtr, in IID_ID3D11Device, out d3d11DevicePtr)` — reads
  IDirect3DDxgiInterfaceAccess vtable slot 3 directly, bypassing the managed layer entirely.

- Sites D/E/F: `CreateTexture2DRaw(d3d11DevicePtr, ref desc, out texture)` — reads ID3D11Device
  vtable slot 5 directly.

Both dead `[ComImport]` interface definitions (`IDirect3DDxgiInterfaceAccess`, `ID3D11Device`)
removed from both files. `Win2DBlurRenderTarget` gained its own `GetInterfaceDelegate`,
`CreateTexture2DDelegate`, `CallGetInterface`, and `CreateTexture2DRaw` (mirroring the helpers
that already existed in `Win2DBlurInterop`).

Post-fix grep: zero `GetObjectForIUnknown` call sites in `src/Gausslite.Core/Blur/` (two
remaining matches are comments explaining why the pattern is absent).

---

**Vtable slot reference (both files).**

| Slot | Object | Method | Used in |
|---|---|---|---|
| 3 | `IDirect3DDxgiInterfaceAccess*` | `GetInterface` | `CallGetInterface` |
| 5 | `ID3D11Device*` | `CreateTexture2D` | `CreateTexture2DRaw` |
| 40 | `ID3D11Device*` | `GetImmediateContext` | `FlushD3D11Context`, `TryReadBgra` |
| 111 | `ID3D11DeviceContext*` | `Flush` | `FlushD3D11Context`, `TryReadBgra` |
| 47 | `ID3D11DeviceContext*` | `CopyResource` | `TryReadBgra` |
| 14 | `ID3D11DeviceContext*` | `Map` | `TryReadBgra` |
| 15 | `ID3D11DeviceContext*` | `Unmap` | `TryReadBgra` |

---

**Test fix.**

Added `AllConvertedCallSites_WhileDeviceIsAlive_DoNotThrow` to
`Win2DBlurInteropIntegrationTests`. Exercises all 6 converted paths in sequence:

1. `CreateRenderTarget` → `Win2DBlurRenderTarget` ctor → Sites B (fixed) + D (fixed)
2. `CreateCachedFrame` → Site E (fixed) + `MarshalInterface<IDirect3DSurface>.FromAbi`
3. `CreateStagingTexture` → Site F (fixed)
4. `FlushDevice` → `FlushD3D11Context` → Site C (fixed)
5. `TryReadBgra` → previously fixed site (surface path)

All run while `_d3dDevice` (created via `MarshalInterface<IDirect3DDevice>.FromAbi`) is alive.
Surface path (step 5) catches regression in WARP because IDirect3DSurface's
IDirect3DDxgiInterfaceAccess shares IUnknown with the registered surface projection.
Device-path sites D/E/F catch regression only on hardware GPU (WARP doesn't reproduce the
IUnknown identity collapse for those paths).

---

Smoke test: zero `InvalidCastException` entries, `detection-succeeded` on every trigger
(first frame + all BoundsChanged), plausible chatList/conversation rects, no exceptions
from any call site. Scope switch (ChatList → Conversation → Both) logged cleanly.

Test counts: Core 87/87, App 50/50 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning.

Commit: `fefd970` on branch `v0.2.0-detection-plumbing`.

---

### 2026-05-02 — v0.2.0 Session A: detection plumbing

**Goal.** Wire `WhatsAppRegionDetector` into the live capture path so detection runs on every
first frame and on every WhatsApp resize. No visual behavior change. Session B will consume
the results to drive scope-aware blur.

---

**GPU→CPU readback design.**

The existing `BlurPipeline` keeps a `ICachedFrame` (`Win2DCachedFrame`, a Win2D
`CanvasRenderTarget`) that holds the most recent captured frame for on-demand re-render.
Reading it back to CPU requires a D3D11 staging texture.

New types:
- `IBlurStagingTexture` (interface, `float Width/Height + Dispose`) — manages lifetime.
- `Win2DBlurStagingTexture` (internal) — wraps a raw `ID3D11Texture2D*` created with
  `D3D11_USAGE_STAGING`, `D3D11_CPU_ACCESS_READ`, zero bind flags. Owns the COM pointer
  and releases it on `Dispose`.

New `IBlurInterop` methods:
- `CreateStagingTexture(device, w, h) -> IBlurStagingTexture` — creates the staging
  texture on the app's shared D3D11 device.
- `TryReadBgra(device, cachedFrame, staging, out pixels, out w, out h, out stride) -> bool`
  — gets the immediate context (vtable slot 40), flushes (slot 111) to avoid stale
  frame data, calls `CopyResource` (slot 47) from source → staging, then `Map` (slot 14) /
  `Marshal.Copy` / `Unmap` (slot 15). Returns `false` on any HRESULT failure.

Source texture access: `Win2DCachedFrame` now stores an `IDirect3DSurface? Surface` property.
The creation path in `Win2DBlurInterop.CreateCachedFrame` was changed from
`new CanvasRenderTarget(device, w, h, 96f)` (Win2D-internal allocation, no surface handle)
to the explicit D3D11 path (same as `Win2DBlurRenderTarget`): `CreateTexture2D` with
`BIND_RENDER_TARGET | BIND_SHADER_RESOURCE` (no `MISC_SHARED` needed), QI for `IDXGISurface`,
`CreateDirect3D11SurfaceFromDXGISurface`, then `CanvasRenderTarget.CreateFromDirect3D11Surface`.
The stored `IDirect3DSurface` can be QI'd for `IDirect3DDxgiInterfaceAccess →
GetInterface(IID_ID3D11Texture2D)` to get the source pointer for `CopyResource`.

All vtable-dispatch infrastructure follows the existing `FlushD3D11Context` pattern:
`[UnmanagedFunctionPointer(StdCall)]` delegates, `Marshal.ReadIntPtr(vtable, slot * IntPtr.Size)`,
`Marshal.GetDelegateForFunctionPointer`. No new COM imports for `ID3D11DeviceContext` —
the 47-stub placeholder approach was rejected as load-bearing noise.

`BlurPipeline` lifecycle for `_stagingTexture`:
- Allocated on first `TryReadLatestFrameAsBgra` call (when `_cachedInputFrame` is non-null).
- Reused while `Width/Height` match `_cachedInputFrame`.
- Discarded (null-ed) alongside `_cachedInputFrame` on dimension change in `BlurFrame`.
- Reallocated on the next `TryReadLatestFrameAsBgra` call after a resize.
- `Dispose()`d in `BlurPipeline.Dispose()` inside `_cacheLock`.

---

**Detector triggering design.**

`TrayOrchestrator` gains `IRegionDetector _regionDetector` (8th ctor param, public ctor now
8-arg, internal test ctor now 10-arg). `App.xaml.cs` wires `new WhatsAppRegionDetector()`.

Two trigger points:

1. **First frame** (`OnFrameArrived`, background thread):
   After `BlurFrame` succeeds and `PresentFrame` is called, an Interlocked one-shot gate
   (`_detectionDone`, pattern mirrors `_noOutputLogged`) dispatches a UI-thread lambda.
   The lambda is guarded by `_setupGeneration` (captured on the background thread) so a
   `TearDownCaptureAndOverlay` that runs before the lambda executes does not write stale
   layout data into `_lastDetectionResult` for the new session.

2. **Every `BoundsChanged`** (UI-thread body of `OnBoundsChanged`):
   `RunDetection("OnBoundsChanged")` is called at the end of the dispatch lambda, after
   overlay has been moved to the new bounds. Runs unconditionally; `TryReadLatestFrameAsBgra`
   returns `false` before the first frame, producing a logged skip with no result written.

`RunDetection` (UI-thread private helper):
1. `_blurPipeline.TryReadLatestFrameAsBgra(out pixels, out w, out h, out stride)`
2. If `false`: log "detection skipped — no frame available".
3. `var result = _regionDetector.Detect(pixels, w, h, stride)`
4. `_lastDetectionResult = result`
5. Log `DetectedRailSide`, `ChatListRect`, `ConversationRect` on success; `FailureReason` on failure.

`_detectionDone` is reset to 0 in `TearDownCaptureAndOverlay` (via `Interlocked.Exchange`)
so detection re-fires on the first frame of the next session.

`_lastDetectionResult` is `RegionDetectionResult?` (null = never run). Plain assignment —
written and read on UI thread only. Exposed via `internal RegionDetectionResult? LastDetectionResult`.

---

**Tests.**

`BlurPipelineTests` (6 new):
- `BeforeAnyFrame_ReturnsFalse` — no `CreateStagingTexture` call.
- `OnFirstRead_AllocatesStagingTexture` — `CreateStagingTexture` called once.
- `SameDimensionsTwice_ReusesStagingTexture` — `CreateStagingTexture` called once total.
- `AfterDimensionChange_ReallocatesStagingTexture` — called once per dimension pair; old staging disposed.
- `Dispose_DisposeStagingTexture` — staging disposed when pipeline is.
- `DelegatesToInterop` — `TryReadBgra` called; returned pixels/width/height/stride forwarded.

`TrayOrchestratorTests` (4 new):
- `FirstFrame_RunsDetectionAndStoresResult`
- `FirstFrame_DetectionOnlyRunsOnce_SecondFrameDoesNotRerunDetect`
- `BoundsChanged_RunsDetectionAgain`
- `Detection_SkippedWhenReadbackFails_LastResultStaysNull`

`FixedResultDetector` inner class: simple `IRegionDetector` implementation that increments a
counter and returns a configurable result, avoiding NSubstitute limitations with `ReadOnlySpan<byte>`.

Test counts: Core 85/85, App 50/50 (x64 Release). Build: 0 errors, 1 pre-existing Win2D
AnyCPU warning.

---

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
