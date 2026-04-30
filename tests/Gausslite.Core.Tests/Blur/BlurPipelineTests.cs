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
}
