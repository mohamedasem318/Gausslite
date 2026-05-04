# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.3.5 — "Minimum settings for beta + Win10 + installer"** (branch
`v0.3.5-polish-and-ship`)

v0.1.0, v0.1.1, v0.2.0, v0.3.0, and v0.3.5-minimum-settings are shipped on `main`.
This branch adds the polish-and-ship layer: project metadata via
`Directory.Build.props`, embedded app icon, Win10 19041 support (TFM downgrade +
runtime `IsBorderRequired` guard), Inno installer, `build-release.ps1`, and
release docs. After this merges, tag `v0.3.5` and create the GitHub Release.

## Last session summary

**2026-05-04 — v0.3.5 polish-and-ship: metadata, icon, installer, smoke-tested (branch `v0.3.5-polish-and-ship`).**

Bridged the gap between v0.3.5 code being merged on `main` and v0.3.5 being installable for the public beta. Smoke-tested end-to-end on Win11 22H2; ready to tag and release.

**Project metadata.** New `Directory.Build.props` at repo root sets `Authors=Mohamed Assem`, `Company=Gausslite`, `Product=Gausslite`, `Copyright=(c) 2026 Mohamed Assem`, `Version=0.3.5`, `AssemblyVersion/FileVersion=0.3.5.0`, `Description`. Propagates to all 9 csprojs via SDK auto-generated assembly attributes. `app.manifest` `assemblyIdentity version` bumped from `1.0.0.0` to `0.3.5.0`. The compiled `.exe`'s Properties → Details dialog reads `Gausslite / Gausslite / 0.3.5.0 / (c) 2026 Mohamed Assem`.

**Embedded app icon.** `<ApplicationIcon>Assets\tray-icon.ico</ApplicationIcon>` added to `Gausslite.App.csproj`. The icon already shipped as `Content` (so the tray loaded it from disk); now also embedded in the `.exe` resource table, so Explorer / Alt-Tab / installer wizard show the Gausslite icon.

**Windows 10 support — attempted, dropped.** Lowered TFM to `19041` and tried gating `_session.IsBorderRequired = false` on `ApiInformation.IsPropertyPresent` + reflection-based setter (`typeof(GraphicsCaptureSession).GetProperty(...)`). Smoke-tested on Win11 22H2 — the reflection no-opped because `typeof(GraphicsCaptureSession)` resolves at *compile time* using the 19041 SDK projection, which doesn't have `IsBorderRequired` (added in 22621); `GetProperty()` returned null at runtime even on Win11. Result: yellow capture-indicator border was visible on Win11, regression vs the previous code. Reverted: TFM is back to `net8.0-windows10.0.22621.0` across all 5 production+test csprojs, `WinRTCaptureSession.IsBorderRequired` calls the property directly, the `ApiInformation` guard and reflection wrapper are gone. Win10 is **not supported** in v0.3.5; if a user asks, the principled fix is COM QI to `IGraphicsCaptureSession3` (the Win11 22H2 interface that exposes `put_IsBorderRequired`) — a v0.3.x follow-up worth maybe 30 minutes if there's actual demand.

**Auto-start self-heal log line.** `RegistryAutoStartManager.Enable()` now logs a `StartupLog.Info` line on successful registry write — makes the install→uninstall→install-elsewhere self-heal visible in `gausslite-startup.log`. The reconciliation logic at `App.xaml.cs:62-65` was already correct; this is diagnostics-only.

