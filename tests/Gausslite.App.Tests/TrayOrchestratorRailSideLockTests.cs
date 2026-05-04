// SPDX-License-Identifier: AGPL-3.0-or-later
using NSubstitute;
using System.Windows;
using Gausslite.App.Hotkey;
using Gausslite.App.Orchestration;
using Gausslite.App.Tray;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;

namespace Gausslite.App.Tests;

/// <summary>
/// Tests the sticky rail-side lock in <see cref="TrayOrchestrator"/>.
/// The lock guards against the detector's rail-side heuristic flipping on small
/// per-frame visual perturbations (e.g. the user pressing Shift+Alt to switch
/// Windows input language causes a tiny indicator change in WhatsApp's
/// text-input area; the detector flips Left → Right; "scope=Conversation"
/// then clips against what is visually the chat list).
/// </summary>
public sealed class TrayOrchestratorRailSideLockTests
{
    private readonly IWindowTracker _windowTracker = Substitute.For<IWindowTracker>();
    private readonly ICaptureEngine _captureEngine = Substitute.For<ICaptureEngine>();
    private readonly IBlurPipeline _blurPipeline = Substitute.For<IBlurPipeline>();
    private readonly IOverlayWindow _overlayWindow = Substitute.For<IOverlayWindow>();
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly ICaptureItemFactory _captureItemFactory = Substitute.For<ICaptureItemFactory>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly ConfigurableDetector _detector = new();

    private sealed class ConfigurableDetector : IRegionDetector
    {
        public RegionDetectionResult Result { get; set; } =
            new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        public RegionDetectionResult Detect(ReadOnlySpan<byte> bgraPixels, int w, int h, int stride) => Result;
    }

    private const double OverlayW = 800;
    private const double OverlayH = 600;
    private static readonly Rect OverlayBounds = new(0, 0, OverlayW, OverlayH);

    private const int ContentW = 814;
    private const int ContentH = 607;

    // Default LTR detection result: chat list on the left, conversation on the right.
    private static readonly RegionDetectionResult LtrResult = new()
    {
        Succeeded        = true,
        DetectedRailSide = RailSide.Left,
        ChatListRect     = new Rect(0,   0, 300,           ContentH),
        ConversationRect = new Rect(300, 0, ContentW - 300, ContentH),
    };

    // Same divider, but now claiming RTL: chat list on the right, conversation on the left.
    private static readonly RegionDetectionResult RtlResult = new()
    {
        Succeeded        = true,
        DetectedRailSide = RailSide.Right,
        ChatListRect     = new Rect(ContentW - 300, 0, 300,           ContentH),
        ConversationRect = new Rect(0,              0, ContentW - 300, ContentH),
    };

    private void SetupWindowFound()
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.VisibleRegion.Returns(new[] { OverlayBounds });
        _windowTracker.CurrentBounds.Returns(OverlayBounds);

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    private void SetupReadback()
    {
        var pixels = new byte[ContentW * ContentH * 4];
        _blurPipeline
            .TryReadLatestFrameAsBgra(out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(x => { x[0] = pixels; x[1] = ContentW; x[2] = ContentH; x[3] = ContentW * 4; return true; });
    }

    private ICaptureFrame MakeFrame()
    {
        var rt = Substitute.For<IBlurRenderTarget>();
        _blurPipeline.BlurFrame(Arg.Any<ICaptureFrame>()).Returns(rt);
        var frame = Substitute.For<ICaptureFrame>();
        frame.ContentSize.Returns(new Windows.Graphics.SizeInt32 { Width = ContentW, Height = ContentH });
        return frame;
    }

    private TrayOrchestrator CreateSut() => new(
        _windowTracker, _captureEngine, _blurPipeline, _overlayWindow,
        _hotkeyService, _captureItemFactory, _profile, _detector,
        (_, action, _) => action(), (_, action) => action());

    // Cadence is every 30 frames — fire 30 FrameArrived events to force a re-detection.
    private void RaiseFramesUntilNextDetection()
    {
        for (int i = 0; i < 30; i++)
            _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());
    }

    [Fact]
    public void FirstSuccessfulDetection_LocksRailSide()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = LtrResult;

