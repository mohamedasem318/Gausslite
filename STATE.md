# Gausslite — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.3.5 — Shipped 2026-05-04.** Tag pushed to `main`, GitHub Release live at
https://github.com/mohamedasem318/Gausslite/releases/tag/v0.3.5 with both
`GaussliteSetup-0.3.5.exe` (~52 MB, SHA-256 `5E8745…`) and
`Gausslite-0.3.5-portable.zip` (~74 MB, SHA-256 `DF1D27…`) attached.

**Repo visibility**: still **private** at session-end. User intends to flip
to public alongside / right after the SignPath.io OSS application is
submitted. The README "Latest release" badge and the GIF in the GitHub
Release notes both render via raw GitHub URLs that 404 for private repos
— they activate automatically once the repo is public.

**Code signing**: SignPath.io OSS application submitted 2026-05-04 at
https://signpath.org/apply. Vetting takes 1-2 weeks. v0.3.6 = signing
integration when the approval email lands.

## Last session summary

**2026-05-04 — v0.3.5 polish-and-ship: metadata, icon, installer, tag + Release (branch `v0.3.5-polish-and-ship`).**

Bridged the gap between v0.3.5-minimum-settings code merged on `main` and v0.3.5 being a tagged installable public-beta release. Smoke-tested end-to-end on Win11 22H2 across multiple iterations (every iteration found a new edge case the previous one missed); all final paths green. Tagged `v0.3.5` and published the GitHub Release with both artifacts.

**Project metadata + icon.** New `Directory.Build.props` at repo root sets `Authors=Mohamed Assem`, `Company/Product=Gausslite`, `Copyright=(c) 2026 Mohamed Assem`, `Version=0.3.5`, `AssemblyVersion/FileVersion=0.3.5.0`, `Description`. Propagates to all 9 csprojs via SDK auto-attrs. `app.manifest` `assemblyIdentity version` bumped from `1.0.0.0` to `0.3.5.0`. `<ApplicationIcon>Assets\tray-icon.ico</ApplicationIcon>` embeds the icon in `Gausslite.App.exe`.

