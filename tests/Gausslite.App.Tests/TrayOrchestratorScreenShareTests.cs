using NSubstitute;
using System.Windows;
using Gausslite.App.Hotkey;
using Gausslite.App.Orchestration;
using Gausslite.App.Tray;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.ScreenShare;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;

namespace Gausslite.App.Tests;

/// <summary>
/// Tests for the v0.3.0 screen-share auto-toggle state machine in <see cref="TrayOrchestrator"/>.
/// Covers Idle↔Active transitions, manual override during share, and balloon firing.
/// </summary>
public sealed class TrayOrchestratorScreenShareTests
{
    private readonly IWindowTracker _windowTracker = Substitute.For<IWindowTracker>();
    private readonly ICaptureEngine _captureEngine = Substitute.For<ICaptureEngine>();
    private readonly IBlurPipeline _blurPipeline = Substitute.For<IBlurPipeline>();
    private readonly IOverlayWindow _overlayWindow = Substitute.For<IOverlayWindow>();
    private readonly IHotkeyService _hotkeyService = Substitute.For<IHotkeyService>();
    private readonly ICaptureItemFactory _captureItemFactory = Substitute.For<ICaptureItemFactory>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly ITrayNotifier _notifier = Substitute.For<ITrayNotifier>();
    private readonly FakeRegionDetector _regionDetector = new();
    private readonly FakeScreenShareDetector _shareDetector = new();

    private sealed class FakeRegionDetector : IRegionDetector
    {
        public RegionDetectionResult Detect(ReadOnlySpan<byte> bgraPixels, int w, int h, int stride) =>
            new() { Succeeded = true, DetectedRailSide = RailSide.Left };
    }

    /// <summary>
    /// Test-controllable detector. <see cref="Fire"/> mimics a real Idle↔Active transition
    /// without driving a polling loop, so we can target each transition deterministically.
    /// </summary>
    private sealed class FakeScreenShareDetector : IScreenShareDetector
    {
        public event EventHandler<ScreenShareState>? StateChanged;
        public ScreenShareState CurrentState { get; private set; } = ScreenShareState.Idle;
        public ActiveShareEvidence? CurrentEvidence { get; private set; }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }

        public void Fire(ScreenShareState newState, ActiveShareEvidence? evidence = null)
        {
            CurrentState = newState;
            CurrentEvidence = evidence;
            StateChanged?.Invoke(this, newState);
        }
    }

    private void SetupWhatsAppFound()
    {
        _windowTracker.IsWindowPresent.Returns(true);
        _windowTracker.IsMinimized.Returns(false);
        _windowTracker.VisibleRegion.Returns(new[] { new Rect(0, 0, 800, 600) });
        _windowTracker.CurrentBounds.Returns(new Rect(0, 0, 800, 600));

        GraphicsCaptureItem? dummy = null;
        _captureItemFactory
            .TryCreateForProfile(out dummy)
            .Returns(x => { x[0] = null!; return true; });
    }

    private TrayOrchestrator CreateSut()
    {
        var sut = new TrayOrchestrator(
            _windowTracker, _captureEngine, _blurPipeline, _overlayWindow,
            _hotkeyService, _captureItemFactory, _profile, _regionDetector,
            (_, action, _) => action(),
            (_, action) => action());
        sut.SetTrayNotifier(_notifier);
        sut.SetScreenShareDetector(_shareDetector);
        return sut;
    }

    private static ActiveShareEvidence ZoomEvidence() =>
        new("Zoom", "Zoom", "ZPFloatToolbarClass", "Screen sharing meeting controls", new IntPtr(0xABCD));

    // ── Idle → Active ────────────────────────────────────────────────────────

    [Fact]
    public void ShareStarts_WhileBlurOff_AutoEnablesBlur()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        Assert.False(sut.IsBlurEnabled);

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());

        Assert.True(sut.IsBlurEnabled);
    }

    [Fact]
    public void ShareStarts_WhileBlurOn_LeavesBlurOn()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();
        Assert.True(sut.IsBlurEnabled);

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());

        Assert.True(sut.IsBlurEnabled);
        // No balloon: Active didn't auto-enable, so user disabling later is "user choice", not "override"
        _notifier.DidNotReceive().ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Info);
    }

    // ── Active → Idle ────────────────────────────────────────────────────────

    [Fact]
    public void ShareEnds_AfterAutoEnable_RestoresBlurOff()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.True(sut.IsBlurEnabled);

        _shareDetector.Fire(ScreenShareState.Idle);

        Assert.False(sut.IsBlurEnabled);
    }

    [Fact]
    public void ShareEnds_AfterAutoEnableThenUserOverride_LeavesBlurOff_NoFurtherAction()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.True(sut.IsBlurEnabled);

        sut.DisableBlur(); // user manually disables during share
        Assert.False(sut.IsBlurEnabled);

        _shareDetector.Fire(ScreenShareState.Idle);

        Assert.False(sut.IsBlurEnabled);
    }

    [Fact]
    public void ShareEnds_WhenBlurWasOnBeforeShare_LeavesBlurOn()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur(); // user had blur on before share
        Assert.True(sut.IsBlurEnabled);

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        _shareDetector.Fire(ScreenShareState.Idle);

        // Auto path didn't enable, so end-of-share doesn't disable.
        Assert.True(sut.IsBlurEnabled);
    }

    // ── Override balloon semantics ───────────────────────────────────────────

    [Fact]
    public void UserDisablesDuringActiveShare_BalloonFiresOnce()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        sut.DisableBlur();

        _notifier.Received(1).ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Info);
    }

    [Fact]
    public void UserDisablesEnablesDisablesDuringActiveShare_BalloonFiresOnceTotal()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        sut.DisableBlur();   // first disable -> balloon
        sut.EnableBlur();    // user changes mind -> override flag cleared
        sut.DisableBlur();   // second disable -> NO balloon (already shown for this share)

        _notifier.Received(1).ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Info);
    }

    [Fact]
    public void UserDisableOutsideShare_NoBalloon()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();

        sut.DisableBlur(); // outside any share

        _notifier.DidNotReceive().ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Info);
    }

    [Fact]
    public void NewShare_AfterPreviousOverride_BalloonCanFireAgain()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        // Share #1: user overrides
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        sut.DisableBlur();
        _shareDetector.Fire(ScreenShareState.Idle);

        // Share #2: user overrides again — balloon should fire fresh
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        sut.DisableBlur();

        _notifier.Received(2).ShowBalloon(
            Arg.Any<string>(), Arg.Any<string>(), NotificationIcon.Info);
    }

    [Fact]
    public void UserReEnablesAfterOverride_BlurStaysOnAtShareEnd()
    {
        // Once the user has manually toggled blur during a share, we respect their
        // last manifest setting at share-end — auto-restore should NOT fire.
        // Rationale: the user's last explicit action ("blur on") wins over the
        // pre-share state ("blur off").  Next share starts fresh in auto mode.
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        sut.DisableBlur();   // override #1
        sut.EnableBlur();    // user changes mind: blur back on (override still set)

        _shareDetector.Fire(ScreenShareState.Idle);

        // Auto-restore is gated on !userOverrode — since user took control, blur stays.
        Assert.True(sut.IsBlurEnabled);
    }

    // ── Detector wire/unwire lifecycle ───────────────────────────────────────

    [Fact]
    public void SetScreenShareDetector_Null_StopsReceivingTransitions()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        sut.SetScreenShareDetector(null);
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());

        Assert.False(sut.IsBlurEnabled); // no auto-enable since detector was unwired
    }

    [Fact]
    public void SetScreenShareDetector_Replacement_OldDetectorIsUnhooked()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        var newDetector = new FakeScreenShareDetector();
        sut.SetScreenShareDetector(newDetector);

        // Firing on the OLD detector should now do nothing.
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.False(sut.IsBlurEnabled);

        // Firing on the NEW detector should drive auto-enable.
        newDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.True(sut.IsBlurEnabled);
    }

    // ── Occlusion override during active share ────────────────────────────────
    // v0.2.0 occlusion logic walks Z-order and subtracts each above-Z window's
    // bounds from the tracked window's rect.  During a screen share, sharing apps
    // (Zoom in particular) drop many small floating overlays on top, fragmenting
    // the visible region to zero even when the window is visually mostly visible.
    // v0.3.0 override: while a share is active, ignore the fully-occluded report
    // and keep the overlay shown over the full window.

    [Fact]
    public void ShareActive_VisibleRegionDropsToZero_OverlayStaysOn()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        // Auto-enable on share start — overlay reaches Active state with full visibility.
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);

        // Now Zoom drops overlays on top — the WindowTracker reports VisibleRegion = 0 rects.
        _windowTracker.VisibleRegion.Returns(Array.Empty<Rect>());
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)Array.Empty<Rect>());

        // Override kicks in — state stays Active, overlay was NOT moved offscreen by this event.
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);
    }

    [Fact]
    public void NoShare_VisibleRegionDropsToZero_OverlayHides()
    {
        // v0.2.0 occlusion behavior preserved when no share is active.
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);

        _windowTracker.VisibleRegion.Returns(Array.Empty<Rect>());
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)Array.Empty<Rect>());

        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    // ── Cold-start repaint nudge ─────────────────────────────────────────────
    // The auto-enable path calls RequestRepaintOfTrackedWindow so WGC has a fresh
    // paint to capture, instead of waiting for an idle WhatsApp to paint on its own.

    [Fact]
    public void ShareStarts_AutoEnable_RequestsRepaintOfTrackedWindow()
    {
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());

        _windowTracker.Received(1).RequestRepaintOfTrackedWindow();
    }

    [Fact]
    public void ShareStarts_BlurAlreadyOn_DoesNotRequestRepaint()
    {
        // When blur was already manually enabled, auto-enable is a no-op and there's
        // no need to nudge a repaint — the existing capture session is producing frames.
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();
        _windowTracker.ClearReceivedCalls();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());

        _windowTracker.DidNotReceive().RequestRepaintOfTrackedWindow();
    }
}