        using var sut = CreateSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());

        Assert.Equal(RailSide.Left, sut.LockedRailSide);
        Assert.True(sut.LastDetectionResult!.Value.Succeeded);
        Assert.Equal(RailSide.Left, sut.LastDetectionResult!.Value.DetectedRailSide);
        Assert.Equal(LtrResult.ChatListRect,     sut.LastDetectionResult!.Value.ChatListRect);
        Assert.Equal(LtrResult.ConversationRect, sut.LastDetectionResult!.Value.ConversationRect);
    }

    [Fact]
    public void SubsequentDetection_AgreesWithLock_PassesThroughUnchanged()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = LtrResult;

        using var sut = CreateSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());

        // Re-trigger detection; detector still says Left.
        RaiseFramesUntilNextDetection();

        Assert.Equal(RailSide.Left, sut.LockedRailSide);
        Assert.Equal(LtrResult.ChatListRect,     sut.LastDetectionResult!.Value.ChatListRect);
        Assert.Equal(LtrResult.ConversationRect, sut.LastDetectionResult!.Value.ConversationRect);
    }

    [Fact]
    public void SubsequentDetection_DisagreesWithLock_SwapsRectsBackToLockedOrientation()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = LtrResult;

        using var sut = CreateSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());
        Assert.Equal(RailSide.Left, sut.LockedRailSide);

        // Detector flips to Right (the bug scenario — Shift+Alt input-language switch
        // causes a transient pixel change that flips the heuristic). The lock should
        // hold and the rects should be swapped back into Left orientation.
        _detector.Result = RtlResult;
        RaiseFramesUntilNextDetection();

        // Lock unchanged.
        Assert.Equal(RailSide.Left, sut.LockedRailSide);

        // Stored result reports the locked rail side, not the detector's reading.
        Assert.Equal(RailSide.Left, sut.LastDetectionResult!.Value.DetectedRailSide);

        // Rects swapped: detector returned (chatList=right300, conv=left514) but we
        // stored (chatList=left514, conv=right300) — the post-swap orientation matches
        // what the LtrResult labelling would produce on the same divider position.
        Assert.Equal(RtlResult.ConversationRect, sut.LastDetectionResult!.Value.ChatListRect);
        Assert.Equal(RtlResult.ChatListRect,     sut.LastDetectionResult!.Value.ConversationRect);
    }

    [Fact]
    public void DetectionFailure_DoesNotEstablishOrAffectLock()
    {
        SetupWindowFound();
        SetupReadback();

        // First "detection" returns failure — lock must stay null.
        _detector.Result = new RegionDetectionResult { Succeeded = false, FailureReason = "test" };

        using var sut = CreateSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());

        Assert.Null(sut.LockedRailSide);

        // Now produce a successful result on the next detection — lock should engage.
        _detector.Result = LtrResult;
        RaiseFramesUntilNextDetection();

        Assert.Equal(RailSide.Left, sut.LockedRailSide);
    }

    [Fact]
    public void TearDownCaptureAndOverlay_ResetsLock()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = LtrResult;

        using var sut = CreateSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());
        Assert.Equal(RailSide.Left, sut.LockedRailSide);

        // Disabling blur tears down the capture session — lock should reset.
        sut.DisableBlur();
        Assert.Null(sut.LockedRailSide);

        // Re-enable + new detection with the OPPOSITE side: lock should re-establish
        // to the new side, since teardown legitimately means "session reset".
        _detector.Result = RtlResult;
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());

        Assert.Equal(RailSide.Right, sut.LockedRailSide);
    }

    [Fact]
    public void TearDownCaptureAndOverlay_ClearsBlurPipelineFrameCache()
    {
        // The lock-on-stale-frame bug: after DisableBlur tears down the capture session
        // but the BlurPipeline's cached frame survives, an OnBoundsChanged that fires
        // before the new session's first frame would run detection on stale pixels —
        // locking the rail side based on the previous session's WhatsApp UI direction.
        // TearDown must clear the BlurPipeline cache so the next session's RunDetection
        // returns "no frame" until a real new-session frame is blurred.
        SetupWindowFound();
        SetupReadback();
        _detector.Result = LtrResult;

        using var sut = CreateSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());

        sut.DisableBlur();

        _blurPipeline.Received().ClearCachedFrame();
    }
}
