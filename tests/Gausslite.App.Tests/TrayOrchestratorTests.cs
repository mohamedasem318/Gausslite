using NSubstitute;
using System.Windows;
using Gausslite.App.Hotkey;
using Gausslite.App.Orchestration;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;
using NSubstitute.ReceivedExtensions;

namespace Gausslite.App.Tests;

public sealed class TrayOrchestratorTests
{
    private readonly IWindowTracker _windowTracker = Substitute.For<IWindowTracker>();
    private readonly ICaptureEngine _captureEngine = Substitute.For<ICaptureEngine>();
    private readonly IBlurPipeline _blurPipeline = Substitute.For<IBlurPipeline>();
    private readonly IOverlayWindow _overlayWindow = Substitute.For<IOverlayWindow>();
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly ICaptureItemFactory _captureItemFactory = Substitute.For<ICaptureItemFactory>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly FixedResultDetector _regionDetector = new();

    // Avoids NSubstitute limitations with ReadOnlySpan<byte> parameters.
    private sealed class FixedResultDetector : IRegionDetector
    {
        public RegionDetectionResult Result { get; set; } =
            new RegionDetectionResult { Succeeded = true, DetectedRailSide = RailSide.Left };
        public int CallCount { get; private set; }
        public RegionDetectionResult Detect(ReadOnlySpan<byte> bgraPixels, int w, int h, int stride)
        {
            CallCount++;
            return Result;
        }
    }

    private TrayOrchestrator CreateSut() => new(
        _windowTracker,
        _captureEngine,
        _blurPipeline,
        _overlayWindow,
        _hotkeyService,
        _captureItemFactory,
        _profile,
        _regionDetector);

    private TrayOrchestrator CreateSutWithInlineDispatch() => new(
        _windowTracker,
        _captureEngine,
        _blurPipeline,
        _overlayWindow,
        _hotkeyService,
        _captureItemFactory,
        _profile,
        _regionDetector,
        (_, action, _) => action(),
        (_, action) => action());

    private static IReadOnlyList<Rect> FullRegion => new[] { new Rect(0, 0, 800, 600) };
    private static IReadOnlyList<Rect> EmptyRegion => Array.Empty<Rect>();