**Inno installer (`installer/Gausslite.iss`).** Per-user install at `{localappdata}\Programs\Gausslite` (no admin / UAC). Fixed `AppId={4BEC16D5-721D-4C2B-9B71-03CBF83653C6}` baked in once and never changed — future installer versions auto-upgrade old installs in place. `MinVersion=10.0.22621` matches the TFM floor. `[Registry]` removes the auto-start entry on uninstall (`HKCU\...\Run\Gausslite` with `deletevalue uninsdeletevalue dontcreatekey` — the installer never *creates* the value, only the in-app toggle does, but always cleans it up). `[UninstallDelete]` removes `{localappdata}\Gausslite\` (per-user state dir holding `settings.json`) plus explicit `gausslite-startup.log` / `gausslite-crash.log` entries inside `{app}` (runtime-created files Inno's `[Files]` cleanup doesn't track).

**`build-release.ps1`.** PowerShell 7+ pipeline: reads version from `Directory.Build.props`, runs `dotnet publish -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en` (multi-file — `PublishSingleFile=true` conflicts with `EnableCoreMrtTooling=false` which the project sets to avoid VS2022 AppxPackage tooling not in the dotnet CLI SDK), invokes ISCC.exe (auto-discovered from `HKLM:\...\Inno Setup 6_is1`), packs the publish output as `Gausslite-0.3.5-portable.zip` via `Compress-Archive`, prints SHA-256 hashes.

**Smoke-test results (Win11 22H2).** All paths green after one Inno typo fix (`filesandfolders` → `filesandordirs` in `[UninstallDelete]`):
- Install + Properties dialog metadata + embedded icon: ✓
- Tray menu structure (Enable blur, intensity, region, Auto-start, Blur on any sharing app, Exit): ✓
- Manual blur enable/disable + intensity change: ✓
- Auto-blur on Zoom share start, off at share end: ✓
- Settings persistence (intensity + region survive restart): ✓
- Auto-start with Windows toggle writes `HKCU\...\Run\Gausslite`: ✓
- Auto-start self-heal: moving the binary writes the new path on next launch; the new `RegistryAutoStartManager.Enable: wrote ...` log line appears as expected: ✓
- Uninstall cleanup: install dir, `{localappdata}\Gausslite`, registry value, Start Menu shortcut all removed (one harmless cosmetic leftover: `{localappdata}\Gausslite` was sometimes left as an empty directory if File Explorer was browsing it during uninstall).

**Defender ASR caveat (documented, not blocking).** Smoke test surfaced that Microsoft Defender's "Use advanced protection against ransomware" ASR rule (GUID `c1db55ab-c21a-4637-bb3f-a12568109d35`) silently blocks the unsigned binary because we have zero cloud reputation as a brand-new build. ASR is **off by default** on consumer Windows — only affects users with security-managed configurations. Documented in CHANGELOG known limitations with the `Add-MpPreference -AttackSurfaceReductionOnlyExclusions` workaround. Long-term fix is Microsoft's file-submission pipeline (https://www.microsoft.com/en-us/wdsi/filesubmission), which whitelists the file hash in cloud reputation — once whitelisted, ASR stops firing for all users globally.

**Final output artifacts** (post-revert build):
- `installer\Output\GaussliteSetup-0.3.5.exe` (~52 MB, Inno per-user installer)
- `installer\Output\Gausslite-0.3.5-portable.zip` (~74 MB, no-install option)

**Per-release checklist (recurring).** Every Gausslite public release should:
1. Build artifacts via `build-release.ps1`.
2. Submit *both* `GaussliteSetup-X.Y.Z.exe` and `Gausslite.App.exe` (from `publish/`) to Microsoft for analysis. They're different file hashes; both need whitelisting. Same Microsoft account each time so submissions correlate.
3. Tag and create the GitHub Release with both artifacts attached + SHA-256 hashes in the notes.
4. The "Submit other AVs (Norton, Kaspersky, etc.)" step is on-demand only — only if a beta tester reports their AV blocking us.

**Status of this session.** Build clean (0 errors, 1 expected Win2D AnyCPU warning). Tests green: Core 140/140, App 110/110 (x64). Smoke-test passed on Win11 22H2. Ready to tag `v0.3.5` and create the GitHub Release after the user resubmits the final-build hashes to Microsoft.

**Next session.** Realistic-scenario test pass for `ComputeVisibleRegion` + the orchestrator's visibility logic — table-driven tests for the cases the v0.3.5 privacy regression exposed (Spotify covers WhatsApp during share, Edge fullscreen, virtual desktop switch, Zoom transparent overlays). Do this BEFORE v0.4.0.

---

**2026-05-03 — v0.3.5 minimum settings for beta (branch `v0.3.5-minimum-settings`).**

Carved the three load-bearing items out of v0.4.0 to unblock the beta installer release. Tray-menu-only surface — no full settings window yet (that stays in v0.4.0).

**Settings persistence layer.** New `Gausslite.Core.Settings.Settings` immutable record + `ISettingsStore` interface; `JsonSettingsStore` implementation in `Gausslite.App.Persistence` writes to `%LOCALAPPDATA%\Gausslite\settings.json`. Per-user, no admin elevation, survives reinstalls. Tolerates missing/empty/corrupt/null files by returning defaults — IO never throws out of the logger boundary. Persists `Intensity`, `Scope`, `AutoStart`, `ProcessRunningHeuristicEnabled`. The first two were previously lost on every restart — free win once the layer existed. App-side namespace is `Gausslite.App.Persistence` (not `Settings`) to avoid the namespace-vs-type collision with `Gausslite.Core.Settings.Settings`.

**Auto-start with Windows toggle.** New `IAutoStartManager` + `RegistryAutoStartManager` writing
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Gausslite` = `"<full path to .exe>"`.
Per-user hive — no UAC. `IsEnabled()` checks the existing value matches the current executable
path (so a moved .exe reads as "not enabled" and the user can re-toggle to update). Reconciled at
startup against `Settings.AutoStart` so the registry never silently diverges from intent. Failures
log via `StartupLog` and return false; menu reverts the visual toggle if the registry write fails.

**"Blur on any sharing app" toggle.** Extended `WindowSignalScreenShareDetector` with a runtime-mutable trigger-process-name list (default empty). Phase 1 of `Poll()` runs the strict signature match (existing); phase 2, gated on the list being non-empty, treats any visible window owned by a process in `{Zoom, ms-teams, Discord}` as an active share. Browsers explicitly excluded — matching on `chrome`/`msedge` would mean blur all the time. Discord desktop now has a workaround for issue #38: flip this toggle and any Discord window triggers blur. New static `DefaultTriggerProcessNames` constant on the detector. 7 new tests covering: heuristic-off respects signature only; heuristic-on triggers on process match; case-insensitive process matching; signature precedence over process match; mid-session flip-off; default list excludes browsers.

