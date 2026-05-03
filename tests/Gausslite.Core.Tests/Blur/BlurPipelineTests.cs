// SPDX-License-Identifier: AGPL-3.0-or-later
using NSubstitute;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Tests.Blur;

public sealed class BlurPipelineTests
{
    private readonly IBlurInterop _interop = Substitute.For<IBlurInterop>();
    private readonly IDirect3DDevice _device = Substitute.For<IDirect3DDevice>();
    private readonly IBlurCanvasDevice _canvasDevice = Substitute.For<IBlurCanvasDevice>();

    public BlurPipelineTests()
    {
        _interop.CreateCanvasDevice(Arg.Any<IDirect3DDevice>()).Returns(_canvasDevice);
    }

    private BlurPipeline CreatePipeline() => new(_interop);

    private IBlurRenderTarget MakeRenderTarget(float width, float height)
    {
        var rt = Substitute.For<IBlurRenderTarget>();
        rt.Width.Returns(width);
        rt.Height.Returns(height);
        return rt;
    }

    private ICaptureFrame MakeFrame(float width, float height)
    {
        var frame = Substitute.For<ICaptureFrame>();
        _interop.GetFrameSize(frame).Returns((width, height));
        return frame;
    }

    [Fact]
    public void BlurFrame_BeforeInitialize_Throws()
    {
        var pipeline = CreatePipeline();
        var frame = Substitute.For<ICaptureFrame>();

        Assert.Throws<InvalidOperationException>(() => pipeline.BlurFrame(frame));
    }