    // Configures the factory to report WhatsApp as found.
    // item is null! because GraphicsCaptureItem cannot be constructed in unit tests;
    // TrayOrchestrator passes it straight through to the mocked ICaptureEngine which
    // accepts any value (Arg.Any<GraphicsCaptureItem>() matches null).
    private void SetupWhatsAppFound()
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.VisibleRegion.Returns(FullRegion);
        _windowTracker.CurrentBounds.Returns(new Rect(0, 0, 800, 600));

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    private void SetupWhatsAppNotRunning()
    {
        _windowTracker.IsWindowPresent.Returns(false);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.VisibleRegion.Returns((IReadOnlyList<Rect>?)null);
        _windowTracker.CurrentBounds.Returns((Rect?)null);

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return false; });
    }

    private void SetupWhatsAppMinimized()
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(true);
        _windowTracker.VisibleRegion.Returns(EmptyRegion);
        _windowTracker.CurrentBounds.Returns((Rect?)null);

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    [Fact]
    public void Enable_StartsTrackerThenCreatesOffscreenOverlayThenCapture()
    {
        SetupWhatsAppFound();
        using var sut = CreateSutWithInlineDispatch();

        sut.EnableBlur();

        _windowTracker.Received(1).Start();
        _overlayWindow.Received(1).ShowPlaceholder();
        _overlayWindow.Received(1).ShowOffscreen(new Rect(0, 0, 800, 600));
        _captureEngine.Received(1).Start(Arg.Any<GraphicsCaptureItem>());
        _overlayWindow.Received(1).MoveToBounds(new Rect(0, 0, 800, 600));
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);
    }

    [Fact]
    public void Enable_SetsIsBlurEnabledTrue()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.EnableBlur();

        Assert.True(sut.IsBlurEnabled);
    }

    [Fact]
    public void Enable_FiresBlurStateChangedWithTrue()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        bool? received = null;
        sut.BlurStateChanged += (_, v) => received = v;

        sut.EnableBlur();

        Assert.True(received);
    }

    [Fact]
    public void Enable_WhenWhatsAppNotRunning_StartsTrackerButNotCapture()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.EnableBlur();

        _windowTracker.Received(1).Start();
        _captureEngine.DidNotReceive().Start(Arg.Any<GraphicsCaptureItem>());
    }

    [Fact]
    public void Disable_StopsCaptureHidesOverlayThenStopsTracker()
    {
        SetupWhatsAppFound();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        sut.DisableBlur();

        _captureEngine.Received(1).Stop();
        _overlayWindow.Received(1).MoveOffscreen();
        _windowTracker.Received(1).Stop();
    }

    [Fact]
    public void Disable_ClearsIsBlurEnabled()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();

        sut.DisableBlur();

        Assert.False(sut.IsBlurEnabled);
    }

    [Fact]
    public void Disable_FiresBlurStateChangedWithFalse()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();
        bool? received = null;
        sut.BlurStateChanged += (_, v) => received = v;

        sut.DisableBlur();

        Assert.False(received);
    }

    [Fact]
    public void Hotkey_TogglesFromDisabledToEnabled()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        Assert.False(sut.IsBlurEnabled);

        _hotkeyService.HotkeyPressed += Raise.Event();

        Assert.True(sut.IsBlurEnabled);
    }

    [Fact]
    public void Hotkey_TogglesFromEnabledToDisabled()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();

        _hotkeyService.HotkeyPressed += Raise.Event();

        Assert.False(sut.IsBlurEnabled);
    }

    [Fact]
    public void BoundsChanged_WhenApplicationCurrentIsNull_DoesNotTouchOverlay()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();
        var bounds = new Rect(10, 20, 300, 400);

        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, bounds);

        _overlayWindow.DidNotReceive().MoveToBounds(Arg.Any<Rect>());
    }

    [Fact]
    public void BoundsChanged_WhenApplicationCurrentIsNull_DoesNotStartDeferredCapture()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();

        // Now WhatsApp appears
        SetupWhatsAppFound();
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(0, 0, 800, 600));

        _captureEngine.DidNotReceive().Start(Arg.Any<GraphicsCaptureItem>());
    }

    [Fact]
    public void Enable_WhenWhatsAppMinimized_EagerCreatesOffscreenOverlayAndArms()
    {
        SetupWhatsAppMinimized();
        using var sut = CreateSutWithInlineDispatch();

        sut.EnableBlur();

        Assert.True(sut.IsBlurEnabled);
        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
        _windowTracker.Received(1).Start();
        GraphicsCaptureItem? ignored;
        _captureItemFactory.Received(1).TryCreateForProfile(out ignored);
        _captureEngine.Received(1).Start(Arg.Any<GraphicsCaptureItem>());
        _overlayWindow.Received(1).ShowOffscreen(Arg.Any<Rect>());
        _overlayWindow.DidNotReceive().MoveToBounds(Arg.Any<Rect>());
    }

    [Fact]
    public void ReMinimize_MovesOverlayOffscreenAndKeepsCaptureAlive()
    {
        SetupWhatsAppFound();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.ClearReceivedCalls();
        _overlayWindow.ClearReceivedCalls();

        _windowTracker.MinimizedChanged += Raise.Event<EventHandler<bool>>(this, true);

        _overlayWindow.Received(1).MoveOffscreen();
        _captureEngine.DidNotReceive().Stop();
        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    [Fact]
    public void WindowClosesDuringActive_TearsDownCaptureAndReturnsToArmed()
    {
        SetupWhatsAppFound();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.ClearReceivedCalls();
        _overlayWindow.ClearReceivedCalls();

        _windowTracker.IsWindowPresent.Returns(false);
        _windowTracker.WindowPresenceChanged += Raise.Event<EventHandler<bool>>(this, false);

        _captureEngine.Received(1).Stop();
        _overlayWindow.Received(1).MoveOffscreen();
        _overlayWindow.Received(1).Destroy();
        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    [Fact]
    public void WindowReopensAfterClose_RunsEagerSetupAgainAndStaysArmedWhenMinimized()
    {
        SetupWhatsAppFound();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        _windowTracker.IsWindowPresent.Returns(false);
        _windowTracker.WindowPresenceChanged += Raise.Event<EventHandler<bool>>(this, false);
        _captureItemFactory.ClearReceivedCalls();
        _captureEngine.ClearReceivedCalls();
        _overlayWindow.ClearReceivedCalls();

        SetupWhatsAppMinimized();
        _windowTracker.WindowPresenceChanged += Raise.Event<EventHandler<bool>>(this, true);

        GraphicsCaptureItem? ignored;
        _captureItemFactory.Received(1).TryCreateForProfile(out ignored);
        _captureEngine.Received(1).Start(Arg.Any<GraphicsCaptureItem>());
        _overlayWindow.Received(1).ShowOffscreen(Arg.Any<Rect>());
        _overlayWindow.DidNotReceive().MoveToBounds(Arg.Any<Rect>());
        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    [Fact]
    public void Disable_FromActive_StopsCaptureHidesOverlayAndReturnsIdle()
    {
        SetupWhatsAppFound();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.ClearReceivedCalls();
        _overlayWindow.ClearReceivedCalls();

        sut.DisableBlur();

        _captureEngine.Received(1).Stop();
        _overlayWindow.Received(1).MoveOffscreen();
        _overlayWindow.Received(1).Destroy();
        Assert.Equal(BlurActivationState.Idle, sut.ActivationState);
    }

    [Fact]
    public void BoundsChanged_WhenApplicationCurrentIsNull_DoesNotReapplyBoundsAfterShow()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();
        var bounds = new Rect(0, 0, 800, 600);

        // Now WhatsApp appears
        SetupWhatsAppFound();
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, bounds);

        _overlayWindow.DidNotReceive().MoveToBounds(bounds);
        _overlayWindow.DidNotReceive().ShowOffscreen(Arg.Any<Rect>());
    }

    [Fact]
    public void MinimizedChanged_WhenApplicationCurrentIsNull_DoesNotHideOverlay()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();
        _overlayWindow.ClearReceivedCalls();

        _windowTracker.MinimizedChanged += Raise.Event<EventHandler<bool>>(this, true);

        _overlayWindow.DidNotReceive().MoveOffscreen();
    }

    [Fact]
    public void MinimizedChanged_WhenApplicationCurrentIsNull_DoesNotShowOverlay()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();
        _overlayWindow.ClearReceivedCalls();

        _windowTracker.MinimizedChanged += Raise.Event<EventHandler<bool>>(this, false);

        _overlayWindow.DidNotReceive().MoveToBounds(Arg.Any<Rect>());
        _overlayWindow.DidNotReceive().ShowOffscreen(Arg.Any<Rect>());
    }

    [Fact]
    public void MinimizedChangedFalse_WhenArmed_MovesOverlayToBoundsWithoutNewCaptureItem()
    {
        SetupWhatsAppMinimized();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureItemFactory.ClearReceivedCalls();
        _captureEngine.ClearReceivedCalls();
        _overlayWindow.ClearReceivedCalls();

        SetupWhatsAppFound();
        _windowTracker.VisibleRegion.Returns(FullRegion);

        _windowTracker.MinimizedChanged += Raise.Event<EventHandler<bool>>(this, false);

        GraphicsCaptureItem? ignored;
        _captureItemFactory.DidNotReceive().TryCreateForProfile(out ignored);
        _captureEngine.DidNotReceive().Start(Arg.Any<GraphicsCaptureItem>());
        _overlayWindow.Received(1).MoveToBounds(new Rect(0, 0, 800, 600));
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);
    }

    [Fact]
    public void DoubleEnable_IsNoOp()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.EnableBlur();
        sut.EnableBlur();

        _windowTracker.Received(1).Start();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        SetupWhatsAppNotRunning();
        var sut = CreateSut();
        sut.EnableBlur();

        sut.Dispose();
        var ex = Record.Exception(() => sut.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_WhenEnabled_StopsEverything()
    {
        SetupWhatsAppFound();
        var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        sut.Dispose();

        _captureEngine.Received().Stop();
        _windowTracker.Received().Stop();
    }

    [Fact]
    public void Dispose_DisposesBlurPipelineAndOverlay()
    {
        SetupWhatsAppNotRunning();
        var sut = CreateSut();

        sut.Dispose();

        _blurPipeline.Received(1).Dispose();
        _overlayWindow.Received(1).Dispose();
    }

    [Fact]
    public void SetIntensity_Default_IsMedium()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        Assert.Equal(BlurIntensityPreset.Medium, sut.CurrentIntensity);
    }

    [Fact]
    public void SetIntensity_Light_SetsBlurRadiusAndUpdatesCurrentIntensity()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.SetIntensity(BlurIntensityPreset.Light);

        _blurPipeline.Received(1).BlurRadius = BlurIntensityPresets.LightRadius;
        Assert.Equal(BlurIntensityPreset.Light, sut.CurrentIntensity);
    }

    [Fact]
    public void SetIntensity_Medium_SetsBlurRadiusAndUpdatesCurrentIntensity()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.SetIntensity(BlurIntensityPreset.Medium);

        _blurPipeline.Received(1).BlurRadius = BlurIntensityPresets.MediumRadius;
        Assert.Equal(BlurIntensityPreset.Medium, sut.CurrentIntensity);
    }

    [Fact]
    public void SetIntensity_Heavy_SetsBlurRadiusAndUpdatesCurrentIntensity()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.SetIntensity(BlurIntensityPreset.Heavy);

        _blurPipeline.Received(1).BlurRadius = BlurIntensityPresets.HeavyRadius;
        Assert.Equal(BlurIntensityPreset.Heavy, sut.CurrentIntensity);
    }

    [Fact]
    public void SetIntensity_CallsTryRenderCurrentFrame_AndPresentsWhenCachedFrameAvailable()
    {
        SetupWhatsAppNotRunning();
        var renderTarget = Substitute.For<IBlurRenderTarget>();
        _blurPipeline.TryRenderCurrentFrame().Returns(renderTarget);
        using var sut = CreateSut();

        sut.SetIntensity(BlurIntensityPreset.Light);

        _blurPipeline.Received(1).TryRenderCurrentFrame();
        _overlayWindow.Received(1).PresentFrame(renderTarget);
    }

    [Fact]
    public void SetIntensity_DoesNotPresentFrame_WhenNoCachedFrameAvailable()
    {
        SetupWhatsAppNotRunning();
        _blurPipeline.TryRenderCurrentFrame().Returns((IBlurRenderTarget?)null);
        using var sut = CreateSut();

        sut.SetIntensity(BlurIntensityPreset.Light);

        _blurPipeline.Received(1).TryRenderCurrentFrame();
        _overlayWindow.DidNotReceive().PresentFrame(Arg.Any<IBlurRenderTarget>());
    }

    [Fact]
    public void SetScope_Default_IsBoth()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        Assert.Equal(BlurRegionScope.Both, sut.CurrentScope);
    }

    [Fact]
    public void SetScope_ChatList_UpdatesCurrentScope()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.SetScope(BlurRegionScope.ChatList);

        Assert.Equal(BlurRegionScope.ChatList, sut.CurrentScope);
    }

    [Fact]
    public void SetScope_Conversation_UpdatesCurrentScope()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.SetScope(BlurRegionScope.Conversation);

        Assert.Equal(BlurRegionScope.Conversation, sut.CurrentScope);
    }

    [Fact]
    public void SetScope_Both_UpdatesCurrentScope()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();

        sut.SetScope(BlurRegionScope.Both);

        Assert.Equal(BlurRegionScope.Both, sut.CurrentScope);
    }

    // ── Region detection ─────────────────────────────────────────────────────

    private void SetupReadbackSuccess(int width = 800, int height = 600)
    {
        var fakePixels = new byte[width * height * 4];
        _blurPipeline
            .TryReadLatestFrameAsBgra(out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(x => { x[0] = fakePixels; x[1] = width; x[2] = height; x[3] = width * 4; return true; });
    }

    private void SetupReadbackFailure()
    {
        _blurPipeline
            .TryReadLatestFrameAsBgra(out Arg.Any<byte[]>(), out Arg.Any<int>(), out Arg.Any<int>(), out Arg.Any<int>())
            .Returns(false);
    }

    private ICaptureFrame MakeCaptureFrame(int width = 800, int height = 600)
    {
        var rt = Substitute.For<IBlurRenderTarget>();
        _blurPipeline.BlurFrame(Arg.Any<ICaptureFrame>()).Returns(rt);

        var frame = Substitute.For<ICaptureFrame>();
        frame.ContentSize.Returns(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        return frame;
    }

    [Fact]
    public void FirstFrame_RunsDetectionAndStoresResult()
    {
        // CreateSutWithInlineDispatch makes both background and UI dispatches synchronous,
        // so the detection dispatch from OnFrameArrived runs inline on the same thread.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        var expectedResult = new RegionDetectionResult { Succeeded = true, DetectedRailSide = RailSide.Left };
        _regionDetector.Result = expectedResult;

        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        Assert.NotNull(sut.LastDetectionResult);
        Assert.Equal(1, _regionDetector.CallCount);
        Assert.Equal(expectedResult.DetectedRailSide, sut.LastDetectionResult!.Value.DetectedRailSide);
    }

    [Fact]
    public void FirstFrame_DetectionOnlyRunsOnce_SecondFrameDoesNotRerunDetect()
    {
        SetupWhatsAppFound();
        SetupReadbackSuccess();

        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        Assert.Equal(1, _regionDetector.CallCount);
    }

    [Fact]
    public void BoundsChanged_RunsDetectionAgain()
    {
        SetupWhatsAppFound();
        SetupReadbackSuccess();

        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        // First-frame detection
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        int callsAfterFirstFrame = _regionDetector.CallCount;

        // BoundsChanged should trigger another detection
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(0, 0, 800, 600));

        Assert.Equal(callsAfterFirstFrame + 1, _regionDetector.CallCount);
    }

    [Fact]
    public void Detection_SkippedWhenReadbackFails_LastResultStaysNull()
    {
        SetupWhatsAppFound();
        SetupReadbackFailure();

        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        Assert.Null(sut.LastDetectionResult);
        Assert.Equal(0, _regionDetector.CallCount);
    }

    // ── Detection race fix: size-change BoundsChanged clears stale state ─────

    [Fact]
    public void BoundsChanged_SizeChange_ClearsDetectionResult()
    {
        // Arrange: run detection so _lastDetectionResult is populated.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        Assert.NotNull(sut.LastDetectionResult);

        // Make readback fail so the RunDetection inside OnBoundsChanged can't re-populate.
        SetupReadbackFailure();

        // Act: raise a BoundsChanged with a different (larger) size.
        var newBounds = new Rect(0, 0, 1920, 1032);
        _windowTracker.CurrentBounds.Returns(newBounds);
        _windowTracker.VisibleRegion.Returns(new[] { newBounds });
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, newBounds);

        // Assert: stale detection result was cleared and the failed RunDetection
        // did not re-set it.
        Assert.Null(sut.LastDetectionResult);
    }

    [Fact]
    public void BoundsChanged_PositionOnlyChange_DoesNotClearDetectionResult()
    {
        // Arrange: run detection so _lastDetectionResult is populated.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        Assert.NotNull(sut.LastDetectionResult);

        // Make readback fail so the RunDetection result would be null if called fresh.
        SetupReadbackFailure();

        // Act: raise a BoundsChanged with the same size but a different origin (drag).
        var movedBounds = new Rect(100, 100, 800, 600); // same 800×600, moved to 100,100
        _windowTracker.CurrentBounds.Returns(movedBounds);
        _windowTracker.VisibleRegion.Returns(new[] { movedBounds });
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, movedBounds);

        // Assert: detection result was NOT cleared — no stale-state flash during a drag.
        Assert.NotNull(sut.LastDetectionResult);
    }

    [Fact]
    public void BoundsChanged_SizeChange_FastPath_RecoversWhenReadbackMatchesNewBounds()
    {
        // Verifies that the OnBoundsChanged best-effort fast-path RunDetection succeeds
        // when the BlurPipeline cache already holds a frame whose dimensions match the new
        // bounds — this is the convergence path that's faster than the next cadence tick.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        int callsAfterFirstFrame = _regionDetector.CallCount;

        // Resize: new bounds AND a matching-size readback frame are both ready.
        var newBounds = new Rect(0, 0, 1920, 1032);
        _windowTracker.CurrentBounds.Returns(newBounds);
        _windowTracker.VisibleRegion.Returns(new[] { newBounds });
        SetupReadbackSuccess(1920, 1032);
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, newBounds);

        // Fast-path RunDetection inside OnBoundsChanged ran with a consistent readback.
        Assert.Equal(callsAfterFirstFrame + 1, _regionDetector.CallCount);
        Assert.NotNull(sut.LastDetectionResult);
    }

    // ── Fix 1: stale-frame readback validation ───────────────────────────────

    [Fact]
    public void RunDetection_StaleReadbackFrame_DoesNotUpdateCachedState()
    {
        // Stale-frame validation: bounds say 1920×1032 but readback returns the
        // pre-resize 800×600 frame.  IsReadbackFrameConsistentWithBounds rejects it,
        // detection is skipped, no cached state is written, no detector invocation.
        SetupWhatsAppFound();
        SetupReadbackSuccess(800, 600);
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        // Resize bounds to a much larger window — readback still returns the old 800×600
        // frame, simulating the BlurPipeline cache holding a pre-resize frame.
        var newBounds = new Rect(0, 0, 1920, 1032);
        _windowTracker.CurrentBounds.Returns(newBounds);
        _windowTracker.VisibleRegion.Returns(new[] { newBounds });
        _regionDetector.Result = new RegionDetectionResult { Succeeded = true, DetectedRailSide = RailSide.Left };
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, newBounds);

        // The stale 800×600 readback against 1920×1032 bounds yields scale ≈ 0.417 — far
        // outside the [≈0.97, ≈1.04] envelope.  Validation rejects, no detector call.
        Assert.Equal(0, _regionDetector.CallCount);
        Assert.Null(sut.LastDetectionResult);
    }

    [Fact]
    public void RunDetection_FreshReadbackFrame_UpdatesCachedState()
    {
        // Counter-test: when readback dimensions match bounds, detection runs.
        SetupWhatsAppFound();
        SetupReadbackSuccess(800, 600);
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        Assert.Equal(1, _regionDetector.CallCount);
        Assert.NotNull(sut.LastDetectionResult);
    }

    [Fact]
    public void RunDetection_MaximizedRatio_AcceptedAsConsistent()
    {
        // Maximized envelope: bounds and content size match exactly (ratio = 1.000).
        SetupWhatsAppFound();
        _windowTracker.CurrentBounds.Returns(new Rect(0, 0, 1920, 1040));
        _windowTracker.VisibleRegion.Returns(new[] { new Rect(0, 0, 1920, 1040) });
        SetupReadbackSuccess(1920, 1040);
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame(1920, 1040));

        Assert.Equal(1, _regionDetector.CallCount);
        Assert.NotNull(sut.LastDetectionResult);
    }

    // ── Fix 2: continuous (cadence-driven) detection ─────────────────────────

    [Fact]
    public void Detection_RunsOnFirstFrameAndAtCadence()
    {
        // First frame triggers detection (count==1). Subsequent frames don't, until a
        // multiple of DetectionCadenceFrames (30) is hit — at frame 30 detection runs again.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();

        // Fire 30 frames.
        for (int i = 0; i < 30; i++)
            _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        // Detection ran on count==1 and on count==30 → 2 invocations.
        Assert.Equal(2, _regionDetector.CallCount);
    }

    // ── Self-validating clip recompute (auto-clear stale state) ──────────────

    [Fact]
    public void RecomputeAndApplyClip_StaleContentCache_AutoClears()
    {
        // Reproduces the OnVisibleRegionChanged → OnBoundsChanged race seen in the smoke
        // log: bounds get updated to the new (maximized) size by OnVisibleRegionChanged
        // before OnBoundsChanged sees the size change.  When RecomputeAndApplyClip runs
        // with cached content size from BEFORE the resize, the bounds-to-content ratio
        // (e.g. 1.457 for 1920/1318) falls outside the envelope; the helper must clear
        // _lastContentWidth/Height/_lastDetectionResult and fall back to full coverage.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        _regionDetector.Result = new RegionDetectionResult
        {
            Succeeded        = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 300, 600),
            ConversationRect = new Rect(300, 0, 500, 600),
        };

        using var sut = CreateSutWithInlineDispatch();
        sut.SetScope(BlurRegionScope.ChatList);
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        Assert.NotNull(sut.LastDetectionResult);
        _overlayWindow.ClearReceivedCalls();

        // Simulate the race: OnVisibleRegionChanged delivers a visibly larger region
        // (maximize) — its UI handler updates bounds via CacheCurrentOrDefaultBounds.
        // No BoundsChanged fires (or it fires AFTER and sees identical bounds).  Cached
        // content size is still 800×600 from pre-resize detection; new bounds are
        // 1920×1032 → ratios 2.4 / 1.72, well outside envelope.
        var maxBounds = new Rect(0, 0, 1920, 1032);
        _windowTracker.CurrentBounds.Returns(maxBounds);
        _windowTracker.VisibleRegion.Returns(new[] { maxBounds });
        SetupReadbackFailure();
        _windowTracker.VisibleRegionChanged +=
            Raise.Event<EventHandler<IReadOnlyList<Rect>>>(this, (IReadOnlyList<Rect>)new[] { maxBounds });

        // The auto-clear must have cleared detection state (regardless of which event
        // fired) and fallen back to full coverage (SetClip(null) with full visibility).
        _overlayWindow.Received().SetClip(null);
        Assert.Null(sut.LastDetectionResult);
    }

    // ── Debounced delayed-retry ──────────────────────────────────────────────

    [Fact]
    public void BoundsChanged_SchedulesDelayedDetectionRetry_WhichRunsDetectionWhenItFires()
    {
        // Captures the action passed to the delayed-dispatch hook so the test can
        // simulate the timer firing.
        Action? capturedDelayed = null;
        TimeSpan? capturedDelay = null;
        TrayOrchestrator BuildSut() => new(
            _windowTracker, _captureEngine, _blurPipeline, _overlayWindow,
            _hotkeyService, _captureItemFactory, _profile, _regionDetector,
            (_, action, _) => action(),
            (_, action) => action(),
            (delay, action) => { capturedDelay = delay; capturedDelayed = action; return new NoopDisp(); });

        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = BuildSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());
        int callsBeforeBoundsChange = _regionDetector.CallCount;

        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(0, 0, 800, 600));

        // The schedule was made (with our 400 ms delay).
        Assert.NotNull(capturedDelayed);
        Assert.Equal(TimeSpan.FromMilliseconds(400), capturedDelay);

        // Fire the timer manually.  Detection runs again on whatever's in the cache.
        capturedDelayed!();
        Assert.True(_regionDetector.CallCount > callsBeforeBoundsChange + 1,
            "Delayed retry must invoke RunDetection one more time");
    }

    [Fact]
    public void BoundsChanged_RapidEvents_DebounceCancelsPriorDelayedRetry()
    {
        // A drag fires many BoundsChanged events.  Each must cancel the prior pending
        // retry; only the last one remains scheduled.
        var captured = new List<(Action action, FakeDisposable disp)>();
        TrayOrchestrator BuildSut() => new(
            _windowTracker, _captureEngine, _blurPipeline, _overlayWindow,
            _hotkeyService, _captureItemFactory, _profile, _regionDetector,
            (_, action, _) => action(),
            (_, action) => action(),
            (_, action) =>
            {
                var d = new FakeDisposable();
                captured.Add((action, d));
                return d;
            });

        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = BuildSut();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        // Three rapid BoundsChanged events (drag).
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(0, 0, 800, 600));
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(10, 10, 800, 600));
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(20, 20, 800, 600));

        // 3 schedules captured; the first two must have been disposed (cancelled).
        Assert.Equal(3, captured.Count);
        Assert.True(captured[0].disp.WasDisposed, "first retry should be cancelled by second BoundsChanged");
        Assert.True(captured[1].disp.WasDisposed, "second retry should be cancelled by third BoundsChanged");
        Assert.False(captured[2].disp.WasDisposed, "last retry should still be pending");
    }

    [Fact]
    public void BoundsChanged_RequestsRepaintOfTrackedWindow()
    {
        // After a bounds change, OnBoundsChanged must ask the window tracker to nudge
        // the captured window into repainting.  Without this, a window whose content
        // is static after the resize (e.g. snap resize where WhatsApp's WGC contentSize
        // doesn't update) never produces a fresh frame and detection stays stale until
        // the user hovers over the window.  The repaint must be requested every time —
        // not gated on OverlaySizeChanged — so racy paths (e.g. OnVisibleRegionChanged
        // landing first) still benefit.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _windowTracker.ClearReceivedCalls();

        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(0, 0, 1920, 1032));

        _windowTracker.Received(1).RequestRepaintOfTrackedWindow();
    }

    private sealed class NoopDisp : IDisposable { public void Dispose() { } }
    private sealed class FakeDisposable : IDisposable
    {
        public bool WasDisposed { get; private set; }
        public void Dispose() => WasDisposed = true;
    }

    [Fact]
    public void Detection_ReRunsContinuously_CatchesInternalLayoutShifts()
    {
        // Internal-divider scenario: WhatsApp's chat-list/conversation divider gets dragged
        // without the window resizing.  No BoundsChanged fires.  With cadence-driven
        // detection, the next cadence tick re-runs detection on a fresh frame and
        // _lastDetectionResult is overwritten with the new divider position.
        SetupWhatsAppFound();
        SetupReadbackSuccess();

        var rectsBeforeDrag = new RegionDetectionResult
        {
            Succeeded        = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 300, 600),
            ConversationRect = new Rect(300, 0, 500, 600),
        };
        var rectsAfterDrag = new RegionDetectionResult
        {
            Succeeded        = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 450, 600),
            ConversationRect = new Rect(450, 0, 350, 600),
        };

        _regionDetector.Result = rectsBeforeDrag;
        using var sut = CreateSutWithInlineDispatch();
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        Assert.Equal(rectsBeforeDrag.ChatListRect, sut.LastDetectionResult!.Value.ChatListRect);

        // User drags WhatsApp's internal divider — no BoundsChanged event.
        // Detector now reports the post-drag layout. Fire frames 2..30 to cross the
        // cadence boundary; the count==30 frame re-runs detection.
        _regionDetector.Result = rectsAfterDrag;
        for (int i = 0; i < 29; i++)
            _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        Assert.Equal(rectsAfterDrag.ChatListRect, sut.LastDetectionResult!.Value.ChatListRect);
    }

    [Fact]
    public void BoundsChanged_SizeChange_PrivacyInvariant_ScopedClipFallsBackToFullCoverage()
    {
        // When detection state is cleared on a size change, RecomputeAndApplyClip must
        // produce a full-coverage clip (SetClip(null)) regardless of the current scope —
        // the same behaviour as scope=Both or a detection failure. This prevents any
        // partial un-blur of the conversation pane during the detection-pending window.
        SetupWhatsAppFound();
        SetupReadbackSuccess();
        _regionDetector.Result = new RegionDetectionResult
        {
            Succeeded        = true,
            DetectedRailSide = RailSide.Left,
            ChatListRect     = new Rect(0, 0, 300, 600),
            ConversationRect = new Rect(300, 0, 500, 600),
        };

        using var sut = CreateSutWithInlineDispatch();
        sut.SetScope(BlurRegionScope.ChatList);
        sut.EnableBlur();
        _captureEngine.FrameArrived += Raise.Event<EventHandler<ICaptureFrame>>(this, MakeCaptureFrame());

        // Confirm that a scoped clip is active (non-null).
        _overlayWindow.Received().SetClip(Arg.Is<IReadOnlyList<Rect>>(r => r != null && r.Count > 0));
        _overlayWindow.ClearReceivedCalls();

        // Make readback fail so the BoundsChanged RunDetection doesn't re-set detection.
        SetupReadbackFailure();

        // Raise size-changing BoundsChanged — must fall back to full coverage.
        var fullBounds = new Rect(0, 0, 1920, 1032);
        _windowTracker.CurrentBounds.Returns(fullBounds);
        _windowTracker.VisibleRegion.Returns(new[] { fullBounds });
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, fullBounds);

        // Full-coverage means SetClip(null) (visible region matches full overlay bounds).
        _overlayWindow.Received().SetClip(null);
        _overlayWindow.DidNotReceive().SetClip(Arg.Is<IReadOnlyList<Rect>>(r => r != null && r.Count > 0));
    }
}