**Tray menu wiring.** `TrayIconHost` constructor now takes `ISettingsStore`, `IAutoStartManager`, `WindowSignalScreenShareDetector`, and `Settings initialSettings`. Two new menu items between the existing entries and Exit: "Auto-start with Windows" and "Blur on any sharing app". Both checkable; click handlers update the underlying state, save the new `Settings` via the store. Existing intensity / scope click handlers also persist now. App.xaml.cs composition root constructs `JsonSettingsStore` first, loads `Settings`, reconciles the auto-start registry, applies persisted intensity/scope/heuristic to the orchestrator + detector before `TrayIconHost.Initialize`.

**Tests.** 7 new Core (heuristic coverage) + 7 new App (`JsonSettingsStoreTests` covering missing/empty/corrupt/null/partial JSON, round-trip, parent-dir creation). No new test for `RegistryAutoStartManager` — registry IO is delegation, smoke test covers it.

**Smoke-found bug + fix.**  Smoke test exposed a privacy regression in the v0.3.0 occlusion-override: with the heuristic toggle on (`ms-teams` running but no actual share), if WhatsApp got covered by another app or minimized, the always-on-top blur overlay stayed on screen leaking blurred WhatsApp pixels on top of the unrelated foreground app.  Root cause: the v0.3.0 override "during share, ignore fully-occluded reports" was too broad — it correctly handled the false-positive case (Zoom's many small tile overlays stacking) but also fired for legitimate full-occlusion (Edge fullscreen, virtual-desktop switch, minimize).  Plus a missing `HideOverlay` call at `OnVisibleRegionChanged` line 736 when minimized-during-share.

Fix: added `IsLikelyFullyHidden` signal to `IWindowTracker`, computed via `WindowFromPoint` sampling at 5 points (4 inset corners + center) inside WhatsApp's rect.  `WindowFromPoint` is Win32's authoritative "what window is *visually* on top here?" — it automatically skips `WS_EX_TRANSPARENT` click-through overlays.  Any sample resolving to WhatsApp's process → not fully hidden.  This correctly distinguishes Edge-fullscreen (every sample returns Edge → fully hidden, hide overlay) from Zoom-during-share (Zoom's annotation/share-host overlays are `WS_EX_TRANSPARENT` so `WindowFromPoint` looks past them and returns WhatsApp → not fully hidden, keep overlay).  Gated the share-active overlay-keep-visible override on `!IsLikelyFullyHidden`.  Also fixed the missing `HideOverlay` so minimize-during-share now hides correctly.  Bonus: `WindowTracker.Stop()` now waits for the poll task to finish before clearing observable state — closes a pre-existing test-flake race.

**Took five iterations to land.**  (1) Geometric `FullyContains` heuristic re-introduced the v0.3.0 Zoom-share bug.  (2) `WindowFromPoint` sampling fixed Edge-fullscreen but broke other cases.  (3) Z-order walk silently missed Spotify → fired `VisibleRegionChanged` on `IsLikelyFullyHidden` transitions.  (4) `EffectiveVisibleRegion` discarded partial-region data during share — clip got cleared, overlay painted on top of Spotify.  (5) Final root cause: the v0.3.0 share-active override existed because `ComputeVisibleRegion` didn't filter `WS_EX_TRANSPARENT` covering windows (the same flag the overlay itself uses to be click-through), so Zoom's transparent annotation/share-host overlays falsely subtracted WhatsApp to zero.  The orchestrator-level override was a workaround for that root cause, and every iteration tried to make the workaround smarter while it was fundamentally the wrong layer.

**Final fix — push the fix down to the source:**

- **`ComputeVisibleRegion` now skips `WS_EX_TRANSPARENT` covering windows.** Same flag the overlay itself uses to be click-through; Win32's `WindowFromPoint` already skips them; the Z-order subtraction must too.  Closes the Zoom-share root cause: Zoom's transparent annotation/share-host overlays no longer falsely subtract WhatsApp to zero, so the visible region is accurate during a real share without needing any orchestrator-level override.
- **All v0.3.0 / v0.3.5 share-active overrides removed** from the orchestrator.  `EffectiveVisibleRegion()` is now just `_windowTracker.VisibleRegion`.  `HasVisibleRegion()` is `IsLikelyFullyHidden ? false : region.Count > 0`.  `OnVisibleRegionChanged` hides whenever the region is empty OR `IsLikelyFullyHidden=true` OR the window is absent/minimized — no special "during share" branch.
- **`IsLikelyFullyHidden` (`WindowFromPoint` sampling) is kept as a defensive backup** for the rare case where the Z-order walk silently misses a covering window entirely (e.g., Chromium-compositor apps with non-standard rendering).  The primary signal is the visible region; this is the safety net.
- **`WindowTracker` still fires `VisibleRegionChanged` on `IsLikelyFullyHidden` transitions** so the safety net actually triggers even when the rect list doesn't change.

