# Contributing to Gausslite

Thanks for considering a contribution. This document covers what gets merged,
how to propose changes, and the (light) license-assignment terms for
contributions.

## TL;DR

- **Bug reports** → open an issue with reproduction steps.
- **Small fixes** (typos, tightening up code, fixing a clear bug) → open a
  PR directly; I'll review it.
- **New features** or **architectural changes** → open an issue first to
  discuss the shape before you write code.
- **By submitting a PR**, you agree your contribution is licensed under
  AGPL-3.0 *and* may be sublicensed under the project's commercial license
  (see [COMMERCIAL.md](COMMERCIAL.md)). Details below.

## Bug reports

Open an [issue](https://github.com/mohamedasem318/Gausslite/issues/new) with:

1. **What happened** vs. what you expected.
2. **Reproduction steps** — exactly what you did, in order.
3. **Environment**: Windows version, Gausslite version, GPU, sharing app
   (Zoom / Teams / etc.) and version.
4. **Logs** if relevant: `gausslite-startup.log` and / or
   `gausslite-crash.log` next to the executable. Skim them first for
   anything sensitive (window titles, file paths) before pasting.

The smaller and more focused the report, the faster it gets fixed.

## Pull requests

### What gets merged easily

- Bug fixes with a clear reproduction, ideally with a regression test.
- Doc improvements, typo fixes, README clarity.
- New `IAppProfile` implementations for additional chat clients (post v0.6).
- New `ShareSignature` entries for additional sharing apps.
- Performance improvements with measurements.

### What needs discussion first

- New top-level features.
- Changes to public interfaces (`IBlurPipeline`, `IWindowTracker`,
  `IScreenShareDetector`, etc.).
- Anything that touches the privacy-critical paths
  (occlusion clipping, capture pipeline, overlay visibility logic).
- Dependencies on new NuGet packages.
- Changes that affect supported Windows versions or GPU requirements.

For these, please open an issue first. Saves you from writing code that I'd
ask you to rework.

### Style and conventions

- **No new comments** unless the *why* is non-obvious — names should do the
  explanation. See the existing code for examples.
- **Tests for non-trivial logic.** The repo aims for fast, deterministic
  tests using NSubstitute and inline-dispatch overloads (no real timers /
  threads / dispatchers in tests). Look at `TrayOrchestratorTests` and
  `WindowSignalScreenShareDetectorTests` for the pattern.
- **No new comments commented-out code** or `// removed:` markers — git
  remembers what was there.
- **Conventional commits** in PR titles when possible: `feat(scope): ...`,
  `fix(scope): ...`, `docs: ...`, `refactor: ...`, `test: ...`. Not strict.
- **One logical change per PR.** If you find yourself wanting two unrelated
  things, two PRs.
- **SPDX header** at the top of any new `.cs` file:

  ```csharp
  // SPDX-License-Identifier: AGPL-3.0-or-later
  ```

  See existing files for the convention.

### Build and test before pushing

```powershell
dotnet build src/Gausslite.App/Gausslite.App.csproj --configuration Release
dotnet test --arch x64 --configuration Release
```

Tests must be green. Build must be clean (one pre-existing Win2D AnyCPU
warning is expected; no other warnings).

For UI / blur / capture changes, add a one-line note in the PR description
about the manual smoke test you ran. The orchestration / clip / capture
paths are not covered by automated tests; manual smoke is the safety net.

## License assignment for contributions

Gausslite is released under [AGPL-3.0](LICENSE) and dual-licensed for
commercial use (see [COMMERCIAL.md](COMMERCIAL.md)). To keep that model
viable, contributions need a clean license assignment.

**By submitting a pull request, you agree that:**

1. Your contribution is your original work or you have the right to submit
   it.
2. You license your contribution to the project under AGPL-3.0.
3. You grant the project maintainer the right to sublicense your
   contribution under the project's commercial license terms.

This is a lightweight equivalent of a CLA (Contributor License Agreement)
without the paperwork. It's what lets the project both stay open-source
*and* offer commercial licenses to companies that can't use AGPL — same
model GitLab, MongoDB, Mattermost, and many others use.

If your employer has policies that prevent you from contributing under
these terms, please don't submit code; open an issue instead so we can
talk about whether there's a different way for you to help.

## Code of conduct

Be civil. Disagreements are fine; insults aren't. The maintainer reserves
the right to close discussions or remove contributions that don't meet
this bar.

## Recognition

All contributors are listed via the GitHub contributor graph automatically.
For significant contributions, an explicit thank-you also lands in the
relevant CHANGELOG entry.