**Inno installer + release pipeline.** `installer/Gausslite.iss` is a per-user install at `{localappdata}\Programs\Gausslite` (no admin / UAC), fixed AppId GUID `4BEC16D5-721D-4C2B-9B71-03CBF83653C6` baked once for upgrade-in-place behavior, `[UninstallDelete]` for `{localappdata}\Gausslite\` + runtime logs, `[Registry]` for HKCU\...\Run\Gausslite cleanup, `WizardSmallImageFile=wizard-small.bmp` for the wizard top-right. `build-release.ps1` is a PowerShell pipeline: `dotnet publish` self-contained + `SatelliteResourceLanguages=en` → ISCC.exe → `Compress-Archive` portable zip → SHA-256 reporting.

**Win10 support: attempted, dropped.** Lowered TFM to 19041 with `ApiInformation.IsPropertyPresent` + reflection-based `IsBorderRequired` setter. Reflection no-opped because `typeof(GraphicsCaptureSession)` resolves at compile time using the lowered SDK projection (which doesn't have the property), so the yellow capture-indicator border was visible on Win11 too — regression vs the previous behavior. Reverted: TFM is back to `net8.0-windows10.0.22621.0` across all 5 production+test csprojs, direct property access restored. If a Win10 user asks, the principled fix is COM QI to `IGraphicsCaptureSession3` — a v0.3.x follow-up worth ~30 minutes if real demand materializes.

**Three bug fixes layered on top of the metadata work** (see Recent decisions for the design notes):
1. **Sticky rail-side lock** in `TrayOrchestrator` — closes the Shift+Alt-flips-the-blur bug.
2. **`IBlurPipeline.ClearCachedFrame()` on capture-session teardown** — prevents the next session's region detection from running on the previous session's stale frame, fixes mislabeled chat-list/conversation when WhatsApp restarts in a different UI direction (LTR ↔ RTL).
3. **Dispose all three of `_renderTarget` / `_cachedInputFrame` / `_stagingTexture` together in `ClearCachedFrame`** — fixes NRE on every frame after `DisableBlur` → `EnableBlur` cycle. Initial cut kept `_renderTarget` for "hot-path optimization" which broke `BlurFrame`'s allocation invariant.

**Defender ASR finding (documented, not blocking the v0.3.5 ship).** Microsoft Defender's *"Use advanced protection against ransomware"* ASR rule silently blocks the unsigned binary on machines with the rule enabled (off by default; only on in enterprise-managed setups). Independent of Microsoft's malware engine — file submission cleared as "no malware detected" but ASR is a separate proactive gate. README + CHANGELOG document the `Add-MpPreference -AttackSurfaceReductionOnlyExclusions` workaround. Real fix: code signing in v0.3.6 via SignPath.io.

**Recording-during-share limitation discovered.** Recording tools like ScreenToGif put a region-selection window on top of WhatsApp in Z-order; our `ComputeVisibleRegion` correctly subtracts that → visible region 0 → overlay hidden → recording captures unblurred content. Decided NOT to fix in v0.3.5 — any "force overlay visible" mode re-introduces the privacy regression. Workaround: use OBS / Xbox Game Bar / Win11 Snipping Tool (capture without an on-top overlay) for demo recording. Logged as a v0.4.0 candidate ("Demo recording mode" toggle, off by default).

**Tests.** +5 `TrayOrchestratorRailSideLockTests`, +5 `BlurPipeline.ClearCachedFrame` tests. Core 145/145, App 116/116 (x64). Build clean (0 errors, 1 expected Win2D AnyCPU warning).

**Full narrative archived in HISTORY.md.**

## Next up

**Immediate (waiting on external):**
- **SignPath.io OSS approval email.** Application submitted 2026-05-04 at
  https://signpath.org/apply (not the outdated `about.signpath.io/product/open-source`
  URL — actual form is HubSpot-embedded at signpath.org/apply). Vetting
  takes 1-2 weeks. When the approval email arrives, that triggers v0.3.6.

**v0.3.6 — Code signing integration (when SignPath approves).**
- Add SignPath GitHub Actions workflow per their onboarding doc.
- Modify `build-release.ps1` to sign `Gausslite.App.exe` (inside `publish/`)
  *before* Inno wraps it, then sign `GaussliteSetup-X.Y.Z.exe` after Inno
  produces it. Two artifacts to sign per release.
- Verify with `signtool verify /pa /v ...` (signtool ships in the Windows
  SDK at `Windows Kits\10\bin\<sdk-ver>\x64\`).
- Tag v0.3.6, re-publish GitHub Release. Defender ASR stops blocking
  globally; SmartScreen passes silently.

**v0.3.x follow-ups (post-signing, in priority order):**
- **Realistic-scenario test pass for `ComputeVisibleRegion` + orchestrator
  visibility logic** — table-driven tests for Spotify-covers-WhatsApp, Edge
  fullscreen, virtual-desktop switch, Zoom transparent overlays, recording-tool
  overlay. The unit tests we have today passed during the v0.3.5 minimum-settings
  privacy regression while actual visible behavior was broken; this round of
  tests is the safety net. **Do BEFORE v0.4.0.**
- **Discord desktop detection** ([#38](https://github.com/mohamedasem318/Gausslite/issues/38))
  via UIA tree-walking on Discord's main window. Need a `tools/UiaShareProbe`
  recon round to find a stable share-only element name (e.g. "Stop streaming"
  button). UIA polling carries CPU cost — only run while Discord is the
  active candidate, not always.
- **Share-target detection** ([#40](https://github.com/mohamedasem318/Gausslite/issues/40)):
  only blur when WhatsApp is on the monitor / window the sharing client is
  capturing. v0.3.0 milestone covers this; deferred to a v0.3.x follow-up.
- **D3DImageBridge `GetObjectForIUnknown` → vtable dispatch**
  ([#43](https://github.com/mohamedasem318/Gausslite/issues/43)). Audit
  verified currently safe by COM design; consistency improvement only.
- **Win10 support via COM QI to `IGraphicsCaptureSession3`** if any real Win10
  user asks. Out of scope until then.
- Slack huddle support if user demand materializes.

**v0.4.0 — "Polish."** Settings window with persistence; continuous
blur-radius slider; share-client checklist; opt-in armed-state notification
toggle; opt-in wall-time forced-repaint timer ([#35](https://github.com/mohamedasem318/Gausslite/issues/35));
**opt-in "Demo recording mode" toggle** (disables Z-order occlusion gates
so users can capture demo gifs without their recording tool's selection
rectangle hiding the overlay; documented foot-gun, comes with a tray
balloon explaining it disables privacy protections); manual updater check.

**v0.5.0 — "Notifications too."** Toast-notification blur during share.

**v1.0.0 — Composite-window mode** ("Walmart IDD"). Shareable Gausslite
window that mirrors the desktop with selective blur baked in. No driver,
no kernel code, no signing required.

**v2.0.0 — Real IDD driver.** Phantom monitor via WDDM driver. Premium
experience. Gated on EV cert investment.

**Future tray UX**: distinct on/off tray icon images so the user can see
at a glance whether blur is active. Cosmetic, not load-bearing.

## Per-release checklist

Every Gausslite public release should:
1. Build artifacts via `build-release.ps1`.
2. Submit *both* `GaussliteSetup-X.Y.Z.exe` AND `Gausslite.App.exe` (from
   `publish/`) to Microsoft for analysis at
   https://www.microsoft.com/en-us/wdsi/filesubmission. Different file
   hashes; both need clearing. Same Microsoft account each time so
   submissions correlate.
3. Tag and create the GitHub Release with both artifacts attached + SHA-256
   hashes in the notes body.
4. Once SignPath signing lands (v0.3.6+), step 2 also applies to other AV
   vendors only on-demand if a beta tester reports their AV blocking.

## Blockers

None internal. Waiting on SignPath.io vetting (external; 1-2 weeks).

## Recent decisions

(See [PLAN.md](PLAN.md) for the canonical decisions log. Recent v0.3.5-era
decisions only here.)

- **2026-05-04 — Win10 support dropped; TFM stays at 22621.** Lowered TFM
  to 19041 + `ApiInformation` guard + reflection-based `IsBorderRequired`
  setter on `WinRTCaptureSession`. Reflection no-opped because
  `typeof(GraphicsCaptureSession)` resolves at compile time using the
  lowered SDK projection that doesn't include the property — `GetProperty`
  returned null even on Win11 where the runtime API exists. Reverted to
  the direct `_session.IsBorderRequired = false` call at TFM 22621. Win10
  can be added back via COM QI to `IGraphicsCaptureSession3` if real demand
  surfaces.

- **2026-05-04 — Sticky rail-side lock in `TrayOrchestrator`.** Rail side
  latches on the first successful detection per capture session in a new
  `_lockedRailSide` field; subsequent detections that disagree have their
  `chatListRect`/`conversationRect` swapped back into the locked
  orientation before being stored. Closes the Shift+Alt-flips-the-blur
  bug. Lock resets only on capture-session teardown or window-bounds size
  change — both legitimate "layout might really have changed" signals.

- **2026-05-04 — `IBlurPipeline.ClearCachedFrame()` on capture-session teardown.**
  Capture-session teardown explicitly clears the BlurPipeline frame cache
  so the next session's region detection only ever runs on frames from
  the current session. Without this, a stale frame from the previous
  session triggered the rail-side lock with the wrong answer when WhatsApp
  restarted in a different UI direction (LTR ↔ RTL).

- **2026-05-04 — `ClearCachedFrame` disposes all three cache fields together.**
  First implementation kept `_renderTarget` while clearing `_cachedInputFrame`
  + `_stagingTexture` (framed as a hot-path optimization to avoid
  reallocation when blur re-enables on the same window). This broke
  `BlurFrame`'s allocation guard (`if _renderTarget is null || dims-mismatch`)
  — sees the kept render target with matching dims, decides nothing needs
  to be allocated, then calls `UpdateCachedFrame(_, null, _)` and throws
  NRE on every frame. Final implementation disposes all three together;
  `BlurFrame` then correctly reallocates everything on the next frame of
  the new session. Regression test
  (`ClearCachedFrame_DisposesRenderTargetToo_AndNextBlurFrameReallocatesCleanly`)
  exercises clear-then-blur and asserts no exception.

- **2026-05-04 — Inno `[UninstallDelete]` keyword: `filesandordirs` (not `filesandfolders`).**
  Inno's actual keyword is `filesandordirs`. The first build failed with
  *"Parameter 'Type' is not a valid value"* because of the typo. Also
  added explicit `Type: files; Name: "{app}\gausslite-startup.log"` and
  `gausslite-crash.log` entries since runtime-created logs aren't in
  Inno's `[Files]` cleanup pipeline.

- **2026-05-04 — Publish satellite-resource trim with `SatelliteResourceLanguages=en`.**
  Drops ~25 WPF framework satellite resource DLLs (zh-Hans, ja, ko, ru,
  ...) that the app never uses. ~6 MB savings on the portable zip, ~2 MB
  on the installer. Trade-off: WPF system-dialog strings on non-English
  Windows fall back to English instead of the user's locale. Acceptable
  for v0.x.

- **2026-05-04 — Defender ASR is the practical signing gate, not Microsoft's malware-submission portal.**
  Submission to https://www.microsoft.com/en-us/wdsi/filesubmission cleared
  the binary as "no malware detected" but didn't whitelist anything. ASR's
  *"Use advanced protection against ransomware"* rule (GUID
  `c1db55ab-c21a-4637-bb3f-a12568109d35`) is a separate proactive gate
  that blocks unsigned, low-prevalence executables doing ransomware-shaped
  things (writes to `%LOCALAPPDATA%`, writes to `HKCU\...\Run`, enumerates
  windows, captures screen content). Real fix is code signing.

- **2026-05-04 — `ComputeVisibleRegion` skips `WS_EX_TRANSPARENT` covering windows.**
  Same flag the overlay itself uses for click-through; same flag Win32's
  `WindowFromPoint` already skips. Adding it to the Z-order subtraction's
  filter closes the v0.3.0 Zoom-share root cause (transparent annotation/
  share-host overlays were geometrically covering WhatsApp but visually
  invisible) at the source. The visible region is now the authoritative
  "what part of WhatsApp is visible to the user" signal in both the Zoom
  case (transparent skipped → region full → blur full coverage) and the
  Spotify case (opaque → region shrinks/empty → overlay clips to visible
  part or hides). Doesn't help with recording-tool selection rectangles
  which are opaque non-transparent windows — those still subtract WhatsApp
  to zero, which is the documented v0.4.0 "Demo recording mode" follow-up.