8 new tests pin the new model:
- `WindowTrackerTests`: full-cover-by-opaque → likelyFullyHidden=true; partial-cover → false; no-cover → false; transparent-fullscreen-overlay → false; **`ComputeVisibleRegion_SkipsTransparentClickThroughCoveringWindows`** (the actual mechanism that closes the v0.3.0 case at the source).
- `TrayOrchestratorScreenShareTests`: Edge-fullscreen during share, Spotify-style covered-with-fake-full-region (share + manual), Spotify-style partial occlusion during share asserts the clip is the partial region not full bounds, and `ShareActive_VisibleRegionDropsToZero_OverlayHides` (was the v0.3.0 "stays on" test, now correctly asserts hide because the override is gone).
- `TrayOrchestratorClipTests`: partial-occlusion clip assertion in manual blur.

Test counts: Core 140/140, App 110/110 (x64). Build clean (0 errors, 1 expected Win2D AnyCPU warning). `dotnet restore --locked-mode` clean.

---

**2026-05-03 — Pre-public-release security audit + privacy hardening (branch `v0.3.0-security-audit`).**

Pre-tag audit before v0.3.0 ships publicly. Three Explore agents in parallel covered native interop, privilege/data-leakage, and supply-chain/secrets/hygiene. Findings critically reviewed (several agent severities were overstated; one "phantom" finding was rejected); the real issues fixed in this PR are:

