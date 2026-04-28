# WAshed — Current State

> **For Claude Code:** Read this file at the start of every session.
> Update it at the end of every session before committing.

## Current milestone

**v0.1.0 — "Hello, blur"**

## Last session summary

Built the `BlurPipeline` module in `src/WAshed.Core/Blur/`:

- `Microsoft.Graphics.Win2D` 1.3.0 added to `WAshed.Core.csproj`. Win2D's transitive
  dependency on `Microsoft.WindowsAppSDK` injects `MrtCore.PriGen.targets` which
  requires VS2022 AppxPackage DLLs not present in the dotnet CLI SDK. Fixed by setting
  `<EnableCoreMrtTooling>false</EnableCoreMrtTooling>` in both `WAshed.Core.csproj` and
  `WAshed.Core.Tests.csproj` (class library needs no PRI resource packaging).
- `IBlurInterop` — seam for four Win2D factory-level operations: `CreateCanvasDevice`,
  `CreateRenderTarget`, `DrawBlur`, and `GetFrameSize` (frame dimension extraction)
- `IBlurCanvasDevice` / `IBlurRenderTarget` — thin mockable wrappers so no Win2D types
  leak into the public interface
- `IBlurPipeline` — public interface: `Initialize(IDirect3DDevice)`, `BlurFrame(ICaptureFrame)`
  returns `IBlurRenderTarget`, and `BlurRadius { get; set; }`
- `BlurPipeline` — concrete implementation with `DefaultBlurRadius = 20.0f`; device
  created at `Initialize`, render target allocated lazily on first `BlurFrame` and
  reallocated only when frame dimensions change; `BlurFrame` before init throws
  `InvalidOperationException`; after dispose throws `ObjectDisposedException`; `Dispose`
  is idempotent
- 8 xUnit tests in `tests/WAshed.Core.Tests/Blur/BlurPipelineTests.cs` using NSubstitute
  — all 19 tests (11 existing + 8 new) green

## Next up

**Build the `OverlayWindow` module** (`src/WAshed.Overlay/`).

Transparent always-on-top WPF window that hosts a `D3DImage` and renders the blurred
output from `BlurPipeline`.

## Blockers

None.

## Recent decisions

(See `PLAN.md` Decisions Log for the full history.)

- IDE = VS2022 throughout (driver dev in v1 requires it)
- Claude Code workflow = terminal alongside VS2022, not as IDE extension
