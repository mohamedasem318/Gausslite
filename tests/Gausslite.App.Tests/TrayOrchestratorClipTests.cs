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
/// Tests for scope-aware clip composition in <see cref="TrayOrchestrator"/>.
/// All tests use the inline-dispatch overload so dispatched lambdas run synchronously.
/// </summary>
public sealed class TrayOrchestratorClipTests
{
    private readonly IWindowTracker _windowTracker = Substitute.For<IWindowTracker>();
    private readonly ICaptureEngine _captureEngine = Substitute.For<ICaptureEngine>();
    private readonly IBlurPipeline _blurPipeline = Substitute.For<IBlurPipeline>();
    private readonly IOverlayWindow _overlayWindow = Substitute.For<IOverlayWindow>();
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly ICaptureItemFactory _captureItemFactory = Substitute.For<ICaptureItemFactory>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly ConfigurableDetector _detector = new();

    // Avoids NSubstitute limitations with ReadOnlySpan<byte> parameters.
    private sealed class ConfigurableDetector : IRegionDetector
    {
        public RegionDetectionResult Result { get; set; } =
            new() { Succeeded = true, DetectedRailSide = RailSide.Left };
        public RegionDetectionResult Detect(ReadOnlySpan<byte> bgraPixels, int w, int h, int stride) => Result;
    }

    // Full-screen visible region, matching overlay bounds exactly.
    private static IReadOnlyList<Rect> FullRegion(Rect bounds) => new[] { bounds };
    private static IReadOnlyList<Rect> EmptyRegion => Array.Empty<Rect>();

    private const double OverlayW = 800;
    private const double OverlayH = 600;
    private static readonly Rect OverlayBounds = new(0, 0, OverlayW, OverlayH);

    // Content slightly smaller than overlay (simulates 14×7 WGC gap).
    private const int ContentW = 814;
    private const int ContentH = 607;

    private void SetupWindowFound(IReadOnlyList<Rect>? visibleRegion = null)
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.VisibleRegion.Returns(visibleRegion ?? FullRegion(OverlayBounds));
        _windowTracker.CurrentBounds.Returns(OverlayBounds);

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    private void SetupReadback(int width = ContentW, int height = ContentH)
    {
        var pixels = new byte[width * height * 4];
        _blurPipeline
            .TryReadLatestFrameAsBgra(out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(x => { x[0] = pixels; x[1] = width; x[2] = height; x[3] = width * 4; return true; });
    }

    private ICaptureFrame MakeFrame()
    {
        var rt = Substitute.For<IBlurRenderTarget>();
        _blurPipeline.BlurFrame(Arg.Any<ICaptureFrame>()).Returns(rt);
        var frame = Substitute.For<ICaptureFrame>();
        frame.ContentSize.Returns(new Windows.Graphics.SizeInt32 { Width = ContentW, Height = ContentH });
        return frame;
    }

    // Build orchestrator with inline dispatch (synchronous), no notifier.
    private TrayOrchestrator CreateSut() => new(
        _windowTracker, _captureEngine, _blurPipeline, _overlayWindow,
        _hotkeyService, _captureItemFactory, _profile, _detector,
        (_, action, _) => action(), (_, action) => action());

    // Enable blur and simulate a captured frame so detection runs.
    private void EnableAndDetect(TrayOrchestrator sut)
    {
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());
    }

    // ── scope=Both ───────────────────────────────────────────────────────────

