using NSubstitute;
using System.Windows;
using WAshed.App.Hotkey;
using WAshed.App.Orchestration;
using WAshed.Core.Blur;
using WAshed.Core.Capture;
using WAshed.Core.WindowTracking;
using WAshed.Overlay;
using Windows.Graphics.Capture;

namespace WAshed.App.Tests;

public sealed class TrayOrchestratorTests
{
    private readonly IWindowTracker _windowTracker = Substitute.For<IWindowTracker>();
    private readonly ICaptureEngine _captureEngine = Substitute.For<ICaptureEngine>();
    private readonly IBlurPipeline _blurPipeline = Substitute.For<IBlurPipeline>();
    private readonly IOverlayWindow _overlayWindow = Substitute.For<IOverlayWindow>();
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly ICaptureItemFactory _captureItemFactory = Substitute.For<ICaptureItemFactory>();

    private TrayOrchestrator CreateSut() => new(
        _windowTracker,
        _captureEngine,
        _blurPipeline,
        _overlayWindow,
        _hotkeyService,
        _captureItemFactory);

    // Configures the factory to report WhatsApp as found.
    // item is null! because GraphicsCaptureItem cannot be constructed in unit tests;
    // TrayOrchestrator passes it straight through to the mocked ICaptureEngine which
    // accepts any value (Arg.Any<GraphicsCaptureItem>() matches null).
    private void SetupWhatsAppFound()
    {
        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForWhatsApp(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    private void SetupWhatsAppNotRunning()
    {
        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForWhatsApp(out dummy)
            .Returns(x => { x[0] = null!; return false; });
    }

    [Fact]
    public void Enable_StartsTrackerThenCaptureThenShowsOverlay()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        sut.EnableBlur();

        Received.InOrder(() =>
        {
            _windowTracker.Start();
            _captureEngine.Start(Arg.Any<GraphicsCaptureItem>());
            _overlayWindow.Show();
        });
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
        using var sut = CreateSut();
        sut.EnableBlur();

        sut.DisableBlur();

        Received.InOrder(() =>
        {
            _captureEngine.Stop();
            _overlayWindow.Hide();
            _windowTracker.Stop();
        });
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
    public void BoundsChanged_PropagatesSetBoundsToOverlay()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();
        var bounds = new Rect(10, 20, 300, 400);

        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, bounds);

        _overlayWindow.Received().SetBounds(bounds);
    }

    [Fact]
    public void BoundsChanged_WhenWhatsAppLaterAppears_StartsCapture()
    {
        SetupWhatsAppNotRunning();
        using var sut = CreateSut();
        sut.EnableBlur();

        // Now WhatsApp appears
        SetupWhatsAppFound();
        _windowTracker.BoundsChanged += Raise.Event<EventHandler<Rect>>(this, new Rect(0, 0, 800, 600));

        _captureEngine.Received(1).Start(Arg.Any<GraphicsCaptureItem>());
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
        var sut = CreateSut();
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
