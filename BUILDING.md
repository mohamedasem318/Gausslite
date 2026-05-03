# Building Gausslite from source

This file is for developers / contributors / anyone who wants to run Gausslite
without using the installer. End users should use the
[Releases page](https://github.com/mohamedasem318/Gausslite/releases/latest)
instead.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| **.NET SDK** | 8.0 or newer | `dotnet --version` should print `8.x.x`. Get it from <https://dotnet.microsoft.com/download/dotnet/8.0>. |
| **Windows** | 11 22H2 (build 22621) or newer | The minimum target framework is `net8.0-windows10.0.22621.0`. |
| **Visual Studio 2022** | 17.8+ (optional) | The repo builds fine from the dotnet CLI; VS gives you debugging + designer support. The Community edition is free. |
| **WhatsApp Desktop** | Microsoft Store or Win32 build | Needed for any manual smoke test. |

## Clone and build

```powershell
git clone https://github.com/mohamedasem318/Gausslite.git
cd Gausslite
dotnet build src/Gausslite.App/Gausslite.App.csproj --configuration Release
```

The build output lands in
`src/Gausslite.App/bin/x64/Release/net8.0-windows10.0.22621.0/win-x64/`. Run
`Gausslite.App.exe` from there.

> **Why x64 specifically?**
> Win2D 1.3.0 (which we use for GPU-accelerated blur) ships native binaries
> only for x64 in our current pin, so the .csproj files set
> `<Platforms>x64</Platforms>`. ARM64 support is on the v0.4.x roadmap; it
> needs a Win2D bump and verification.
>
> Building the **solution** (`Gausslite.sln`) under AnyCPU will fail because
> Win2D's deploy step requires a concrete platform. Always build the App
> .csproj specifically (as above), or open the solution in VS where the x64
> configuration is the default.

## Run the tests

```powershell
dotnet test --arch x64 --configuration Release
```

The `--arch x64` is required: `dotnet test` without it hangs on the App test
project after discovery (a long-standing test-host / platform-target issue,
not a regression). The `--configuration Release` matches what we ship.

Expected at the time of writing: **Core 128/128, App 98/98**. If you see
failures, the test output will name the failing test and assertion — start
there.

## Project layout

```
Gausslite/
├── src/
│   ├── Gausslite.Core/         class library — blur, capture, window
│   │                            tracking, region detection, screen-share
│   │                            detection. Pure logic, testable.
│   ├── Gausslite.Overlay/      WPF overlay window: D3DImage host,
│   │                            click-through, click-through hooks.
│   └── Gausslite.App/          WPF tray app: orchestration, hotkey,
│                                composition root.
├── tests/
│   ├── Gausslite.Core.Tests/   Core unit + integration tests.
│   └── Gausslite.App.Tests/    Orchestrator + tray + overlay tests.
├── tools/                       Diagnostic / recon utilities — not
│                                 shipped. See tools/README.md.
├── installer/                   Inno Setup script + build helpers.
├── PLAN.md, STATE.md,           Living documentation. Read these in
│   HISTORY.md, CHANGELOG.md     order if you're picking up the project.
├── README.md                    User-facing entry point.
├── BUILDING.md                  This file.
├── CONTRIBUTING.md              How to propose changes.
├── COMMERCIAL.md                Commercial licensing terms.
└── LICENSE                      AGPL-3.0 full text.
```

## Common build issues

### `dotnet build` succeeds with a Win2D warning

You'll see something like:

```
warning : Microsoft.Graphics.Canvas.dll could not be copied because the
AnyCPU platform is being used. Please specify a specific platform to copy
this file.
```

This warning is **expected and harmless** when building the Core class
library directly (it has no concrete platform). The App build always uses
x64 and copies the native binary correctly. Don't chase it.

### `dotnet test` hangs forever after "Discovering tests"

You forgot `--arch x64`. The App test project hangs on AnyCPU but works fine
under x64. See above.

### `Gausslite.App.exe` launches and immediately exits

Look in `gausslite-startup.log` and `gausslite-crash.log` next to the
executable. The startup log traces every step of `OnStartup`; the crash log
captures unhandled exceptions. The most common causes are missing
`Assets/tray-icon.ico` (rebuild) and a tray-icon registration failure
(reboot, or check that the Windows shell isn't blocking notify icons).

### "Cannot find Microsoft.Graphics.Canvas.dll"

Make sure you built `Gausslite.App.csproj` directly (not the solution under
AnyCPU), and that the build output directory contains the
`runtimes/win-x64/native/` subdirectory.

## Smoke testing

After any non-trivial change, smoke test in this order:

1. **Tray UX**: launch, left-click tray (toggle blur), right-click → menu,
   hotkey Ctrl+Shift+B.
2. **Capture**: enable blur with WhatsApp visible — it should blur within
   ~500 ms.
3. **Auto-blur on share**: start a Zoom or Teams share — blur should fire
   within ~2 seconds.
4. **Manual override during share**: with auto-blur on, disable manually —
   the friendly tray balloon should appear once.
5. **Restore on share end**: stop the share — blur returns to its pre-share
   state.

If something feels off, `gausslite-startup.log` is verbose and timestamped.
Grep for `ScreenShare:`, `EnableBlur`, `OnVisibleRegionChanged`, or
`PresentFrame` to narrow in on a specific code path.

## Bumping versions

Versions are tracked in:

- [`CHANGELOG.md`](CHANGELOG.md) — user-facing release notes.
- Git tags — `v0.3.0`, etc., applied at release time.
- The Inno Setup script (`installer/Gausslite.iss`) reads the version from
  the build script.

When shipping a new release: move `CHANGELOG.md`'s `[Unreleased]` heading to
`[X.Y.Z] - YYYY-MM-DD`, build the installer, tag the commit, push the tag,
create a GitHub Release with the binaries attached.

## Useful pointers

- [PLAN.md](PLAN.md) — milestones and architecture.
- [STATE.md](STATE.md) — what's actively in progress.
- [HISTORY.md](HISTORY.md) — verbose session-by-session notes (long; use as
  a reference, not bedtime reading).
- [tools/README.md](tools/README.md) — diagnostic utilities that exist
  outside the main solution.
