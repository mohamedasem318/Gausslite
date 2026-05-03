// SPDX-License-Identifier: AGPL-3.0-or-later
using NSubstitute;
using Gausslite.Core.Capture;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Gausslite.Core.Tests.Capture;

public sealed class CaptureEngineTests
{
    private readonly ICaptureInterop _interop = Substitute.For<ICaptureInterop>();
    private readonly IDirect3DDevice _device = Substitute.For<IDirect3DDevice>();
    private readonly ICaptureFramePool _pool = Substitute.For<ICaptureFramePool>();
    private readonly ICaptureSession _session = Substitute.For<ICaptureSession>();

    public CaptureEngineTests()
    {
        _interop
            .CreateFreeThreadedFramePool(_device, Arg.Any<GraphicsCaptureItem>())
            .Returns(_pool);
        _interop
            .CreateSession(Arg.Any<ICaptureFramePool>(), Arg.Any<GraphicsCaptureItem>())
            .Returns(_session);
    }

    private CaptureEngine CreateEngine() => new(_interop, _device);

    [Fact]
    public void Start_SubscribesToPool_AndPropagatesFrameArrived()
    {
        var engine = CreateEngine();
        var receivedFrames = new List<ICaptureFrame>();
        engine.FrameArrived += (_, f) => receivedFrames.Add(f);

        var frame = Substitute.For<ICaptureFrame>();
        _pool.TryGetNextFrame().Returns(frame);

        engine.Start(null!);
        _pool.FrameArrived += Raise.Event();

        Assert.Single(receivedFrames);
        Assert.Same(frame, receivedFrames[0]);
    }

    [Fact]
    public void Stop_DisposesSessionAndPool()
    {
        var engine = CreateEngine();
        engine.Start(null!);
        engine.Stop();

        _session.Received(1).Dispose();
        _pool.Received(1).Dispose();
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void Start_WhenAlreadyCapturing_ThrowsInvalidOperationException()
    {
        var engine = CreateEngine();
        engine.Start(null!);

        Assert.Throws<InvalidOperationException>(() => engine.Start(null!));
    }

    [Fact]
    public void Stop_WhenNotCapturing_IsNoOp()
    {
        var engine = CreateEngine();

        var ex = Record.Exception(() => engine.Stop());

        Assert.Null(ex);
        Assert.False(engine.IsCapturing);
    }

    [Fact]
    public void FrameArrived_FrameIsDisposedAfterHandlerReturns()
    {
        var engine = CreateEngine();
        var frame = Substitute.For<ICaptureFrame>();
        _pool.TryGetNextFrame().Returns(frame);

        bool disposedDuringHandler = false;
        engine.FrameArrived += (_, _) =>
        {
            // Snapshot received calls while the handler is still on the stack.
            // Dispose must not appear here — the engine must call it only after returning.
            disposedDuringHandler = frame.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == nameof(IDisposable.Dispose));
        };

        engine.Start(null!);
        _pool.FrameArrived += Raise.Event();

        Assert.False(disposedDuringHandler);  // not disposed while handler was executing
        frame.Received(1).Dispose();           // disposed exactly once after handler returned
    }

    [Fact]
    public void FrameArrived_WhenContentSizeChanges_RecreatesPoolAndDropsTransitionFrame()
    {
        var engine = CreateEngine();
        var firstFrame = MakeFrame(100, 100);
        var transitionFrame = MakeFrame(200, 200);
        var recreatedFrame = MakeFrame(200, 200);
        var receivedFrames = new List<ICaptureFrame>();
        _pool.TryGetNextFrame().Returns(firstFrame, transitionFrame, recreatedFrame);
        engine.FrameArrived += (_, frame) => receivedFrames.Add(frame);

        engine.Start(null!);
        _pool.FrameArrived += Raise.Event();
        _pool.FrameArrived += Raise.Event();
        _pool.FrameArrived += Raise.Event();

        Assert.Equal(new[] { firstFrame, recreatedFrame }, receivedFrames);
        _pool.Received(1).Recreate(
            _device,
            Arg.Is<SizeInt32>(size => size.Width == 200 && size.Height == 200));
        transitionFrame.Received(1).Dispose();
    }

    private static ICaptureFrame MakeFrame(int width, int height)
    {
        var frame = Substitute.For<ICaptureFrame>();
        frame.ContentSize.Returns(new SizeInt32 { Width = width, Height = height });
        return frame;
    }
}