    [Fact]
    public void Scope_Both_FullRegion_ClearsClip()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left,
            ChatListRect = new Rect(0, 0, 300, ContentH),
            ConversationRect = new Rect(300, 0, ContentW - 300, ContentH) };

        using var sut = CreateSut();
        sut.SetScope(BlurRegionScope.Both);
        EnableAndDetect(sut);

        // Full region + Both → clip cleared (null).
        _overlayWindow.Received().SetClip(null);
        _overlayWindow.DidNotReceive().SetClip(Arg.Is<IReadOnlyList<Rect>>(r => r != null));
    }

    // ── scope=ChatList + LTR success ─────────────────────────────────────────

    [Fact]
    public void Scope_ChatList_DetectionSuccess_LTR_IntersectsLeftHalf()
    {
        SetupWindowFound();
        SetupReadback();

        // Chat list = left 300px of 814-wide content.
        _detector.Result = new()
        {
            Succeeded = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 300, ContentH),
            ConversationRect = new Rect(300, 0, ContentW - 300, ContentH),
        };

        using var sut = CreateSut();
        sut.SetScope(BlurRegionScope.ChatList);
        EnableAndDetect(sut);

        // The scope rect in overlay DIPs: x=0, width = 300 * (800/814)
        double expectedW = 300.0 * (OverlayW / ContentW);
        _overlayWindow.Received().SetClip(Arg.Is<IReadOnlyList<Rect>>(rects =>
            rects.Count == 1
            && Math.Abs(rects[0].X - 0) < 0.5
            && Math.Abs(rects[0].Width - expectedW) < 0.5
            && rects[0].Y == 0
            && Math.Abs(rects[0].Height - OverlayH) < 0.5));
    }

    // ── scope=ChatList + RTL success ─────────────────────────────────────────

    [Fact]
    public void Scope_ChatList_DetectionSuccess_RTL_IntersectsRightHalf()
    {
        SetupWindowFound();
        SetupReadback();

        // RTL: chat list = right 300px of 814-wide content.
        _detector.Result = new()
        {
            Succeeded = true,
            DetectedRailSide = RailSide.Right,
            ChatListRect     = new Rect(ContentW - 300, 0, 300, ContentH),
            ConversationRect = new Rect(0, 0, ContentW - 300, ContentH),
        };

        using var sut = CreateSut();
        sut.SetScope(BlurRegionScope.ChatList);
        EnableAndDetect(sut);

        double scaleX = OverlayW / ContentW;
        double expectedX = (ContentW - 300) * scaleX;
        double expectedW = 300.0 * scaleX;

        _overlayWindow.Received().SetClip(Arg.Is<IReadOnlyList<Rect>>(rects =>
            rects.Count == 1
            && Math.Abs(rects[0].X - expectedX) < 0.5
            && Math.Abs(rects[0].Width - expectedW) < 0.5));
    }

    // ── scope=ChatList + detection failure ───────────────────────────────────

    [Fact]
    public void Scope_ChatList_DetectionFailure_PassesVisibleRectsThrough()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = false, FailureReason = "test failure" };

        using var sut = CreateSut();
        sut.SetScope(BlurRegionScope.ChatList);
        EnableAndDetect(sut);

        // Failure → no scope rect → visible rects pass through unchanged.
        // Full-region case hits the null-clip optimisation path.
        _overlayWindow.Received().SetClip(null);
    }

    // ── scope=Conversation + LTR success ─────────────────────────────────────

    [Fact]
    public void Scope_Conversation_DetectionSuccess_LTR_IntersectsRightHalf()
    {
        SetupWindowFound();
        SetupReadback();

        _detector.Result = new()
        {
            Succeeded = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 300, ContentH),
            ConversationRect = new Rect(300, 0, ContentW - 300, ContentH),
        };

        using var sut = CreateSut();
        sut.SetScope(BlurRegionScope.Conversation);
        EnableAndDetect(sut);

        double scaleX = OverlayW / ContentW;
        double expectedX = 300.0 * scaleX;
        double expectedW = (ContentW - 300) * scaleX;

        _overlayWindow.Received().SetClip(Arg.Is<IReadOnlyList<Rect>>(rects =>
            rects.Count == 1
            && Math.Abs(rects[0].X - expectedX) < 0.5
            && Math.Abs(rects[0].Width - expectedW) < 0.5));
    }

    // ── Empty intersection (scope rect outside visible region) ────────────────

    [Fact]
    public void Scope_ChatList_ScopeOutsideVisibleRegion_PassesEmptyListToSetClip()
    {
        // Visible region covers only the RIGHT half of the overlay.
        var rightHalf = new[] { new Rect(OverlayW / 2, 0, OverlayW / 2, OverlayH) };
        SetupWindowFound(rightHalf);
        SetupReadback();

        // Chat list is in the leftmost 200 px of the capture frame (clearly outside the right-half visible region).
        _detector.Result = new()
        {
            Succeeded = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 200, ContentH),
            ConversationRect = new Rect(200, 0, ContentW - 200, ContentH),
        };

        using var sut = CreateSut();
        sut.SetScope(BlurRegionScope.ChatList);
        EnableAndDetect(sut);

        // The visible right-half rect and left-half scope rect do not intersect.
        // SetClip must receive an empty list (full-overlay privacy fallback).
        _overlayWindow.Received().SetClip(Arg.Is<IReadOnlyList<Rect>>(r => r != null && r.Count == 0));
    }

    // ── Trigger wiring ────────────────────────────────────────────────────────

    [Fact]
    public void RunDetection_TriggersByItself_CallsSetClip()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };

        using var sut = CreateSut();
        sut.EnableBlur();
        _overlayWindow.ClearReceivedCalls();

        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeFrame());

        // First-frame detection fires; RecomputeAndApplyClip is called by RunDetection.
        _overlayWindow.Received().SetClip(Arg.Any<IReadOnlyList<Rect>?>());
    }

    [Fact]
    public void SetScope_WhenOverlayActive_CallsSetClip()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left,
            ChatListRect = new Rect(0, 0, 300, ContentH) };

        using var sut = CreateSut();
        EnableAndDetect(sut);
        _overlayWindow.ClearReceivedCalls();

        sut.SetScope(BlurRegionScope.ChatList);

        _overlayWindow.Received(1).SetClip(Arg.Any<IReadOnlyList<Rect>?>());
    }

    [Fact]
    public void VisibleRegionChange_CallsSetClip()
    {
        SetupWindowFound();
        SetupReadback();

        using var sut = CreateSut();
        EnableAndDetect(sut);
        _overlayWindow.ClearReceivedCalls();

        // Partially occlude the overlay — visible region shrinks.
        var partialRegion = new[] { new Rect(0, 0, 400, 600) };
        _windowTracker.VisibleRegion.Returns(partialRegion);
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(this, partialRegion);

        _overlayWindow.Received().SetClip(Arg.Any<IReadOnlyList<Rect>?>());
    }

    [Fact]
    public void BoundsChanged_RunsDetectionThenCallsSetClip()
    {
        SetupWindowFound();
        SetupReadback();
        _detector.Result = new() { Succeeded = true, DetectedRailSide = RailSide.Left };

        using var sut = CreateSut();
        EnableAndDetect(sut);
        int setClipCallsBefore = _overlayWindow.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IOverlayWindow.SetClip));

        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, OverlayBounds);

        int setClipCallsAfter = _overlayWindow.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IOverlayWindow.SetClip));
        Assert.True(setClipCallsAfter > setClipCallsBefore,
            "BoundsChanged should trigger at least one additional SetClip call");
    }
}