    [Fact]
    public void BlurFrame_AfterDispose_Throws()
    {
        var rt = MakeRenderTarget(100, 100);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), Arg.Any<float>(), Arg.Any<float>()).Returns(rt);
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pipeline.BlurFrame(MakeFrame(100, 100)));
    }

    [Fact]
    public void BlurRadius_DefaultsTo_DefaultBlurRadius()
    {
        var pipeline = CreatePipeline();

        Assert.Equal(BlurPipeline.DefaultBlurRadius, pipeline.BlurRadius);
    }

    [Fact]
    public void BlurRadius_Default_IsMediumPreset()
    {
        Assert.Equal(BlurIntensityPresets.MediumRadius, BlurPipeline.DefaultBlurRadius);
    }

    [Fact]
    public void BlurRadius_CanBeSetAndRetrieved()
    {
        var pipeline = CreatePipeline();

        pipeline.BlurRadius = 42.5f;

        Assert.Equal(42.5f, pipeline.BlurRadius);
    }

    [Fact]
    public void Initialize_CreatesDeviceAndRenderTarget_ViaInterop()
    {
        var rt = MakeRenderTarget(100, 100);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(rt);
        var pipeline = CreatePipeline();

        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));

        _interop.Received(1).CreateCanvasDevice(_device);
        _interop.Received(1).CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 100f, 100f);
    }

    [Fact]
    public void BlurFrame_ReusesRenderTarget_WhenDimensionsUnchanged()
    {
        var rt = MakeRenderTarget(100, 100);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), Arg.Any<float>(), Arg.Any<float>()).Returns(rt);
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);

        pipeline.BlurFrame(MakeFrame(100, 100));
        pipeline.BlurFrame(MakeFrame(100, 100));

        _interop.Received(1).CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public void BlurFrame_ReallocatesRenderTarget_WhenDimensionsChange()
    {
        var rt1 = MakeRenderTarget(100, 100);
        var rt2 = MakeRenderTarget(200, 200);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(rt1);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 200f, 200f).Returns(rt2);
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);

        pipeline.BlurFrame(MakeFrame(100, 100));
        pipeline.BlurFrame(MakeFrame(200, 200));

        _interop.Received(2).CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);

        var ex = Record.Exception(() =>
        {
            pipeline.Dispose();
            pipeline.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void TryRenderCurrentFrame_BeforeAnyFrame_ReturnsNull()
    {
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);

        var result = pipeline.TryRenderCurrentFrame();

        Assert.Null(result);
        _interop.DidNotReceive().DrawBlurFromCache(
            Arg.Any<IBlurCanvasDevice>(), Arg.Any<IBlurRenderTarget>(), Arg.Any<ICachedFrame>(), Arg.Any<float>());
        _interop.DidNotReceive().FlushDevice(Arg.Any<IBlurCanvasDevice>());
    }

    [Fact]
    public void TryRenderCurrentFrame_AfterFrame_DrawsFromCacheAtCurrentRadius()
    {
        var rt = MakeRenderTarget(100, 100);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(rt);
        var cachedFrame = Substitute.For<ICachedFrame>();
        cachedFrame.Width.Returns(100f);
        cachedFrame.Height.Returns(100f);
        _interop.CreateCachedFrame(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(cachedFrame);
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));

        pipeline.BlurRadius = 42.5f;
        var result = pipeline.TryRenderCurrentFrame();

        Assert.NotNull(result);
        _interop.Received(1).DrawBlurFromCache(
            Arg.Any<IBlurCanvasDevice>(), rt, cachedFrame, 42.5f);
        _interop.Received(1).FlushDevice(Arg.Any<IBlurCanvasDevice>());
    }

    [Fact]
    public void TryRenderCurrentFrame_AfterFrame_ReturnsRenderTarget()
    {
        var rt = MakeRenderTarget(100, 100);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(rt);
        _interop.CreateCachedFrame(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(Substitute.For<ICachedFrame>());
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));

        var result = pipeline.TryRenderCurrentFrame();

        Assert.Same(rt, result);
        _interop.Received(1).FlushDevice(Arg.Any<IBlurCanvasDevice>());
    }

    // ── TryReadLatestFrameAsBgra ─────────────────────────────────────────────

    [Fact]
    public void TryReadLatestFrameAsBgra_BeforeAnyFrame_ReturnsFalse()
    {
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);

        var ok = pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);

        Assert.False(ok);
        _interop.DidNotReceive().CreateStagingTexture(
            Arg.Any<IBlurCanvasDevice>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public void TryReadLatestFrameAsBgra_OnFirstRead_AllocatesStagingTexture()
    {
        SetupFullPipeline(100, 100);
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));

        pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);

        _interop.Received(1).CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), 100f, 100f);
    }

    [Fact]
    public void TryReadLatestFrameAsBgra_SameDimensionsTwice_ReusesStagingTexture()
    {
        SetupFullPipeline(100, 100);
        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));

        pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);
        pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);

        _interop.Received(1).CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public void TryReadLatestFrameAsBgra_AfterDimensionChange_ReallocatesStagingTexture()
    {
        var rt1 = MakeRenderTarget(100, 100);
        var rt2 = MakeRenderTarget(200, 200);
        var cf1 = MakeCachedFrame(100, 100);
        var cf2 = MakeCachedFrame(200, 200);
        var st1 = Substitute.For<IBlurStagingTexture>();
        st1.Width.Returns(100f); st1.Height.Returns(100f);
        var st2 = Substitute.For<IBlurStagingTexture>();
        st2.Width.Returns(200f); st2.Height.Returns(200f);

        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(rt1);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), 200f, 200f).Returns(rt2);
        _interop.CreateCachedFrame(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(cf1);
        _interop.CreateCachedFrame(Arg.Any<IBlurCanvasDevice>(), 200f, 200f).Returns(cf2);
        _interop.CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(st1);
        _interop.CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), 200f, 200f).Returns(st2);

        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));
        pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);

        pipeline.BlurFrame(MakeFrame(200, 200));
        pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);

        _interop.Received(1).CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), 100f, 100f);
        _interop.Received(1).CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), 200f, 200f);
        st1.Received(1).Dispose(); // old staging texture disposed on resize
    }

    [Fact]
    public void TryReadLatestFrameAsBgra_Dispose_DisposeStagingTexture()
    {
        SetupFullPipeline(100, 100);
        var stagingTexture = Substitute.For<IBlurStagingTexture>();
        stagingTexture.Width.Returns(100f);
        stagingTexture.Height.Returns(100f);
        _interop.CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), 100f, 100f).Returns(stagingTexture);

        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));
        pipeline.TryReadLatestFrameAsBgra(out _, out _, out _, out _);

        pipeline.Dispose();

        stagingTexture.Received(1).Dispose();
    }

    [Fact]
    public void TryReadLatestFrameAsBgra_DelegatesToInterop()
    {
        SetupFullPipeline(100, 100);
        var fakePixels = new byte[100 * 100 * 4];
        _interop.TryReadBgra(
                Arg.Any<IBlurCanvasDevice>(), Arg.Any<ICachedFrame>(), Arg.Any<IBlurStagingTexture>(),
                out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(x => { x[3] = fakePixels; x[4] = 100; x[5] = 100; x[6] = 400; return true; });

        var pipeline = CreatePipeline();
        pipeline.Initialize(_device);
        pipeline.BlurFrame(MakeFrame(100, 100));

        var ok = pipeline.TryReadLatestFrameAsBgra(out var pixels, out var w, out var h, out var s);

        Assert.True(ok);
        Assert.Equal(fakePixels, pixels);
        Assert.Equal(100, w);
        Assert.Equal(100, h);
        Assert.Equal(400, s);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private ICachedFrame MakeCachedFrame(float width, float height)
    {
        var cf = Substitute.For<ICachedFrame>();
        cf.Width.Returns(width);
        cf.Height.Returns(height);
        return cf;
    }

    private void SetupFullPipeline(float width, float height)
    {
        var rt = MakeRenderTarget(width, height);
        var cf = MakeCachedFrame(width, height);
        var st = Substitute.For<IBlurStagingTexture>();
        st.Width.Returns(width);
        st.Height.Returns(height);
        _interop.CreateRenderTarget(Arg.Any<IBlurCanvasDevice>(), width, height).Returns(rt);
        _interop.CreateCachedFrame(Arg.Any<IBlurCanvasDevice>(), width, height).Returns(cf);
        _interop.CreateStagingTexture(Arg.Any<IBlurCanvasDevice>(), width, height).Returns(st);
    }
}
