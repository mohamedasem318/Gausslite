# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

- Project planned end-to-end with Claude (Opus 4.7)
- License chosen: AGPL-3.0
- Name chosen: WAshed
- Architecture chosen: WPF + Win2D + Windows.Graphics.Capture for v0;
  IDD driver added in v1
- Module map defined (see PLAN.md)
- Repo not yet scaffolded

## Next up

**Scaffold the repository.**

1. Create private GitHub repo `WAshed`
2. Clone locally
3. Add `LICENSE`, `README.md`, `PLAN.md`, `STATE.md`, `CHANGELOG.md`,
   `.gitignore` from drafts
4. Initial commit: `chore: scaffold repository with planning documents`
5. Push to remote
6. Open VS2022, create blank solution `WAshed.sln` in repo root
7. Add empty class library projects per module map (see PLAN.md)
8. Commit: `chore: create empty project structure`
9. **Then** start a Claude Code session for the first module: `WindowTracker`

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension

## Notes for next Claude Code session

When you start the WindowTracker session, the prompt to give Claude Code is:

> Read `PLAN.md` and `STATE.md`. We're building the WindowTracker module per
> the module map. It lives in `src/WAshed.Core/WindowTracking/`. Public API:
> `IWindowTracker` interface with a `BoundsChanged` event and a `CurrentBounds`
> property (returns `Rect` in physical pixels). Implementation finds the
> WhatsApp Desktop window by process name, polls position/size at ~10Hz,
> raises `BoundsChanged` on changes, handles DPI via `GetDpiForWindow`.
> Wrap all Win32 calls behind an `IWin32Api` interface so unit tests can
> mock them. Write the implementation, then write xUnit tests in
> `tests/WAshed.Core.Tests/WindowTracking/` using NSubstitute for the
> `IWin32Api` mock. When done, update STATE.md and propose a commit message.