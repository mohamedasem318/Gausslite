using Windows.Graphics.Capture;

namespace WAshed.Core.Tests.Blur;

/// <summary>
/// Tests for Win2DBlurRenderTarget.
///
/// Win2D's CanvasDevice, CanvasRenderTarget, and the underlying ID3D11Device are sealed
/// WinRT/COM types that cannot be mocked with NSubstitute. All tests here therefore
/// require a real GPU and are gated on <see cref="GraphicsCaptureSession.IsSupported"/>
/// (which returns false in CI environments without hardware D3D11 support).
///
/// Per project convention (PLAN.md), GPU/smoke-test code is not unit-tested; this file
/// documents the expected contracts and provides a harness for manual developer verification.
/// The dispose-idempotency contract at the BlurPipeline level is already covered by
/// BlurPipelineTests.Dispose_IsIdempotent.
/// </summary>
public sealed class Win2DBlurRenderTargetTests
{
    private static bool GpuAvailable => GraphicsCaptureSession.IsSupported();

    /// <summary>
    /// GetDirect3DSurface must return a non-null surface and the same instance on every call.
    /// Requires real D3D11 hardware.
    /// </summary>
    [Fact]
    public void GetDirect3DSurface_ReturnsNonNull_AndStableAcrossCalls()
    {
        if (!GpuAvailable) return; // skip in CI without GPU

        using var rt = GpuTestHelper.CreateRenderTarget(320, 240);

        var s1 = rt.GetDirect3DSurface();
        var s2 = rt.GetDirect3DSurface();

        Assert.NotNull(s1);
        Assert.Same(s1, s2);
    }

    /// <summary>
    /// Calling Dispose() twice must not throw.
    /// Requires real D3D11 hardware.
    /// </summary>
    [Fact]
    public void Dispose_IsIdempotent()
    {
        if (!GpuAvailable) return;

        var rt = GpuTestHelper.CreateRenderTarget(64, 64);

        var ex = Record.Exception(() =>
        {
            rt.Dispose();
            rt.Dispose();
        });

        Assert.Null(ex);
    }

    /// <summary>
    /// Width and Height must match the values supplied at construction.
    /// Requires real D3D11 hardware.
    /// </summary>
    [Fact]
    public void WidthAndHeight_MatchConstructorArgs()
    {
        if (!GpuAvailable) return;

        using var rt = GpuTestHelper.CreateRenderTarget(512, 384);

        Assert.Equal(512f, rt.Width);
        Assert.Equal(384f, rt.Height);
    }
}
