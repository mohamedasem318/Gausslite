using NSubstitute;
using System.Windows;
using Gausslite.App.Hotkey;
using Gausslite.App.Orchestration;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;

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

    private TrayOrchestrator CreateSut() => new(
        _windowTracker,
        _captureEngine,
        _blurPipeline,
        _overlayWindow,
        _hotkeyService,
        _captureItemFactory,
        _profile);

    private TrayOrchestrator CreateSutWithInlineDispatch() => new(
        _windowTracker,
        _captureEngine,
        _blurPipeline,
        _overlayWindow,
        _hotkeyService,
        _captureItemFactory,
        _profile,
        (_, action, _) => action(),
        (_, action) => action());

    // Configures the factory to report WhatsApp as found.
    // item is null! because GraphicsCaptureItem cannot be constructed in unit tests;
    // TrayOrchestrator passes it straight through to the mocked ICaptureEngine which
    // accepts any value (Arg.Any<GraphicsCaptureItem>() matches null).
    private void SetupWhatsAppFound()
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.IsOccluded.Returns(false);
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
        _windowTracker.IsOccluded.Returns(false);
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
        _windowTracker.IsOccluded.Returns(false);
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
        _windowTracker.IsOccluded.Returns(false);

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
}