- **[H1] Privacy leakage in `gausslite-startup.log`**: `CaptureItemFactory.FindProfileWindow` was logging the full window title of up to 20 examined non-matching visible windows during capture init — browser tab titles, document names, etc. `WindowSignalScreenShareDetector.Poll` was logging the full title of the matched share-control window on every Idle→Active transition; for the Browser signature that title contains the meeting host's domain. Fixed: replaced `title={title}` with `title.len={n}` in the per-window enumeration log (process and class kept for diagnostic value); dropped the title field entirely from the share-detection transition log (AppName + WindowClass already uniquely identify which signature fired).
- **[H2] HWND validation gap before `CreateForWindow`**: `CaptureItemFactory.TryCreateForProfile` had a window between `FindProfileWindow` returning the HWND and `interop.CreateForWindow` consuming it during which the kernel could destroy the window and recycle the HWND value to another process. Capturing a recycled HWND would have meant blurring some unrelated window thinking it was WhatsApp — an anti-privacy outcome. Added `IsWindow(hwnd)` plus class-name re-check before `CreateForWindow`; either failing returns false and the next poll retries.
- **[M1] Startup log unbounded within a session**: `StartupLog` truncated on each launch but had no in-session size cap, so a tray app running for days could in principle accumulate large logs. Added a 5 MB soft cap (`FileInfo.Length` check before each append, truncate-and-reset if over). Applied to both `Gausslite.App.Diagnostics.StartupLog` and `Gausslite.Core.Diagnostics.DiagLog` since both write to the same file.
- **[M2] NuGet dependency graph not locked**: direct package references were pinned, but transitive versions could float across `dotnet restore` runs. Added `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` to the 5 production+test csprojs (tools/* skipped — diagnostic-only), regenerated `packages.lock.json` in each. `--locked-mode restore` now succeeds, so future builds are reproducible.
- **[L2] Belt-and-suspenders `*.log` ignore**: added `*.log` to root `.gitignore` so any future log file that escapes its expected location can't accidentally be committed.

**[M3] D3DImageBridge `GetObjectForIUnknown` consistency — investigated, deferred to v0.3.1.** `D3DImageBridge.GetSharedHandleFromSurface` casts to `IDirect3DDxgiInterfaceAccess` (line 149) and `IDXGIResource` (line 166) using the `Marshal.GetObjectForIUnknown` + managed-cast pattern that v0.2.0 banned in `Gausslite.Core/Blur/`. Investigation:
- Site 149 is structurally identical to v0.2.0 Sites A/B/C, which the original commit explicitly identified as safe-by-design (`IDirect3DDxgiInterfaceAccess` is a documented Microsoft tear-off — QI returns a distinct IUnknown identity that bypasses the projection table).
- Site 166 (cast to `IDXGIResource`) is **not** the same risk class as Sites D/E/F. The v0.2.0 collision was specific to `ID3D11Device`'s identity collapsing with the WinRT `IDirect3DDevice` projection (one device per process). Textures are distinct COM objects per allocation; `dxgiResPtr` carries the texture's IUnknown, distinct from the surface projection.
- Empirical: this code has shipped through v0.1.0/v0.2.0/v0.3.0 on real-hardware GPU; no `InvalidCastException` has ever surfaced in this code path.

Conclusion: the conversion would be a consistency improvement, not a bug fix. Deferred to v0.3.1 with a tracking issue (to be filed). A regression integration test would require setting up a live Win2D surface + D3D9Ex device just to assert "no exception" — high LOC for a code path that's been stable for three releases.

**Rejected agent findings (critical pushback).** Several agent flags didn't survive review:
- "Phantom" `WindowsCreateString` HSTRING leak: per MSDN the function sets `hstring=NULL` on failure, so the early-return path doesn't leak.
- Missing `SetLastError` on `EnumWindows`/`IsIconic`/etc.: code-quality nit, no security impact — these functions' failure modes are obvious from the return value.
- `(IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr)` in `CaptureItemFactory:74`: different risk class from [M3] — `IGraphicsCaptureItemInterop` is a documented WinRT-projected interop interface and the canonical Microsoft sample pattern.
- `RowDelta`/`ColumnDelta` "out-of-bounds read": call-site invariants (`y >= TitleBarIgnore > 0`, `x >= EdgeIgnorePixels = 5`) make the underflow unreachable. Not a real bug.

**Verified non-findings (documented as audited and clean).** No network calls / telemetry / update checks; no registry writes; GPU pixel data is never logged or persisted (`TryReadLatestFrameAsBgra` → `_regionDetector.Detect()` → only the `Rect` coordinates are logged); `app.manifest` is `asInvoker` / `uiAccess=false` / no elevation; crash log content is stack traces only (no local variables); no secrets or PII anywhere in the working tree or git history; `tools/*.txt` recon outputs are correctly gitignored. Inno installer integrity audit deferred to the next PR (installer doesn't exist yet).

**Tests.** No new tests added — the changes are either log-content (smoke-tested manually) or defensive Win32 plumbing in code paths without existing test infrastructure. Adding test seams just for these would be a heavier change than the fixes themselves. Test counts unchanged: Core 128/128, App 98/98 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning. Locked-mode restore clean.

---

**2026-05-03 — v0.3.0 first slice: screen-share auto-detection for Zoom, Teams, and browser-based shares (branch v0.3.0-screen-share-detection).**

Built the v0.3.0 "knows when to blur" first slice. Polls every second for known share-control windows; auto-enables blur on share start, restores prior state on share end. Manual overrides during a share stick for that share and trigger a one-time friendly tray balloon. Tray icon left-click now toggles blur.

**Recon-driven signatures.** New `tools/ShareProbe` (top-level enumeration) and `tools/DiscordProbe` (recursive child enumeration) console utilities. User ran each before/during a real share for Zoom, Teams, Discord, and a Chrome-based Meet session. Real signatures captured:
- **Zoom desktop**: window class `ZPFloatToolbarClass` + title containing "Screen sharing meeting controls". Class is unique to Zoom; title pattern discriminates the share toolbar from other Zoom float toolbars.
- **Microsoft Teams desktop**: window class `TeamsWebView` + title containing "Sharing control bar". The class is the generic Teams shell; title is the discriminator.
- **Browser (Chrome / Edge / any Chromium browser)**: window class `Chrome_WidgetWin_1` + title containing "is sharing your screen". This is the browser's generic WebRTC `getDisplayMedia` notification — one signature catches Google Meet, browser Zoom, browser Teams, browser Discord, and anything else using the same API.

**Architecture.** New `Gausslite.Core/ScreenShare/` module: `IScreenShareDetector` polls visible top-level windows (extended `IWin32Api.EnumerateVisibleWindows()`) and matches against `KnownShareSignatures.All`. Emits `StateChanged` only on Idle↔Active transitions, never on stable polls. Production scheduler is a `DispatcherTimer` on the WPF UI thread; tests inject a manual scheduler that captures the tick action.

**State machine in `TrayOrchestrator`.** Four new fields (`_shareIsActive`, `_autoEnabledForCurrentShare`, `_userOverrodeForCurrentShare`, `_preShareBlurWasOn`) plus an `_isAutoToggle` re-entry guard so auto-initiated EnableBlur/DisableBlur calls don't get accounted as user overrides. On share start: snapshot pre-share state; if blur is off, auto-enable. On share end: restore pre-share state only if we auto-enabled AND the user didn't take manual control during the share. ANY user toggle during a share (enable or disable) sets the override flag, so the user's last manifest action is what persists at share-end. The override balloon fires once per share, only on the first user disable, never on auto-path disables.

**Tray UX.** `TrayIconHost` wires `TaskbarIcon.LeftClickCommand` to the orchestrator's `ToggleBlur` — same code path as the menu and hotkey, so override / state-machine semantics are identical across all three entry points.

**Discord desktop is a known limitation.** Both ShareProbe and DiscordProbe recon (top-level + recursive child window enumeration to depth 12) showed zero diff between sharing and not-sharing — Discord renders its share UI as Chromium web content invisible to GDI window enumeration. UIA tree-walking would work but adds steady polling CPU and complexity. Deferred to v0.3.1; in the meantime, Discord-in-browser IS auto-detected (Chrome signature), and Discord-desktop users can use the global hotkey, the new tray left-click, or the planned v0.4.0 "blur whenever any sharing app is running" opt-in.

**Occlusion-override-during-share fix (post-first-smoke).** First smoke test exposed a privacy bug: Zoom drops many small floating overlays on top of WhatsApp during a share. v0.2.0's bounds-based occlusion logic walks the Z-order and subtracts each above-Z window's bounds, so during a Zoom share `WindowTracker.VisibleRegion` reports 0 rects (fully occluded) even when the user can clearly see WhatsApp around / through those overlays. Result: blur was correctly auto-enabled but the overlay was hidden by occlusion logic, leaving WhatsApp pixels leaking unblurred into the shared stream. Fix: `_shareIsActive` overrides the occlusion path — `HasVisibleRegion` returns true unconditionally during share, `OnVisibleRegionChanged` ignores the fully-occluded report, and a new `EffectiveVisibleRegion()` helper returns the full window bounds for `RecomputeAndApplyClip`. Two new App tests (`ShareActive_VisibleRegionDropsToZero_OverlayStaysOn`, `NoShare_VisibleRegionDropsToZero_OverlayHides`) lock down both the override and the preserved v0.2.0 behavior outside of share. Privacy-first: worst case is over-blur in regions truly covered by Zoom's UI, but viewers see Zoom's UI there anyway.

**Cold-start repaint nudge (post-second-smoke).** Second smoke test confirmed visible blur during share for all 4 apps. User reported a perceived "extra delay" on Teams ("needs an actual change on WhatsApp's frame to actually apply blur"). Log analysis showed the placeholder→first-blurred-frame transition is consistent across all apps (~200–450 ms), with Teams actually being the fastest (~241 ms). Root cause is WGC's lazy frame delivery: the capture session only emits a frame when the captured window paints, so during the cold-start window (0–500 ms) the user sees the privacy placeholder until WhatsApp happens to paint on its own. Same mechanism as v0.2.0's snap-resize hover requirement. Fix: `HandleShareStarted` now calls `_windowTracker.RequestRepaintOfTrackedWindow()` after auto-`EnableBlur` returns — invalidates WhatsApp's client area so a fresh paint is queued, and WGC has a frame ready to deliver as soon as the capture session is set up. Two new App tests (`ShareStarts_AutoEnable_RequestsRepaintOfTrackedWindow`, `ShareStarts_BlurAlreadyOn_DoesNotRequestRepaint`) verify the nudge fires only on the auto-enable code path, not when blur was already on.

**Tests.** 30 new tests (was Core 102 + App 84 = 186; now Core 128 + App 98 = 226). New: `WindowSignalScreenShareDetectorTests` (9), `KnownShareSignaturesTests` (16), `TrayOrchestratorScreenShareTests` (16 — including occlusion-override and repaint-nudge tests added after first/second smokes). All existing tests still pass.

Test counts: Core 128/128, App 98/98 (x64). Build: 0 errors, 1 pre-existing Win2D AnyCPU warning. Second smoke passed end-to-end for Zoom/Teams/Meet (auto-blur visible) and Discord (correctly NOT auto-detected per documented limitation).

---

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

**v0.3.5 minimum-settings-for-beta PR is built and pending smoke test** (branch
`v0.3.5-minimum-settings`). Once smoke-tested, ship as PR, then proceed with the Inno
installer + tag v0.3.5 + GitHub Release work for low-key beta distribution.

- **v0.3.x follow-ups (post-merge):**
  - **Convert `D3DImageBridge` `GetObjectForIUnknown` sites to vtable dispatch**
    ([issue #43](https://github.com/mohamedasem318/Gausslite/issues/43)) —
    consistency with the v0.2.0 vtable-only rule in `Gausslite.Core/Blur/`.
    Audit verified currently safe by COM design; this is a code-shape
    improvement only.
  - **Discord desktop detection** via UIA tree-walking on Discord's main window
    ([issue #38](https://github.com/mohamedasem318/Gausslite/issues/38)). Need a
    new `tools/UiaShareProbe` recon round to pin down a stable share-only element name
    (e.g. "Stop streaming" button or "You're sharing" region). UIA polling carries CPU
    cost — only run while Discord is the active candidate, not always.
  - **Share-target detection**: determine which monitor or window the sharing client is
    capturing. If WhatsApp is not on the shared monitor or not the shared window, blur
    isn't needed. PLAN.md v0.3.0 milestone covers this; deferring to a follow-up keeps
    this slice small.
  - Slack huddle support if user demand materializes (recon-able in ~10 min).
- **v0.4.0 — "Polish".** Settings window with persistence; continuous blur-radius slider;
  auto-start with Windows; opt-in armed-state notification toggle; opt-in wall-time
  forced-repaint timer (closes issue #35); **opt-in "blur whenever any sharing app is
  running" toggle** — the heavy-handed alternative to v0.3.0's positive-evidence default,
  especially relevant for Discord-desktop users since v0.3.0 can't auto-detect that case.
- **v0.5.0 — "Notifications too".** Toast-notification blur during screen sharing.
- **v1.0.0 — Indirect Display Driver.** The big architectural shift: blur appears only
  in the shared stream, real monitor stays untouched.
- `RegionDump` annotation fix (separate Codex task, still pending; orthogonal to the
  milestones above).
- **Future tray UX**: distinct on/off tray icon images so the user can see at a glance
  whether blur is active. Captured in v0.3.0 planning; deferred (cosmetic, not load-bearing).

All pre-existing v0.2.0 work (occlusion clipping, intensity presets, edge-fade fix, region scope
submenu, scope-aware clip composition) remains working and unchanged.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- **2026-05-03 — Carved v0.3.5 "minimum settings for beta" out of v0.4.0.**
  Three load-bearing items only — settings persistence, "blur on any sharing
  app" toggle, auto-start with Windows. Tray-menu-only surface; no full
  settings window. Rationale: shipping the installer immediately after v0.3.0
  leaves three high-pain gaps for beta testers (no persistence, no Discord
  desktop workaround, no auto-start), and shipping all of v0.4.0 first would
  push the installer out 4-6 sessions while designing settings UX before any
  real user feedback exists.

- **2026-05-03 — Settings storage = `%LOCALAPPDATA%\Gausslite\settings.json`.**
  Per-user, no admin elevation, survives reinstalls. JSON with camelCase
  property names + string-encoded enums (so users sending the file with bug
  reports can read it). Missing/corrupt/empty/null files all degrade to
  defaults — IO failures never throw past `JsonSettingsStore`. App-side
  namespace is `Gausslite.App.Persistence` (not `Settings`) to avoid a C#
  namespace-vs-type collision when `using Gausslite.Core.Settings;` brings
  the `Settings` type into scope.

- **2026-05-04 — `ComputeVisibleRegion` skips `WS_EX_TRANSPARENT` covering windows.**
  Same flag the overlay itself uses for click-through; same flag Win32's
  `WindowFromPoint` already skips.  Adding it to the Z-order subtraction's filter
  closes the v0.3.0 Zoom-share root cause (transparent annotation/share-host
  overlays were geometrically covering WhatsApp but visually invisible) at the
  source.  The visible region is now the authoritative "what part of WhatsApp is
  visible to the user" signal in both the Zoom case (transparent skipped → region
  full → blur full coverage) and the Spotify case (opaque → region shrinks/empty
  → overlay clips to visible part or hides).

- **2026-05-04 — All v0.3.0 / v0.3.5 share-active overlay overrides removed.**
  `EffectiveVisibleRegion()` is just `_windowTracker.VisibleRegion`.  No more
  "during share, pretend the visible region is full bounds" workaround — that
  workaround existed because of the missing transparent-window filter (above),
  and once the filter is in place the workaround was actively harmful (it
  caused the overlay to paint blur on top of opaque covers like Spotify).
  `IsLikelyFullyHidden` (via `WindowFromPoint`) is kept as a defensive backup
  for the rare case the Z-order walk silently misses a covering window.

- **2026-05-03 — `WindowTracker.Stop()` waits for poll task to drain.**
  Previously `Stop()` cancelled the CTS but returned immediately, allowing the
  poll thread to fire one more event after callers had already started reading
  event-list state.  Tests sometimes hit `Collection was modified during
  enumeration`; the v0.3.5 minor timing shift made it reproducible.  `Stop()`
  now `Wait()`s on the poll task (1-second timeout) before clearing observable
  state.

- **2026-05-03 — "Blur on any sharing app" trigger list = Zoom, ms-teams, Discord.**
  Desktop clients only. Browsers (`chrome`, `msedge`) deliberately excluded
  — matching on them would mean blur is on whenever any browser is open,
  which is most of the day for most users. Browser-based shares remain
  covered by the existing `Chrome_WidgetWin_1` window-signature path.

- **2026-05-03 — Diagnostic logs redact third-party window titles by default.**
  `gausslite-startup.log` is privacy-sensitive — it's written next to the .exe and
  travels with the user.  The per-window enumeration log in `CaptureItemFactory`
  and the share-transition log in `WindowSignalScreenShareDetector` no longer
  record window titles of windows we don't own.  Process names and class names
  stay (both are needed to debug "why didn't WhatsApp match" / "which signature
  fired"), as does the title length (so "did the window have a title?" is still
  diagnosable).  Titles of the matched WhatsApp window itself are non-sensitive
  and remain.

- **2026-05-03 — Both startup-log writers enforce a 5 MB cap.**
  `Gausslite.App.Diagnostics.StartupLog` and `Gausslite.Core.Diagnostics.DiagLog`
  write to the same `gausslite-startup.log` file.  Each runs a `FileInfo.Length`
  check under its own lock before each append; when the file is over 5 MB it's
  truncated to a single header line and the next append starts fresh.  The
  cross-class race window is harmless (worst case is two truncate-headers in
  rapid succession), so no cross-assembly synchronisation is needed.

- **2026-05-03 — NuGet transitive graph is locked via `packages.lock.json`.**
  `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` is set in the
  5 production + test csprojs.  `tools/*` projects are intentionally not locked
  (diagnostic-only; never shipped).  Future builds use `dotnet restore --locked-mode`
  in CI when CI lands.

- **2026-05-03 — `CaptureItemFactory` re-validates the HWND before `CreateForWindow`.**
  `IsWindow(hwnd)` plus a class-name re-check runs immediately before
  `interop.CreateForWindow`.  Closes a small race window where the window could
  be destroyed and the HWND recycled by the kernel for another process between
  enumeration and capture-item creation — capturing a recycled HWND would mean
  blurring an unrelated window, which is the exact opposite of what a privacy
  app should do.

- **2026-05-03 — `D3DImageBridge` `GetObjectForIUnknown` sites are safe-by-design; conversion deferred.**
  Audit verified that the two `Marshal.GetObjectForIUnknown` + managed-cast sites in
  `D3DImageBridge.GetSharedHandleFromSurface` are not subject to the v0.2.0 CsWinRT
  projection collision: site 149 (cast to `IDirect3DDxgiInterfaceAccess`) goes
  through Microsoft's documented tear-off that has a distinct IUnknown identity;
  site 166 (cast to `IDXGIResource`) operates on a texture pointer whose IUnknown
  is distinct from the surface projection (unlike the device-identity collapse
  that bit Sites D/E/F in v0.2.0).  Vtable-dispatch conversion is tracked for
  v0.3.1 as a consistency improvement, not a bug fix.

- **2026-05-03 — Screen-share detection uses positive window evidence, not process running.**
  Detector matches against `KnownShareSignatures` — well-known share-control window
  signatures captured via real recon. Process-running alone is NOT a signal, because
  Zoom/Teams/Discord users keep these apps open in the system tray all day. The
  alternative ("blur whenever any sharing app is running") is deferred to v0.4.0 as
  an opt-in setting; v0.3.0's default is precise.

- **2026-05-03 — One generic Chrome signature catches all browser-based shares.**
  Chrome and Edge spawn a `Chrome_WidgetWin_1` floating window with title
  `"<domain> is sharing your screen."` for any tab using `getDisplayMedia()`. One
  signature covers Google Meet, browser-Zoom, browser-Teams, browser-Discord, and any
  future WebRTC-based screen-share site. Process predicate matches both `chrome` and
  `msedge`.

- **2026-05-03 — Discord desktop sharing not auto-detected; deferred.**
  Both ShareProbe (top-level enumeration) and DiscordProbe (recursive child enumeration
  to depth 12) showed zero diff between Discord-not-sharing and Discord-sharing.
  Discord renders share controls as Chromium web content, invisible to GDI window
  enumeration at any depth. UIA tree-walking can see web content but adds steady
  polling CPU cost. Path forward documented for v0.3.1; in the meantime, Discord-in-browser
  is auto-detected (Chrome signature), and Discord-desktop users have the global hotkey,
  the new tray left-click toggle, and the planned v0.4.0 "blur whenever any sharing
  app is running" opt-in as workarounds.

- **2026-05-03 — Manual override during a share = "user took control" semantics.**
  ANY user toggle (enable or disable) during an active share sets
  `_userOverrodeForCurrentShare = true`. The flag is NOT cleared by subsequent toggles
  — once set, it stays true for the rest of that share. At share-end, auto-restore
  fires only if `(autoEnabledForCurrentShare && !userOverrodeForCurrentShare)`. The
  override balloon fires once per share, on the first user disable. The auto-path
  uses an `_isAutoToggle` re-entry guard so it doesn't accidentally mark its own
  EnableBlur/DisableBlur calls as user overrides.

- **2026-05-03 — Cold-start placeholder color softened to light gray.**
  `OverlayWindow._placeholder` background changed from `RGB(32, 44, 51)` (dark slate)
  to `RGB(220, 222, 220)` (light neutral gray).  The placeholder is still fully
  opaque — privacy contract unchanged — but the new color approximates the average
  tone of heavily-blurred bright UI, so the cold-start ShowPlaceholder→first-frame
  transition feels like "blur fading in" rather than a jarring dark rectangle
  flashing before blur.  Cosmetic-only change; no logic modified.

- **2026-05-03 — Auto-enable path nudges tracked window to repaint immediately.**
  `HandleShareStarted`'s auto-enable path now calls
  `_windowTracker.RequestRepaintOfTrackedWindow()` after `EnableBlur` returns.
  Without this nudge, WGC only delivers a frame when WhatsApp paints on its own —
  if WhatsApp is idle (no animations, cursor not over it) at the moment the share
  starts, the user sees the privacy placeholder until they happen to nudge WhatsApp
  into repainting (similar to v0.2.0's snap-resize / internal-divider issues).
  Reuses the v0.2.0 `IWindowTracker.RequestRepaintOfTrackedWindow()` mechanism.
  Skipped when blur was already on (existing capture session is already producing
  frames; no nudge needed).

- **2026-05-03 — Occlusion clipping is overridden during an active share.**
  Smoke test of the auto-blur path exposed a privacy bug: Zoom drops many small
  floating overlays (share-control toolbar, video tiles, layout selector, annotation
  panel) on top of WhatsApp during a share.  The v0.2.0 occlusion-clipping logic
  walks the Z-order and subtracts each above-Z window's bounds, which during a share
  reduces WhatsApp's visible region to zero rects — even when the user can clearly
  see WhatsApp through / around those overlays.  The orchestrator now overrides this:
  when `_shareIsActive`, `HasVisibleRegion` returns true, `OnVisibleRegionChanged`
  ignores the fully-occluded report, and `EffectiveVisibleRegion()` returns the full
  window bounds.  Worst-case effect is some over-blur in regions truly covered by
  Zoom's UI — but viewers of the share see Zoom's UI there anyway, so the over-blur
  is invisible to them.  Privacy-first: blurring more is safer than blurring less.

- **2026-05-03 — Tray left-click toggles blur via the same code path as menu and hotkey.**
  `TaskbarIcon.LeftClickCommand` binds to a tiny `ToggleBlurCommand : ICommand` wrapper
  that calls `ITrayOrchestrator.ToggleBlur()`. Same entry point as the tray menu's
  "Enable blur" item and the global hotkey, so override semantics, balloon firing, and
  state-machine behavior are identical regardless of which surface the user clicks.

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