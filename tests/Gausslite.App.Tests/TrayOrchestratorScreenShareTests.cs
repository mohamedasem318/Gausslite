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

    // ── Visible region == empty during share ───────────────────────────────
    // (Was previously the v0.3.0 "override during share" test asserting the
    // overlay stays Active when the visible region drops to zero — i.e. that the
    // orchestrator pretends the empty-region report is wrong during share, and
    // keeps blurring the full window.)
    //
    // The Spotify smoke test exposed that this override is the wrong model: it
    // makes the orchestrator paint blur on top of the cover.  The correct fix
    // pushed down to ComputeVisibleRegion: WS_EX_TRANSPARENT covering windows
    // (Zoom's annotation layer, etc.) are now skipped at the source, so during
    // a real Zoom share the visible region stays full and the overlay covers
    // WhatsApp correctly — without needing an orchestrator-level override.
    // When the visible region IS empty, that's now an authoritative signal that
    // WhatsApp is genuinely covered → hide.

    [Fact]
    public void ShareActive_VisibleRegionDropsToZero_OverlayHides()
    {
        // Empty visible region during share now hides the overlay (no override).
        // Real Zoom shares no longer trip this because ComputeVisibleRegion skips
        // WS_EX_TRANSPARENT windows at the source.  When this event DOES fire,
        // it's because WhatsApp is genuinely covered (Spotify, Edge, etc.) and
        // the overlay must move offscreen to avoid leaking blur on top of the
        // covering app.
        SetupWhatsAppFound();
        using var sut = CreateSut();

        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);

        _windowTracker.VisibleRegion.Returns(Array.Empty<Rect>());
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)Array.Empty<Rect>());

        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
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

    [Fact]
    public void ShareActive_VisibleRegionDropsToZero_LikelyFullyHidden_OverlayHides()
    {
        // v0.3.5 fix: even during an active share, if WhatsApp is genuinely hidden
        // (Edge fullscreen, virtual desktop switch — IsLikelyFullyHidden=true),
        // the overlay must move offscreen.  Without this fix, the always-on-top
        // overlay would paint blurred content on top of the unrelated foreground
        // app and leak it into the shared stream.
        SetupWhatsAppFound();
        using var sut = CreateSut();
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);

        _windowTracker.VisibleRegion.Returns(Array.Empty<Rect>());
        _windowTracker.IsLikelyFullyHidden.Returns(true);
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)Array.Empty<Rect>());

        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    [Fact]
    public void ShareActive_NonEmptyRegionButLikelyFullyHidden_OverlayHides()
    {
        // The Spotify case: the Z-order walk in ComputeVisibleRegion silently fails
        // to subtract Spotify's bounds (Chromium-based apps with non-standard
        // rendering can confuse GetWindowRect-based subtraction), so the rect list
        // stays "fully visible" while WhatsApp is visually behind Spotify.
        // WindowFromPoint correctly detects the cover and IsLikelyFullyHidden flips
        // true.  The orchestrator must hide on this signal alone, not wait for the
        // (broken) rect-based path to also report empty.
        SetupWhatsAppFound();
        using var sut = CreateSut();
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);

        var fakeFullRegion = new[] { new Rect(0, 0, 100, 100) };
        _windowTracker.VisibleRegion.Returns(fakeFullRegion);
        _windowTracker.IsLikelyFullyHidden.Returns(true);
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)fakeFullRegion);

        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    [Fact]
    public void NoShare_NonEmptyRegionButLikelyFullyHidden_OverlayHides()
    {
        // Same Spotify scenario, but in manual blur (no share active).  The hide
        // must still fire — the user explicitly asked that this work in normal
        // manual-blur cases, not just under share.
        SetupWhatsAppFound();
        using var sut = CreateSut();
        sut.EnableBlur();
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);

        var fakeFullRegion = new[] { new Rect(0, 0, 100, 100) };
        _windowTracker.VisibleRegion.Returns(fakeFullRegion);
        _windowTracker.IsLikelyFullyHidden.Returns(true);
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)fakeFullRegion);

        Assert.Equal(BlurActivationState.Armed, sut.ActivationState);
    }

    [Fact]
    public void ShareActive_PartialOcclusion_AppliesClipToVisibleRegion_NotFullBounds()
    {
        // The Spotify-during-share case the user keeps hitting.  Tracker reports
        // partial visible region (Spotify covers part of WhatsApp); WindowFromPoint
        // still resolves at uncovered sample points so IsLikelyFullyHidden=false.
        // The overlay MUST be clipped to the visible-region rects, not the full
        // WhatsApp bounds — otherwise the always-on-top overlay paints blurred
        // WhatsApp content on top of Spotify.
        //
        // The previous bug: the v0.3.0 share-active override in EffectiveVisibleRegion
        // returned full bounds for ANY share-active state, ignoring the tracker's
        // partial-occlusion report.  The new override fires only for the empty-region
        // false-positive case.
        SetupWhatsAppFound();
        using var sut = CreateSut();
        _shareDetector.Fire(ScreenShareState.Active, ZoomEvidence());
        Assert.Equal(BlurActivationState.Active, sut.ActivationState);
        _overlayWindow.ClearReceivedCalls();

        // Spotify covers the right half — tracker reports left half visible.
        var leftHalf = new[] { new Rect(0, 0, 400, 600) };
        _windowTracker.VisibleRegion.Returns(leftHalf);
        _windowTracker.IsLikelyFullyHidden.Returns(false);
        _windowTracker.VisibleRegionChanged += Raise.Event<EventHandler<IReadOnlyList<Rect>>>(
            this, (IReadOnlyList<Rect>)leftHalf);

        // The clip MUST be the partial visible rect, NOT cleared (which would mean
        // "paint everywhere = leak on top of Spotify").
        _overlayWindow.Received().SetClip(Arg.Is<IReadOnlyList<Rect>>(rects =>
            rects != null
            && rects.Count == 1
            && rects[0].Width == 400
            && rects[0].Height == 600));
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
