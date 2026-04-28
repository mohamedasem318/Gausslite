# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Built the `CaptureEngine` module in `src/WAshed.Core/Capture/`:

- Both csproj TFMs bumped to `net8.0-windows10.0.19041.0` to unlock
  `Windows.Graphics.Capture` (including `CreateFreeThreaded`, added in build 19041)
- `ICaptureInterop` — seam for three factory-level WinRT calls:
  `CreateDirect3DDevice`, `CreateFreeThreadedFramePool`, and `CreateSession`
- `ICaptureFramePool` / `ICaptureSession` / `ICaptureFrame` — thin mockable wrappers
  over the sealed WinRT runtime classes; all implement `IDisposable`
- `ICaptureEngine` — public interface: `Start(GraphicsCaptureItem)`, `Stop()`,
  `IsCapturing`, and `FrameArrived` (fires on thread-pool thread; callers must not
  retain the frame after the handler returns)
- `CaptureEngine` — implementation: free-threaded pool, disposes frame in `finally`
  after raising `FrameArrived`, `Start` throws if already capturing, `Stop` is idempotent
- 5 xUnit tests in `tests/WAshed.Core.Tests/Capture/CaptureEngineTests.cs` using
  NSubstitute — all 11 tests (6 existing + 5 new) green

## Next up

**Build the `BlurPipeline` module** (`src/WAshed.Core/Blur/`).

Win2D-based pipeline that receives `ICaptureFrame` from `CaptureEngine`, blurs the
entire captured texture (region-aware blurring comes in v0.2.0), and exposes the
result for `OverlayWindow` to render via D3DImage.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension
