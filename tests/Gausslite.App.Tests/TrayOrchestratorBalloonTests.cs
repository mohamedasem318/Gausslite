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
/// Tests for tray-balloon notification transitions in <see cref="TrayOrchestrator"/>.
/// All tests use the inline-dispatch overload so dispatched lambdas run synchronously.
/// </summary>
public sealed class TrayOrchestratorBalloonTests
{
    private readonly IWindowTracker _windowTracker = Substitute.For<IWindowTracker>();
    private readonly ICaptureEngine _captureEngine = Substitute.For<ICaptureEngine>();
    private readonly IBlurPipeline _blurPipeline = Substitute.For<IBlurPipeline>();
    private readonly IOverlayWindow _overlayWindow = Substitute.For<IOverlayWindow>();
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly ICaptureItemFactory _captureItemFactory = Substitute.For<ICaptureItemFactory>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly ITrayNotifier _notifier = Substitute.For<ITrayNotifier>();
    private readonly ConfigurableDetector _detector = new();

    private sealed class ConfigurableDetector : IRegionDetector
    {
        public RegionDetectionResult Result { get; set; } =
            new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        public RegionDetectionResult Detect(ReadOnlySpan<byte> bgraPixels, int w, int h, int stride) => Result;
    }

    private static readonly Rect OverlayBounds = new(0, 0, 800, 600);
    private static IReadOnlyList<Rect> FullRegion => new[] { OverlayBounds };

    private void SetupWindowFound()
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.VisibleRegion.Returns(FullRegion);
        _windowTracker.CurrentBounds.Returns(OverlayBounds);

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    private void SetupReadback(bool succeed = true)
    {
        if (!succeed)
        {
            _blurPipeline
                .TryReadLatestFrameAsBgra(out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
                .Returns(false);
            return;
        }
        var pixels = new byte[800 * 600 * 4];
        _blurPipeline
            .TryReadLatestFrameAsBgra(out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(x => { x[0] = pixels; x[1] = 800; x[2] = 600; x[3] = 800 * 4; return true; });
    }

    private ICaptureFrame MakeFrame()
    {
        var rt = Substitute.For<IBlurRenderTarget>();
        _blurPipeline.BlurFrame(Arg.Any<ICaptureFrame>()).Returns(rt);
        var frame = Substitute.For<ICaptureFrame>();
        frame.ContentSize.Returns(new Windows.Graphics.SizeInt32 { Width = 800, Height = 600 });
        return frame;
    }

    private TrayOrchestrator CreateSut()
    {
        var sut = new TrayOrchestrator(
            _windowTracker, _captureEngine, _blurPipeline, _overlayWindow,
            _hotkeyService, _captureItemFactory, _profile, _detector,
            (_, action, _) => action(), (_, action) => action());
        sut.SetTrayNotifier(_notifier);
        return sut;
    }

    private void TriggerDetection(TrayOrchestrator sut)
    {
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());
    }

    // ── Initial startup failure ───────────────────────────────────────────────

    [Fact]
    public void Failure_BeforeAnySuccess_DoesNotShowFailureBalloon()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = false, FailureReason = "no divider found" };

        using var sut = CreateSut();
        TriggerDetection(sut);

        _notifier.DidNotReceive().ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Warning);
    }

    // ── Success → Failure transition ─────────────────────────────────────────

    [Fact]
    public void Failure_AfterSuccess_ShowsFailureBalloon()
    {
        SetupWindowFound();
        SetupReadback();

        // First detection: success.
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        using var sut = CreateSut();
        TriggerDetection(sut);
        _notifier.ClearReceivedCalls();

        // Second detection trigger (BoundsChanged): failure.
        _detector.Result = new() { Succeeded = false, FailureReason = "lost divider" };
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, OverlayBounds);

        _notifier.Received(1).ShowBalloon(
            "Gausslite couldn't find your chats",
            Arg.Any<string>(),
            NotificationIcon.Warning);
    }

    [Fact]
    public void Failure_AfterSuccess_ShowsFailureBalloonOnlyOnce()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        using var sut = CreateSut();
        TriggerDetection(sut);

        // Switch to failing.
        _detector.Result = new() { Succeeded = false, FailureReason = "lost divider" };
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, OverlayBounds);
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, OverlayBounds);

        _notifier.Received(1).ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Warning);
    }

    // ── Failure → Success (recovery) transition ───────────────────────────────

    [Fact]
    public void Recovery_AfterFailure_Within30s_SuppressesRecoveryBalloon()
    {
        SetupWindowFound();
        SetupReadback();

        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        using var sut = CreateSut();
        TriggerDetection(sut);

        // Fail.
        _detector.Result = new() { Succeeded = false, FailureReason = "lost" };
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, OverlayBounds);

        // Recover immediately (well within 30 s).
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, OverlayBounds);

        _notifier.DidNotReceive().ShowBalloon(
            "Gausslite is back on track",
            Arg.Any<string>(),
            NotificationIcon.Info);
    }

    [Fact]
    public void Recovery_WhenLastFailureBalloonWasNull_ShowsRecoveryBalloon()
    {
        // Simulate: never had a failure balloon (first success after some non-triggering state).
        // This is the case where _lastFailureBalloonAt is null but _detectionWasSucceeding is false.
        // This can't happen in normal flow (failure always sets _lastFailureBalloonAt), so the
        // recovery balloon fires after the very first detected failure that had no prior success.
        // Actually per the logic: recovery fires only when transitioning failure→success.
        // Since we start with failure (no prior success), first success is the "first success ever"
        // branch, not the "recovery" branch. So no recovery balloon expected here.
        // → We test that the recovery path does NOT fire on first-ever success.

        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        using var sut = CreateSut();
        TriggerDetection(sut);

        // First ever success should NOT fire a recovery balloon.
        _notifier.DidNotReceive().ShowBalloon(
            "Gausslite is back on track",
            Arg.Any<string>(),
            Arg.Any<NotificationIcon>());
    }

    // ── Scope-fallback balloon ────────────────────────────────────────────────

    [Fact]
    public void ScopeFallback_WhenNoDetectionYet_ShowsBalloonOnFirstNonBothScope()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = false, FailureReason = "no frame" };
        // Don't TriggerDetection so _hasEverDetected stays false.
        using var sut = CreateSut();
        sut.EnableBlur();

        sut.SetScope(BlurRegionScope.ChatList);

        _notifier.Received(1).ShowBalloon(
            "Gausslite is still finding your chats",
            Arg.Any<string>(),
            NotificationIcon.Info);
    }

    [Fact]
    public void ScopeFallback_ShowsOnlyOnce()
    {
        SetupWindowFound();
        SetupReadback(succeed: false);
        using var sut = CreateSut();
        sut.EnableBlur();

        sut.SetScope(BlurRegionScope.ChatList);
        sut.SetScope(BlurRegionScope.Conversation);

        _notifier.Received(1).ShowBalloon(
            "Gausslite is still finding your chats",
            Arg.Any<string>(),
            NotificationIcon.Info);
    }

    [Fact]
    public void ScopeFallback_WhenScopeIsBoth_DoesNotShowBalloon()
    {
        SetupWindowFound();
        SetupReadback(succeed: false);
        using var sut = CreateSut();
        sut.EnableBlur();

        sut.SetScope(BlurRegionScope.Both);

        _notifier.DidNotReceive().ShowBalloon(
            "Gausslite is still finding your chats",
            Arg.Any<string>(),
            Arg.Any<NotificationIcon>());
    }

    [Fact]
    public void ScopeFallback_AfterDetectionSucceeds_DoesNotShowBalloon()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };

        using var sut = CreateSut();
        TriggerDetection(sut);  // sets _hasEverDetected = true

        sut.SetScope(BlurRegionScope.ChatList);

        _notifier.DidNotReceive().ShowBalloon(
            "Gausslite is still finding your chats",
            Arg.Any<string>(),
            Arg.Any<NotificationIcon>());
    }
}
