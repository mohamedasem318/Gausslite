# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Built the `WindowTracker` module in `src/WAshed.Core/WindowTracking/`:

- `RECT` struct (Win32 layout)
- `IWin32Api` interface wrapping all P/Invoke calls
- `Win32Api` concrete implementation using `GetWindowRect`, `GetDpiForWindow`, and
  `Process.GetProcessesByName` / `MainWindowHandle`
- `IWindowTracker` interface with `BoundsChanged` event, `CurrentBounds`, `IsTracking`,
  `Start()`, and `Stop()`
- `WindowTracker` implementation: 10 Hz background polling, DPI-aware logical→physical
  pixel conversion, fires `BoundsChanged` only on actual bounds changes
- 6 xUnit tests in `tests/WAshed.Core.Tests/WindowTracking/WindowTrackerTests.cs` using
  NSubstitute — all green

Both `WAshed.Core` and `WAshed.Core.Tests` now target `net8.0-windows` with `UseWPF`
enabled so `System.Windows.Rect` resolves.

## Next up

**Build the `CaptureEngine` module** (`src/WAshed.Core/Capture/`).

Wraps `Windows.Graphics.Capture` (WinRT) to expose per-frame `IDirect3DSurface`
textures. Public API:

- `ICaptureEngine` interface with `Start(GraphicsCaptureItem)`, `Stop()`, and a
  `FrameArrived` event carrying the `Direct3D11CaptureFrame`
- Default `CaptureEngine` class using `GraphicsCaptureSession` +
  `Direct3D11CaptureFramePool`
- Wrap any WinRT/interop surface behind `ICaptureInterop` for unit-test seams
- Tests in `tests/WAshed.Core.Tests/Capture/` using NSubstitute

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension
