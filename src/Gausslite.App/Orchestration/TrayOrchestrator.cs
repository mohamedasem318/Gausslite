// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Gausslite.App.Diagnostics;
using Gausslite.App.Hotkey;
using Gausslite.App.Tray;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.Diagnostics;
using Gausslite.Core.ScreenShare;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;

namespace Gausslite.App.Orchestration;

/// <summary>
/// Wires together <see cref="IWindowTracker"/>, <see cref="ICaptureEngine"/>,
/// <see cref="IBlurPipeline"/>, <see cref="IOverlayWindow"/>, and <see cref="IRegionDetector"/>.
/// All public methods must be called from the WPF UI thread.
/// </summary>
public sealed class TrayOrchestrator : ITrayOrchestrator
{
    internal delegate void UiThreadDispatch(string source, Action action, DispatcherPriority priority = DispatcherPriority.Normal);
    internal delegate void BackgroundDispatch(string source, Action action);
    internal delegate IDisposable DelayedUiDispatch(TimeSpan delay, Action action);

    private readonly IWindowTracker _windowTracker;
    private readonly ICaptureEngine _captureEngine;
    private readonly IBlurPipeline _blurPipeline;
    private readonly IOverlayWindow _overlayWindow;
    private readonly IHotkeyService _hotkeyService;
    private readonly ICaptureItemFactory _captureItemFactory;
    private readonly IAppProfile _profile;
    private readonly IRegionDetector _regionDetector;
    private ITrayNotifier? _trayNotifier;
    private IScreenShareDetector? _screenShareDetector;

    // ── Auto-blur state machine for screen-share detection (v0.3.0) ──
    // _shareIsActive mirrors the detector's last-emitted state. The other three flags
    // implement "auto-enable on share start, restore on share end, manual override sticks
    // for the rest of THIS share". All four are written and read on the UI thread only.
    private bool _shareIsActive;
    private bool _autoEnabledForCurrentShare;
    private bool _userOverrodeForCurrentShare;
    private bool _preShareBlurWasOn;
    // True while the auto-enable / auto-restore code is running.  Public EnableBlur /
    // DisableBlur check this so that auto-initiated calls don't get accounted as
    // "user took control during share" — only real user-driven toggles do.
    private bool _isAutoToggle;
    private readonly UiThreadDispatch _dispatchToUiThread;
    private readonly BackgroundDispatch _dispatchToBackground;
    private readonly DelayedUiDispatch _scheduleDelayedOnUi;
    private readonly BlurActivationStateMachine _activation = new();

    // Delay before the post-bounds-change retry runs.  WhatsApp's responsive layout
    // typically settles within ~250-300 ms after a resize/maximize; 400 ms is a safe
    // margin that still feels responsive.  Each new BoundsChanged restarts the timer
    // (debounce), so a continuous drag fires the retry once at drag end.
    private static readonly TimeSpan DelayedDetectionRetry = TimeSpan.FromMilliseconds(400);

    // The pending delayed retry, if any.  Disposed (cancelled) when a new BoundsChanged
    // arrives so a flurry of events during a drag results in a single retry at the end.
    private IDisposable? _pendingDelayedDetection;

    private bool _captureStarted;
    private bool _setupReady;
    private bool _setupInProgress;
    private bool _overlayVisible;
    private bool _disposed;
    private Rect? _lastKnownBounds;
    private int _setupGeneration;

    // Frame-arrival diagnostics — written from background thread, use Interlocked.
    private int _frameCount;
    private int _frameExceptionCount;
    private int _noOutputLogged;     // 0 = not yet logged, 1 = logged

    // Detection runs on the first frame of each capture session and then every
    // DetectionCadenceFrames frames thereafter (~1 s at 30 fps).  A one-shot trigger
    // (the previous _detectionDone gating) misses internal layout shifts inside
    // WhatsApp — dragging the chat-list/conversation divider produces no
    // BoundsChanged event, so the clip would freeze at the old divider forever.
    // Periodic re-detection catches these within ~1 s.
    private const int DetectionCadenceFrames = 30;

    // Stores the most recent detection result and the frame dimensions it was derived from.
    // Written and read on the UI thread only — plain assignment is safe.
    // Null / zero means detection has never run (or was cleared) in the current session.
    private RegionDetectionResult? _lastDetectionResult;
    private int _lastContentWidth;
    private int _lastContentHeight;
    internal RegionDetectionResult? LastDetectionResult => _lastDetectionResult;

    // Sticky rail-side lock. The detector's rail-side heuristic walks the outer edges
    // of the frame looking for a quiet (vertically uniform) zone, which works for the
    // steady-state WhatsApp UI but is sensitive to small per-frame perturbations:
    // pressing Shift+Alt to switch Windows input language, for instance, can change a
    // few pixels in the message-input area enough for the right-side edge walk to flip
    // its decision and start labelling the chat list as the conversation pane.
    // The actual WhatsApp UI direction (LTR vs RTL) only changes when the user changes
    // WhatsApp's UI language — extremely rare during a session — so we lock the rail
    // side on the first successful detection and override the detector on any
    // subsequent frame that disagrees, swapping its chatListRect and conversationRect
    // back into the locked orientation. The lock resets only when the capture session
    // tears down (TearDownCaptureAndOverlay) or the WhatsApp window's bounds change
    // (OnBoundsChanged size-change path) — both legitimate "layout might have changed"
    // signals where re-detection from scratch is correct. Internal getter exposed for
    // tests.
    private RailSide? _lockedRailSide;
    internal RailSide? LockedRailSide => _lockedRailSide;

    // Balloon-notification state — UI-thread only.
    private bool _hasEverDetected;
    private bool _detectionWasSucceeding;
    private DateTime? _lastFailureBalloonAt;
    private bool _scopeFallbackBalloonShown;

    public event EventHandler<bool>? BlurStateChanged;

    public bool IsBlurEnabled => _activation.State != BlurActivationState.Idle;
    public BlurIntensityPreset CurrentIntensity { get; private set; } = BlurIntensityPreset.Medium;
    public BlurRegionScope CurrentScope { get; private set; } = BlurRegionScope.Both;
    internal BlurActivationState ActivationState => _activation.State;

    public TrayOrchestrator(
        IWindowTracker windowTracker,
        ICaptureEngine captureEngine,
        IBlurPipeline blurPipeline,
        IOverlayWindow overlayWindow,
        IHotkeyService hotkeyService,
        ICaptureItemFactory captureItemFactory,
        IAppProfile profile,
        IRegionDetector regionDetector)
        : this(
            windowTracker,
            captureEngine,
            blurPipeline,
            overlayWindow,
            hotkeyService,
            captureItemFactory,
            profile,
            regionDetector,
            DispatchToWpfUiThread,
            DispatchToThreadPool,
            ScheduleDelayedOnWpfUiThread)
    {
    }

    // Wire the balloon notifier after construction. Called by App.xaml.cs once the
    // TrayNotifier is ready (before TrayIconHost.Initialize attaches the TaskbarIcon).
    internal void SetTrayNotifier(ITrayNotifier? notifier) => _trayNotifier = notifier;

    // Wire the screen-share detector after construction.  Optional dependency: when null,
    // the orchestrator behaves exactly as in v0.2.0 (no auto-toggling on share events).
    // Production wiring lives in App.xaml.cs; tests inject a fake to drive transitions.
    public void SetScreenShareDetector(IScreenShareDetector? detector)
    {
        if (_screenShareDetector is not null)
            _screenShareDetector.StateChanged -= OnScreenShareStateChanged;

        _screenShareDetector = detector;

        if (_screenShareDetector is not null)
            _screenShareDetector.StateChanged += OnScreenShareStateChanged;
    }

    internal TrayOrchestrator(
        IWindowTracker windowTracker,
        ICaptureEngine captureEngine,
        IBlurPipeline blurPipeline,
        IOverlayWindow overlayWindow,
        IHotkeyService hotkeyService,
        ICaptureItemFactory captureItemFactory,
        IAppProfile profile,
        IRegionDetector regionDetector,
        UiThreadDispatch dispatchToUiThread,
        BackgroundDispatch? dispatchToBackground = null,
        DelayedUiDispatch? scheduleDelayedOnUi = null)
    {
        _windowTracker = windowTracker;
        _captureEngine = captureEngine;
        _blurPipeline = blurPipeline;
        _overlayWindow = overlayWindow;
        _hotkeyService = hotkeyService;
        _captureItemFactory = captureItemFactory;
        _profile = profile;
        _regionDetector = regionDetector;
        _dispatchToUiThread = dispatchToUiThread;
        _dispatchToBackground = dispatchToBackground ?? DispatchToThreadPool;
        _scheduleDelayedOnUi = scheduleDelayedOnUi ?? NoopDelayedUiDispatch;

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    public void ToggleBlur()
    {
        if (IsBlurEnabled) DisableBlur();
        else EnableBlur();
    }

    private void OnScreenShareStateChanged(object? sender, ScreenShareState newState)
    {
        // Detector ticks may arrive on the scheduler thread; production scheduler is the
        // WPF UI dispatcher (one and the same), but be defensive in case a test scheduler
        // ticks elsewhere.  Marshalling onto the UI thread keeps all auto-blur state
        // mutations on a single thread, matching the rest of the orchestrator.
        _dispatchToUiThread("OnScreenShareStateChanged", () =>
        {
            if (newState == ScreenShareState.Active)
                HandleShareStarted();
            else
                HandleShareEnded();
        });
    }

    private void HandleShareStarted()
    {
        if (_shareIsActive) return; // already in active state
        _shareIsActive = true;
        _preShareBlurWasOn = IsBlurEnabled;

        if (!IsBlurEnabled)
        {
            StartupLog.Info("ScreenShare: active share started — auto-enabling blur");
            _isAutoToggle = true;
            try { EnableBlur(); }
            finally { _isAutoToggle = false; }
            _autoEnabledForCurrentShare = true;

            // Nudge the tracked window into repainting.  Cold-start of the WGC capture
            // session takes ~200-450 ms; during that window, WGC only delivers a frame
            // when WhatsApp actually paints.  If WhatsApp is idle (cursor not over it,
            // no animations), the user sees the opaque privacy placeholder until the
            // next natural paint — which feels like "blur didn't kick in until I moved
            // the mouse over WhatsApp".  Same pattern + same fix as v0.2.0's
            // OnBoundsChanged repaint nudge: invalidate WhatsApp's client area so a
            // fresh paint is queued, ready for WGC to capture as soon as the session
            // is set up.
            _windowTracker.RequestRepaintOfTrackedWindow();
        }
        else
        {
            StartupLog.Info("ScreenShare: active share started — blur already on, no auto action");
            _autoEnabledForCurrentShare = false;
        }
    }

    private void HandleShareEnded()
    {
        if (!_shareIsActive) return;

        // Snapshot flags before clearing.
        bool wasAutoEnabled = _autoEnabledForCurrentShare;
        bool userOverrode   = _userOverrodeForCurrentShare;
        _shareIsActive = false;
        _autoEnabledForCurrentShare = false;
        _userOverrodeForCurrentShare = false;

        if (wasAutoEnabled && !userOverrode && IsBlurEnabled)
        {
            StartupLog.Info("ScreenShare: active share ended — restoring pre-share blur state (off)");
            _isAutoToggle = true;
            try { DisableBlur(); }
            finally { _isAutoToggle = false; }
        }
        else
        {
            StartupLog.Info(
                $"ScreenShare: active share ended — leaving blur as-is (autoEnabled={wasAutoEnabled}, userOverrode={userOverrode}, blurOn={IsBlurEnabled})");
        }
    }

    public void SetIntensity(BlurIntensityPreset preset)
    {
        var radius = BlurIntensityPresets.ToRadius(preset);
        StartupLog.Info($"SetIntensity: preset={preset}, radius={radius:F1} DIPs");
        CurrentIntensity = preset;
        _blurPipeline.BlurRadius = radius;
        var reRendered = _blurPipeline.TryRenderCurrentFrame();
        if (reRendered is not null)
        {
            StartupLog.Info($"SetIntensity: presenting re-rendered frame ({reRendered.Width}x{reRendered.Height})");
            _overlayWindow.PresentFrame(reRendered);
        }
        else
        {
            StartupLog.Info("SetIntensity: no cached frame yet; new radius will apply on next WGC frame");
        }
    }

    public void SetScope(BlurRegionScope scope)
    {
        StartupLog.Info($"SetScope: scope={scope}");
        CurrentScope = scope;

        // One-shot info balloon when the user selects a non-Both scope before we've ever
        // detected chat regions. Tells the user the selection will apply once detection succeeds.
        if (scope != BlurRegionScope.Both && !_hasEverDetected && !_scopeFallbackBalloonShown)
        {
            _trayNotifier?.ShowBalloon(
                "Gausslite is still finding your chats",
                "Your scope choice will kick in once we spot the chat list. Whole window stays blurred until then.",
                NotificationIcon.Info);
            _scopeFallbackBalloonShown = true;
        }

        RecomputeAndApplyClip("SetScope");
    }

    public void EnableBlur()
    {
        if (IsBlurEnabled) return;

        // User-initiated EnableBlur during an active share counts as "user took manual
        // control" — the auto-restore on share-end will then leave blur as the user set
        // it.  The auto-enable path also calls into this method but sets _isAutoToggle
        // first so this side-effect is suppressed for the system's own toggles.
        if (_shareIsActive && !_isAutoToggle)
            _userOverrodeForCurrentShare = true;

        StartupLog.Info("EnableBlur: entry");

        StartupLog.Info("EnableBlur: starting WindowTracker...");
        _windowTracker.BoundsChanged += OnBoundsChanged;
        _windowTracker.MinimizedChanged += OnMinimizedChanged;
        _windowTracker.VisibleRegionChanged += OnVisibleRegionChanged;
        _windowTracker.WindowPresenceChanged += OnWindowPresenceChanged;
        _windowTracker.Start();
        StartupLog.Info("EnableBlur: WindowTracker started");

        TransitionToArmed("EnableBlur");
        if (_windowTracker.CurrentBounds.HasValue)
            _lastKnownBounds = _windowTracker.CurrentBounds.Value;

        if (_windowTracker.IsWindowPresent)
            BeginEagerSetup("EnableBlur");
        else
            StartupLog.Info($"EnableBlur: armed - waiting for {_profile.Name} HWND before eager capture setup");

        BlurStateChanged?.Invoke(this, true);
        StartupLog.Info($"EnableBlur: complete, state={_activation.State}");
    }

    public void DisableBlur()
    {
        StartupLog.Info("DisableBlur: entry");
        if (!IsBlurEnabled) return;

        // If a share is currently active AND we auto-enabled blur for this share AND the
        // user hasn't yet overridden, this disable is the user's first manual override.
        // Record it and show a one-time friendly balloon explaining auto-blur will return.
        // The share-ended handler runs DisableBlur with _isAutoToggle = true and clears
        // _shareIsActive first, so the auto-restore path doesn't trigger this balloon.
        if (_shareIsActive && !_isAutoToggle && _autoEnabledForCurrentShare && !_userOverrodeForCurrentShare)
        {
            _userOverrodeForCurrentShare = true;
            StartupLog.Info("DisableBlur: user overriding auto-blur during active share");
            _trayNotifier?.ShowBalloon(
                "Blur is off for this share",
                "We'll turn it back on automatically the next time you share your screen.",
                NotificationIcon.Info);
        }

        TearDownCaptureAndOverlay("DisableBlur");
        TransitionToIdle("DisableBlur");
        _windowTracker.BoundsChanged -= OnBoundsChanged;
        _windowTracker.MinimizedChanged -= OnMinimizedChanged;
        _windowTracker.VisibleRegionChanged -= OnVisibleRegionChanged;
        _windowTracker.WindowPresenceChanged -= OnWindowPresenceChanged;
        _windowTracker.Stop();

        BlurStateChanged?.Invoke(this, false);
        StartupLog.Info($"DisableBlur: complete, state={_activation.State}");
    }

    private void BeginEagerSetup(string source)
    {
        if (!IsBlurEnabled || !_windowTracker.IsWindowPresent || _setupReady || _setupInProgress)
            return;

        _setupInProgress = true;
        var generation = _setupGeneration;
        var setupStartedAt = Stopwatch.GetTimestamp();
        StartupLog.Info($"{source}: eager setup begin (generation={generation})");

        _dispatchToBackground($"{source}.EagerSetup", () =>
        {
            GraphicsCaptureItem? item = null;
            var factoryStartedAt = Stopwatch.GetTimestamp();
            StartupLog.Info($"{source}: calling CaptureItemFactory.TryCreateForProfile on background thread");
            bool success;
            try
            {
                success = _captureItemFactory.TryCreateForProfile(out item);
            }
            catch (Exception ex)
            {
                StartupLog.Warn($"{source}: TryCreateForProfile failed during eager setup", ex);
                _dispatchToUiThread($"{source}.EagerSetupFactoryException", () =>
                {
                    if (generation == _setupGeneration)
                        _setupInProgress = false;

                    StartupLog.Info($"{source}: eager setup end after factory exception, elapsed={ElapsedMilliseconds(setupStartedAt):F3} ms");
                });
                return;
            }

            var factoryElapsed = ElapsedMilliseconds(factoryStartedAt);
            StartupLog.Info($"{source}: TryCreateForProfile returned success={success}, itemNull={item is null}, elapsed={factoryElapsed:F3} ms");

            if (!success)
            {
                _dispatchToUiThread($"{source}.EagerSetupFailed", () =>
                {
                    if (generation == _setupGeneration)
                        _setupInProgress = false;

                    StartupLog.Info($"{source}: eager setup end without capture item, elapsed={ElapsedMilliseconds(setupStartedAt):F3} ms");
                });
                return;
            }

            _dispatchToUiThread($"{source}.ShowOffscreenOverlay", () =>
            {
                if (!IsBlurEnabled || generation != _setupGeneration || !_windowTracker.IsWindowPresent)
                {
                    _setupInProgress = false;
                    StartupLog.Info($"{source}: eager setup abandoned before offscreen overlay creation");
                    return;
                }

                ShowOverlayOffscreenForSetup(source);
                _captureEngine.FrameArrived += OnFrameArrived;
                _captureStarted = true;

                _dispatchToBackground($"{source}.CaptureEngine.Start", () =>
                {
                    var captureStartedAt = Stopwatch.GetTimestamp();
                    StartupLog.Info($"{source}: calling CaptureEngine.Start on background thread");
                    try
                    {
                        _captureEngine.Start(item!);
                    }
                    catch (Exception ex)
                    {
                        StartupLog.Warn($"{source}: CaptureEngine.Start failed during eager setup", ex);
                        _dispatchToUiThread($"{source}.EagerSetupStartException", () =>
                        {
                            if (generation == _setupGeneration)
                            {
                                _setupInProgress = false;
                                _setupReady = false;
                                _captureStarted = false;
                                _captureEngine.FrameArrived -= OnFrameArrived;
                            }

                            StartupLog.Info($"{source}: eager setup end after CaptureEngine.Start exception, elapsed={ElapsedMilliseconds(setupStartedAt):F3} ms");
                        });
                        return;
                    }

                    StartupLog.Info($"{source}: CaptureEngine.Start returned, elapsed={ElapsedMilliseconds(captureStartedAt):F3} ms");

                    _dispatchToUiThread($"{source}.EagerSetupComplete", () =>
                    {
                        if (!IsBlurEnabled || generation != _setupGeneration || !_windowTracker.IsWindowPresent)
                        {
                            StartupLog.Info($"{source}: eager setup completed after teardown; ignoring completion");
                            return;
                        }

                        _setupReady = true;
                        _setupInProgress = false;
                        StartupLog.Info($"{source}: eager setup end, elapsed={ElapsedMilliseconds(setupStartedAt):F3} ms");
                        ApplyVisibilityForCurrentWindow($"{source}.EagerSetupComplete");
                    });
                });
            });
        });
    }

    private void ShowOverlayOffscreenForSetup(string source)
    {
        StartupLog.Info($"{source}: preparing OverlayWindow with opaque placeholder for offscreen eager setup");
        _overlayWindow.ShowPlaceholder();
        var bounds = CacheCurrentOrDefaultBounds(source);
        StartupLog.Info($"{source}: creating OverlayWindow HWND visible but parked offscreen");
        _overlayWindow.ShowOffscreen(bounds);
        _overlayVisible = false;
        _windowTracker.SetOverlayWindowHandle(_overlayWindow.WindowHandle);
        StartupLog.Info($"{source}: OverlayWindow.ShowOffscreen returned, HWND=0x{_overlayWindow.WindowHandle:X}");
    }

    private Rect CacheCurrentOrDefaultBounds(string source)
    {
        var bounds = _windowTracker.CurrentBounds ?? _lastKnownBounds ?? SystemParameters.WorkArea;
        _lastKnownBounds = bounds;
        StartupLog.Info($"{source}: current/default OverlayWindow bounds {bounds}");
        return bounds;
    }

    private void TearDownCaptureAndOverlay(string source)
    {
        _setupGeneration++;
        _setupInProgress = false;
        _setupReady = false;

        if (_captureStarted)
        {
            _captureStarted = false;
            _captureEngine.FrameArrived -= OnFrameArrived;
            StartupLog.Info($"{source}: stopping CaptureEngine");
            _captureEngine.Stop();
        }

        // Reset detection state and frame counter so the first frame of the next capture
        // session triggers detection (count == 1 satisfies the cadence rule below).
        Interlocked.Exchange(ref _frameCount, 0);
        Interlocked.Exchange(ref _noOutputLogged, 0);
        _lastDetectionResult = null;
        _lastContentWidth    = 0;
        _lastContentHeight   = 0;
        _lockedRailSide      = null;

        // Clear the BlurPipeline's cached frame so the next session's region detection
        // can't read stale pixels from this session. Without this, an OnBoundsChanged
        // that fires after EnableBlur but before the new capture session's first frame
        // would run RunDetection on the previous session's last frame — which mislabels
        // chat-list/conversation when WhatsApp restarted with a different UI direction
        // (e.g. user switched WhatsApp's UI language LTR ↔ RTL between sessions).
        _blurPipeline.ClearCachedFrame();

        // Cancel any pending delayed RunDetection — the new session will start its own.
        _pendingDelayedDetection?.Dispose();
        _pendingDelayedDetection = null;

        HideOverlay(source);
        _windowTracker.SetOverlayWindowHandle(IntPtr.Zero);
        StartupLog.Info($"{source}: destroying OverlayWindow HWND");
        _overlayWindow.Destroy();
    }

    private void HideOverlay(string source)
    {
        if (!_overlayVisible)
        {
            StartupLog.Info($"{source}: overlay already parked offscreen; skipping duplicate MoveOffscreen");
            return;
        }

        StartupLog.Info($"{source}: moving overlay offscreen");
        _overlayWindow.SetClip(null);
        _overlayWindow.MoveOffscreen();
        _overlayVisible = false;
        StartupLog.Info($"{source}: overlay moved offscreen");
    }

    private void ShowOverlay(string source, long? eventTimestamp = null)
    {
        if (!_setupReady)
        {
            StartupLog.Info($"{source}: cannot show overlay yet; eager setup ready={_setupReady}, inProgress={_setupInProgress}");
            BeginEagerSetup(source);
            return;
        }

        var bounds = CacheCurrentOrDefaultBounds(source);

        if (!_overlayVisible)
        {
            StartupLog.Info($"{source}: moving overlay on-screen to bounds {bounds}");
            _overlayWindow.MoveToBounds(bounds);
            _overlayVisible = true;
            TransitionToActive(source);
            var elapsedText = eventTimestamp.HasValue ? $", event-to-move={ElapsedMilliseconds(eventTimestamp.Value):F3} ms" : string.Empty;
            StartupLog.Info($"{source}: overlay move applied to bounds {bounds}{elapsedText}; expected privacy-critical event-to-move under 20 ms");
        }

        // Always (re-)apply the scope-aware clip after showing or while already visible.
        RecomputeAndApplyClip(source);
    }

    private void OnBoundsChanged(object? sender, Rect bounds)
    {
        StartupLog.Info($"OnBoundsChanged: received tracker bounds {bounds}; dispatching overlay work to UI thread");

        _dispatchToUiThread("OnBoundsChanged", () =>
        {
            if (!IsBlurEnabled)
                return;

            // Capture the previous bounds before overwriting so we can compare sizes.
            var previousBounds = _lastKnownBounds;
            _lastKnownBounds = bounds;

            if (OverlaySizeChanged(bounds, previousBounds))
            {
                // On a size change (maximize, restore, manual resize): immediately discard
                // any detection result that was computed from the previous frame dimensions.
                // Stale results would map old capture-pixel rects through the wrong
                // content-size ratio, placing the scope clip in the wrong position on the
                // resized overlay.  Clearing here forces RecomputeAndApplyClip (called
                // below via ApplyVisibilityForCurrentWindow) into the privacy-safe
                // full-coverage fallback while the fresh-frame detection is in flight.
                //
                // The cadence-driven detection in OnFrameArrived (every
                // DetectionCadenceFrames frames) is the reliable backstop: even without an
                // explicit re-arm, the next cadence tick re-runs detection on a
                // post-resize frame.  RunDetection("OnBoundsChanged") below is the
                // best-effort fast path that converges sooner when the cache already holds
                // a matching-size frame — Fix 1 (IsReadbackFrameConsistentWithBounds)
                // ensures stale frames don't get cached.
                _lastDetectionResult = null;
                _lastContentWidth    = 0;
                _lastContentHeight   = 0;
                _lockedRailSide      = null;
                StartupLog.Info($"OnBoundsChanged: size changed — cleared stale detection state");
            }

            if (_setupReady)
            {
                if (_activation.State == BlurActivationState.Active && OverlaySizeGrew(bounds, previousBounds))
                {
                    // Only show the placeholder when the overlay window actually grew
                    // (WhatsApp was resized) — NOT on pure position changes.  The old
                    // BoundsOutgrewLastBlurredFrame compared DIP overlay size against the
                    // WGC physical-pixel frame size; the 14 × 7 px WGC-content-area gap
                    // always made that condition true, causing a solid-color flash on
                    // every move.
                    StartupLog.Info($"OnBoundsChanged: overlay size grew from {previousBounds} to {bounds}; showing placeholder until resized frame arrives");
                    _overlayWindow.ShowPlaceholder();
                }

                if (_activation.State == BlurActivationState.Active)
                {
                    StartupLog.Info($"OnBoundsChanged: moving active overlay to bounds {bounds}");
                    _overlayWindow.MoveToBounds(bounds);
                    _overlayVisible = true;
                }
                else
                {
                    StartupLog.Info($"OnBoundsChanged: setup ready while armed; keeping overlay parked offscreen");
                }
            }

            if (_windowTracker.IsWindowPresent)
                BeginEagerSetup("OnBoundsChanged");

            ApplyVisibilityForCurrentWindow("OnBoundsChanged");

            // Best-effort fast path: re-run detection with whatever frame is cached now.
            // If the frame pool has already produced a post-resize frame this wins cleanly.
            // If not, the re-armed OnFrameArrived detection (above) is the reliable backstop.
            RunDetection("OnBoundsChanged");

            // Even when the fast-path RunDetection succeeds immediately, the first
            // post-resize WGC frame typically captures WhatsApp mid-layout-transition
            // (chat list at an intermediate width).  Schedule a debounced retry ~400 ms
            // later so detection re-runs on whatever frame is in the input cache once
            // WhatsApp's responsive layout has settled.
            ScheduleDelayedDetectionRetry("OnBoundsChanged");

            // Nudge the tracked window into repainting.  Some bounds changes (e.g. snap
            // resizes where WhatsApp's WGC contentSize stays at the pre-snap value)
            // produce no fresh WGC frame on their own — without this the user has to
            // hover the cursor over the window to provoke a paint.  The repaint usually
            // lands well within the 400 ms delayed-retry window above; the retry then
            // detects on the freshly-captured frame.
            _windowTracker.RequestRepaintOfTrackedWindow();
        });
    }

    // Cancels any pending delayed retry and schedules a new one.  Each new BoundsChanged
    // restarts the timer (debounce), so a flurry of events during a drag fires the
    // retry exactly once at drag end.
    private void ScheduleDelayedDetectionRetry(string trigger)
    {
        _pendingDelayedDetection?.Dispose();
        _pendingDelayedDetection = _scheduleDelayedOnUi(
            DelayedDetectionRetry,
            () =>
            {
                _pendingDelayedDetection = null;
                if (!IsBlurEnabled) return;
                RunDetection($"DelayedAfter{trigger}");
            });
    }

    private void OnMinimizedChanged(object? sender, bool isMinimized)
    {
        var priority = isMinimized ? DispatcherPriority.Normal : DispatcherPriority.Send;
        var eventReceivedAt = Stopwatch.GetTimestamp();
        DiagLog.Info($"OnMinimizedChanged: received minimized={isMinimized}; dispatching overlay work to UI thread at priority={priority}");

        _dispatchToUiThread("OnMinimizedChanged", () =>
        {
            DiagLog.Info($"OnMinimizedChanged: applying minimized={isMinimized} on UI thread");

            if (!IsBlurEnabled)
                return;

            if (isMinimized)
            {
                TransitionToArmed("OnMinimizedChanged");
                HideOverlay("OnMinimizedChanged");
                return;
            }

            if (!_windowTracker.IsWindowPresent)
            {
                TransitionToArmed("OnMinimizedChanged");
                return;
            }

            if (!HasVisibleRegion())
            {
                DiagLog.Info($"OnMinimizedChanged: restore arrived while {_profile.Name} is still occluded; keeping overlay hidden");
                TransitionToArmed("OnMinimizedChanged");
                return;
            }

            ShowOverlay("OnMinimizedChanged", eventReceivedAt);
        }, priority);
    }

    private void OnVisibleRegionChanged(object? sender, IReadOnlyList<System.Windows.Rect> region)
    {
        var fullyOccluded = region.Count == 0;
        var priority = fullyOccluded ? DispatcherPriority.Normal : DispatcherPriority.Send;
        var eventReceivedAt = Stopwatch.GetTimestamp();
        DiagLog.Info($"OnVisibleRegionChanged: region has {region.Count} rect(s); dispatching to UI thread at priority={priority}");

        _dispatchToUiThread("OnVisibleRegionChanged", () =>
        {
            DiagLog.Info($"OnVisibleRegionChanged: applying region ({region.Count} rect(s)) on UI thread, isLikelyFullyHidden={_windowTracker.IsLikelyFullyHidden}");

            if (!IsBlurEnabled)
                return;

            // Hide whenever WhatsApp has no visible pixels — share or no share.
            //   - Tracker reports empty region: no visible pixels (Spotify covers, etc.).
            //   - IsLikelyFullyHidden via WindowFromPoint: defensive backup for cases
            //     where the Z-order walk silently missed the cover.
            //   - Window absent or minimized: no pixels to blur.
            // Otherwise show the overlay; the clip composition path uses the visible
            // region's actual rects so the overlay only paints where WhatsApp is
            // visible (Zoom-share false-positive is no longer a problem because
            // ComputeVisibleRegion now skips WS_EX_TRANSPARENT windows the same way
            // WindowFromPoint does).
            if (fullyOccluded
                || _windowTracker.IsLikelyFullyHidden
                || !_windowTracker.IsWindowPresent
                || _windowTracker.IsMinimized)
            {
                TransitionToArmed("OnVisibleRegionChanged");
                HideOverlay("OnVisibleRegionChanged");
                return;
            }

            ShowOverlay("OnVisibleRegionChanged", eventReceivedAt);
        }, priority);
    }

    private void OnWindowPresenceChanged(object? sender, bool isPresent)
    {
        DiagLog.Info($"OnWindowPresenceChanged: received present={isPresent}; dispatching overlay work to UI thread");

        _dispatchToUiThread("OnWindowPresenceChanged", () =>
        {
            if (!IsBlurEnabled)
                return;

            if (!isPresent)
            {
                _lastKnownBounds = null;
                TearDownCaptureAndOverlay("OnWindowPresenceChanged");
                TransitionToArmed("OnWindowPresenceChanged");
                return;
            }

            if (_windowTracker.CurrentBounds.HasValue)
                _lastKnownBounds = _windowTracker.CurrentBounds.Value;

            BeginEagerSetup("OnWindowPresenceChanged");
        });
    }

    private void ApplyVisibilityForCurrentWindow(string source)
    {
        if (!IsBlurEnabled)
            return;

        if (!_windowTracker.IsWindowPresent || _windowTracker.IsMinimized || !HasVisibleRegion())
        {
            TransitionToArmed(source);
            HideOverlay(source);
            return;
        }

        ShowOverlay(source);
    }

    // True when WhatsApp has any visible pixels.  Single source of truth: the tracker's
    // visible region.  ComputeVisibleRegion now skips WS_EX_TRANSPARENT windows (matching
    // WindowFromPoint's own behaviour), so the region is accurate in both the Spotify
    // case (real opaque cover → empty region → hide) and the Zoom-share case (transparent
    // overlays don't subtract → region stays full → blur visible).  IsLikelyFullyHidden
    // is kept as a defensive backup in case some weird app slips past the Z-order walk
    // entirely.
    private bool HasVisibleRegion()
    {
        if (_windowTracker.IsLikelyFullyHidden) return false;
        return (_windowTracker.VisibleRegion?.Count ?? 0) > 0;
    }

    // The orchestrator's view of WhatsApp's visible region in screen-DIP coordinates.
    // Just the tracker's region — no overrides.  The Zoom-share false-positive that
    // used to motivate an override is now handled at the source: ComputeVisibleRegion
    // skips WS_EX_TRANSPARENT covering windows so they don't subtract from the visible
    // region, the same way Win32's WindowFromPoint skips them.  Result: the visible
    // region accurately reflects "where WhatsApp's pixels are visible to the user" in
    // both the Zoom-transparent-overlays case (region stays full → blur covers WhatsApp)
    // and the Spotify-opaque-cover case (region shrinks to the uncovered area or
    // becomes empty → blur clips or hides).
    private IReadOnlyList<Rect>? EffectiveVisibleRegion() => _windowTracker.VisibleRegion;

    // Computes the scope-aware clip and applies it to the overlay.
    // Reads: _lastKnownBounds, _windowTracker.VisibleRegion, _lastDetectionResult,
    //        _lastContentWidth/Height, CurrentScope.
    // Writes: overlay clip via SetClip. Also updates balloon notification state.
    // Must be called on the UI thread.
    private void RecomputeAndApplyClip(string source)
    {
        var bounds = _lastKnownBounds;
        if (bounds is null)
            return;

        // Self-validate against the cached content size.  Multiple paths update
        // _lastKnownBounds (OnBoundsChanged AND OnVisibleRegionChanged via
        // CacheCurrentOrDefaultBounds); when OnVisibleRegionChanged lands first on a
        // resize, OnBoundsChanged sees previousBounds == newBounds and never fires its
        // size-change clear.  This invariant catches that race centrally — wherever
        // bounds are updated, the next clip computation rejects an inconsistent cache.
        // Applies the same envelope as IsReadbackFrameConsistentWithBounds.
        if (_lastContentWidth > 0 && _lastContentHeight > 0)
        {
            double cachedScaleX = bounds.Value.Width  / _lastContentWidth;
            double cachedScaleY = bounds.Value.Height / _lastContentHeight;
            if (!IsScaleRatioConsistent(cachedScaleX, cachedScaleY))
            {
                StartupLog.Info(
                    $"{source}: clip-compose detected stale content cache " +
                    $"(bounds={bounds.Value} content={_lastContentWidth}x{_lastContentHeight} " +
                    $"scale={cachedScaleX:F3}x{cachedScaleY:F3}) — clearing detection state");
                _lastDetectionResult = null;
                _lastContentWidth    = 0;
                _lastContentHeight   = 0;
                _lockedRailSide      = null;
            }
        }

        var region = EffectiveVisibleRegion();
        if (region is null || region.Count == 0)
        {
            _overlayWindow.SetClip(null);
            return;
        }

        bool detectionSucceeded = _lastDetectionResult.HasValue && _lastDetectionResult.Value.Succeeded;
        UpdateBalloonState(detectionSucceeded);

        // Convert visible-region screen-DIP rects to overlay-local coordinates (origin at overlay top-left).
        var localRects = new List<Rect>(region.Count);
        foreach (var r in region)
            localRects.Add(new Rect(r.X - bounds.Value.X, r.Y - bounds.Value.Y, r.Width, r.Height));

        // Determine scope rect in overlay-local DIPs.
        // Null means no scope filtering (Both, or detection unavailable).
        Rect? scopeRect = null;
        Rect? captureRectInput = null;
        if (CurrentScope != BlurRegionScope.Both
            && detectionSucceeded
            && _lastContentWidth  > 0
            && _lastContentHeight > 0)
        {
            captureRectInput = CurrentScope == BlurRegionScope.ChatList
                ? _lastDetectionResult!.Value.ChatListRect
                : _lastDetectionResult!.Value.ConversationRect;

            scopeRect = CaptureToOverlayConverter.Convert(
                captureRectInput.Value,
                _lastContentWidth,
                _lastContentHeight,
                bounds.Value.Width,
                bounds.Value.Height);
        }

        // Diagnostic line: bounds, content size, scale ratios, and the input/output rect
        // pair the converter just operated on.  Pinned out as the single point that lets
        // smoke tests for Layouts B (left-edge resize) and C (maximize) verify the new
        // bounds and the cached content size are paired with the correct ratio.
        double diagScaleX = (_lastContentWidth  > 0) ? bounds.Value.Width  / _lastContentWidth  : 0;
        double diagScaleY = (_lastContentHeight > 0) ? bounds.Value.Height / _lastContentHeight : 0;
        StartupLog.Info(
            $"{source}: clip-compose bounds={bounds.Value} content={_lastContentWidth}x{_lastContentHeight} " +
            $"scale={diagScaleX:F3}x{diagScaleY:F3} captureRect={(captureRectInput?.ToString() ?? "(none)")} " +
            $"scopeRect={(scopeRect?.ToString() ?? "(none)")}");

        if (scopeRect is null)
        {
            // No scope filtering: optimise for the common full-coverage case.
            if (localRects.Count == 1
                && localRects[0].X == 0 && localRects[0].Y == 0
                && localRects[0].Width  == bounds.Value.Width
                && localRects[0].Height == bounds.Value.Height)
            {
                _overlayWindow.SetClip(null);
                StartupLog.Info($"{source}: full visibility, no scope — clip cleared");
                return;
            }

            _overlayWindow.SetClip(localRects);
            StartupLog.Info($"{source}: visibility clip ({localRects.Count} rect(s)), scope=Both or detection unavailable");
            return;
        }

        // Intersect each visible rect with the scope rect.
        // Filter degenerate zero-area intersections (WPF Rect.Intersect can return a
        // zero-width/height rect when edges exactly touch — that is not a visible region).
        var intersected = new List<Rect>();
        foreach (var r in localRects)
        {
            var inter = Rect.Intersect(r, scopeRect.Value);
            if (!inter.IsEmpty && inter.Width > 0 && inter.Height > 0)
                intersected.Add(inter);
        }

        if (intersected.Count == 0)
            StartupLog.Info($"{source}: scope rect fully outside visible region — clip empty (full-overlay privacy fallback)");
        else
            StartupLog.Info($"{source}: scope-filtered clip ({intersected.Count} rect(s)), scope={CurrentScope}");

        _overlayWindow.SetClip(intersected);
    }

    // Tracks detection success/failure transitions and fires balloon notifications.
    private void UpdateBalloonState(bool currentSucceeded)
    {
        if (currentSucceeded && !_hasEverDetected)
        {
            _hasEverDetected = true;
            _detectionWasSucceeding = true;
            return;
        }

        if (!currentSucceeded && _detectionWasSucceeding && _hasEverDetected)
        {
            _trayNotifier?.ShowBalloon(
                "Gausslite couldn't find your chats",
                "Blurring the whole WhatsApp window to keep you covered. This usually fixes itself — try resizing WhatsApp or scrolling the chat list.",
                NotificationIcon.Warning);
            _detectionWasSucceeding = false;
            _lastFailureBalloonAt = DateTime.UtcNow;
            return;
        }

        if (currentSucceeded && !_detectionWasSucceeding)
        {
            _hasEverDetected = true;
            if (_lastFailureBalloonAt is null ||
                (DateTime.UtcNow - _lastFailureBalloonAt.Value).TotalSeconds > 30)
            {
                _trayNotifier?.ShowBalloon(
                    "Gausslite is back on track",
                    "Scope-aware blur is working again.",
                    NotificationIcon.Info);
            }
            _detectionWasSucceeding = true;
        }
    }

    // Reads the latest GPU frame back to the CPU and runs the region detector.
    // Must be called on the UI thread — both trigger points (OnFrameArrived dispatch and
    // OnBoundsChanged body) ensure this.  _lastDetectionResult and _lastContent* are written
    // here and read only on the UI thread, so plain assignment is safe.
    private void RunDetection(string source)
    {
        if (!_blurPipeline.TryReadLatestFrameAsBgra(out var pixels, out var w, out var h, out var s))
        {
            StartupLog.Info($"{source}: detection skipped — no frame available for readback");
            return;
        }

        // Reject readback frames whose dimensions don't match the current bounds.
        // After a resize, the BlurPipeline cache still holds a pre-resize frame for one
        // tick before OnFrameArrived refills it. Detecting on that stale frame writes
        // chatList/conversation rects that, once paired with the new bounds in
        // RecomputeAndApplyClip, get stretched through the wrong scale ratio (e.g.
        // 1.596 instead of 1.000 on maximize), placing the clip far from the divider.
        // Skip and let the privacy-safe full-coverage fallback hold; the next frame from
        // OnFrameArrived will produce a matching-size readback.
        if (!IsReadbackFrameConsistentWithBounds(w, h, out var diagScaleX, out var diagScaleY))
        {
            StartupLog.Info(
                $"{source}: detection skipped — readback {w}x{h} inconsistent with bounds {_lastKnownBounds} " +
                $"(scaleX={diagScaleX:F3}, scaleY={diagScaleY:F3}; expected ≈1.000 or ≈1.012)");
            return;
        }

        // Cache the content size alongside the detection result; both are consumed by
        // RecomputeAndApplyClip for coordinate-space conversion.
        _lastContentWidth  = w;
        _lastContentHeight = h;

        var result = _regionDetector.Detect(pixels, w, h, s);

        // Sticky rail-side lock — see field comment for rationale. First successful
        // detection of the session locks the rail side; any subsequent detection that
        // disagrees has its chatList/conversation rects swapped back into the locked
        // orientation. Without this, a transient detector flip (e.g. on Shift+Alt
        // input-language switch) would silently mislabel the panes for the rest of
        // the session, and a "scope=Conversation" setting would clip against the
        // wrong half of the window.
        if (result.Succeeded)
        {
            if (_lockedRailSide is null)
            {
                _lockedRailSide = result.DetectedRailSide;
                StartupLog.Info($"{source}: rail-side locked to {_lockedRailSide} for this capture session");
            }
            else if (_lockedRailSide.Value != result.DetectedRailSide)
            {
                StartupLog.Info(
                    $"{source}: detector reported rail={result.DetectedRailSide} " +
                    $"but locked={_lockedRailSide}; swapping rects to keep locked orientation");
                result = new RegionDetectionResult
                {
                    Succeeded          = true,
                    ChatListRect       = result.ConversationRect,
                    ConversationRect   = result.ChatListRect,
                    FailureReason      = string.Empty,
                    DetectedRailSide   = _lockedRailSide.Value,
                    RailSideLeftWidth  = result.RailSideLeftWidth,
                    RailSideRightWidth = result.RailSideRightWidth,
                };
            }
        }

        _lastDetectionResult = result;

        if (result.Succeeded)
            StartupLog.Info(
                $"{source}: detection succeeded — rail={result.DetectedRailSide}, " +
                $"chatList={result.ChatListRect}, conversation={result.ConversationRect}");
        else
            StartupLog.Info($"{source}: detection failed — {result.FailureReason}");

        // Recompute clip using the fresh detection result.
        RecomputeAndApplyClip(source);
    }

    // Validates that the readback frame's dimensions are consistent with the current
    // _lastKnownBounds. WhatsApp's bounds-to-content ratio sits in a tight envelope:
    //   - Windowed: ≈ 1.012 horizontal, 1.009 vertical (the 14 × 7 px DWM gap).
    //   - Maximized: ≈ 1.000 (NormalizeWindowRect strips the gap).
    // Anything outside both envelopes (with ±0.03 tolerance) means the readback is from
    // BEFORE the latest size change. Returns false in that case; callers must NOT update
    // cached detection state.  When _lastKnownBounds is null (early startup), accepts the
    // readback unconditionally — there is no current bounds to disagree with.
    internal bool IsReadbackFrameConsistentWithBounds(int frameWidth, int frameHeight, out double scaleX, out double scaleY)
    {
        scaleX = 0; scaleY = 0;
        if (!_lastKnownBounds.HasValue) return true;
        if (frameWidth <= 0 || frameHeight <= 0) return false;

        var bounds = _lastKnownBounds.Value;
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;

        scaleX = bounds.Width  / frameWidth;
        scaleY = bounds.Height / frameHeight;
        return IsScaleRatioConsistent(scaleX, scaleY);
    }

    // Checks a (scaleX, scaleY) pair against WhatsApp's expected bounds-to-content ratio
    // envelope: maximized ≈ 1.000 or windowed ≈ 1.012/1.009 (the 14 × 7 px DWM gap),
    // ±0.03 tolerance on each axis.  Used by both readback validation and by
    // RecomputeAndApplyClip's self-validation against the cached content size.
    private static bool IsScaleRatioConsistent(double scaleX, double scaleY)
    {
        const double Tolerance = 0.03;
        bool xOk = Math.Abs(scaleX - 1.000) < Tolerance || Math.Abs(scaleX - 1.012) < Tolerance;
        bool yOk = Math.Abs(scaleY - 1.000) < Tolerance || Math.Abs(scaleY - 1.009) < Tolerance;
        return xOk && yOk;
    }

    private void TransitionToIdle(string source)
    {
        var previous = _activation.State;
        _activation.Disable();
        LogStateTransition(source, previous);
    }

    private void TransitionToArmed(string source)
    {
        var previous = _activation.State;
        if (previous == BlurActivationState.Idle)
            _activation.Enable();
        else
            _activation.Arm();

        LogStateTransition(source, previous);
    }

    private void TransitionToActive(string source)
    {
        var previous = _activation.State;
        _activation.Activate();
        LogStateTransition(source, previous);
    }

    private void LogStateTransition(string source, BlurActivationState previous)
    {
        if (previous == _activation.State)
            StartupLog.Info($"{source}: state remains {_activation.State}");
        else
            StartupLog.Info($"{source}: state transition {previous}->{_activation.State}");
    }

    private static void DispatchToWpfUiThread(string source, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            DiagLog.Warn($"{source}: Application.Current is null; dropping tracker event during shutdown");
            return;
        }

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            DiagLog.Warn($"{source}: UI dispatcher is shutting down; dropping tracker event");
            return;
        }

        var dispatchRequestedAt = Stopwatch.GetTimestamp();
        dispatcher.BeginInvoke(
            () =>
            {
                try
                {
                    var elapsedMilliseconds =
                        (Stopwatch.GetTimestamp() - dispatchRequestedAt) * 1000.0 / Stopwatch.Frequency;
                    DiagLog.Info($"{source}: UI dispatch queue latency {elapsedMilliseconds:F3} ms at priority={priority}");
                    action();
                }
                catch (Exception ex)
                {
                    DiagLog.Warn($"{source}: UI-dispatched tracker event failed", ex);
                }
            },
            priority);
    }

    // Real DispatcherTimer-based delayed dispatch used in production.  Created with
    // DispatcherPriority.Normal so it doesn't fight overlay move/clip operations
    // dispatched at Send priority.  Disposing the returned IDisposable stops the timer
    // before it fires (used to debounce rapid BoundsChanged events).
    private static IDisposable ScheduleDelayedOnWpfUiThread(TimeSpan delay, Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return NoopDisposable.Instance;

        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            try { action(); }
            catch (Exception ex) { StartupLog.Warn("ScheduleDelayedOnWpfUiThread: delayed action threw", ex); }
        };
        timer.Tick += handler;
        timer.Start();
        return new TimerStopDisposable(timer);
    }

    private sealed class TimerStopDisposable : IDisposable
    {
        private readonly DispatcherTimer _timer;
        public TimerStopDisposable(DispatcherTimer timer) => _timer = timer;
        public void Dispose() => _timer.Stop();
    }

    // Default for tests: never fires the delayed action.  Tests that exercise the
    // delayed-retry path inject a capturing dispatch instead.
    private static IDisposable NoopDelayedUiDispatch(TimeSpan _, Action __) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }

    private static void DispatchToThreadPool(string source, Action action)
    {
        _ = Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                StartupLog.Warn($"{source}: background work failed", ex);
            }
        });
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;

    private void OnFrameArrived(object? sender, ICaptureFrame frame)
    {
        int count = Interlocked.Increment(ref _frameCount);

        try
        {
            var sz = frame.ContentSize;
            if (count <= 10 || count % 30 == 0)
                StartupLog.Info($"FrameArrived #{count}: dims={sz.Width}x{sz.Height}");
            if (count == 1)
                StartupLog.Info(
                    $"FrameArrived #1 edge-fade diag: WGC ContentSize={sz.Width}x{sz.Height} px, " +
                    $"overlay DIP bounds={_lastKnownBounds}, BlurRadius={_blurPipeline.BlurRadius:F1} px");

            var blurred = _blurPipeline.BlurFrame(frame);

            if (blurred is null)
            {
                if (Interlocked.Exchange(ref _noOutputLogged, 1) == 0)
                    StartupLog.Warn("FrameArrived: blur pipeline returned no output");
                return;
            }

            _overlayWindow.PresentFrame(blurred);

            // Cadence-driven detection: first frame of the session, then every
            // DetectionCadenceFrames frames thereafter.  Captures both the post-bounds-change
            // backstop (the fast path in OnBoundsChanged is best-effort) and internal
            // WhatsApp layout shifts that emit no BoundsChanged event (e.g. divider drag).
            // Capture _setupGeneration here (background thread) so a TearDownCaptureAndOverlay
            // that executes before this lambda runs does not write stale layout data for the
            // new session.
            if (count == 1 || count % DetectionCadenceFrames == 0)
            {
                var gen = _setupGeneration;
                _dispatchToUiThread("OnFrameArrived.Detection",
                    () => { if (_setupGeneration == gen) RunDetection("OnFrameArrived"); });
            }
        }
        catch (Exception ex)
        {
            int exceptionCount = Interlocked.Increment(ref _frameExceptionCount);
            StartupLog.Warn($"FrameArrived #{count}: EXCEPTION {ex.GetType().FullName}: {ex.Message} (exception #{exceptionCount})");
            StartupLog.Warn($"FrameArrived #{count}: stack: {ex.StackTrace}");
        }
    }

    // Returns true only when the overlay window's DIP size increased — i.e. WhatsApp was
    // resized, not just moved.  Comparing DIP-to-DIP avoids the false positive that the
    // old BoundsOutgrewLastBlurredFrame produced: it mixed DIP overlay size against
    // physical-pixel WGC frame size, and the 14 × 7 px structural gap between the two
    // always tripped the threshold, triggering a solid-colour placeholder flash on every
    // position change during a drag.
    private static bool OverlaySizeGrew(Rect newBounds, Rect? previousBounds)
    {
        if (!previousBounds.HasValue) return true; // first bounds received
        return newBounds.Width  > previousBounds.Value.Width  + 1 ||
               newBounds.Height > previousBounds.Value.Height + 1;
    }

    // Returns true when the overlay DIP size changed in either direction (grow or shrink),
    // using the same ±1 DIP tolerance as OverlaySizeGrew to filter sub-pixel float noise.
    // Returns false when previousBounds is null (first-bounds event: nothing stale to clear).
    private static bool OverlaySizeChanged(Rect newBounds, Rect? previousBounds)
    {
        if (!previousBounds.HasValue) return false;
        return Math.Abs(newBounds.Width  - previousBounds.Value.Width)  > 1 ||
               Math.Abs(newBounds.Height - previousBounds.Value.Height) > 1;
    }

    private void OnHotkeyPressed(object? sender, EventArgs e) => ToggleBlur();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_screenShareDetector is not null)
        {
            _screenShareDetector.StateChanged -= OnScreenShareStateChanged;
            _screenShareDetector = null;
        }

        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;

        if (IsBlurEnabled)
        {
            TearDownCaptureAndOverlay("Dispose");
            TransitionToIdle("Dispose");
            _windowTracker.BoundsChanged -= OnBoundsChanged;
            _windowTracker.MinimizedChanged -= OnMinimizedChanged;
            _windowTracker.VisibleRegionChanged -= OnVisibleRegionChanged;
            _windowTracker.WindowPresenceChanged -= OnWindowPresenceChanged;
            _windowTracker.Stop();
        }

        // ICaptureEngine and IWindowTracker don't expose Dispose on their interfaces;
        // Stop() handles their internal resource cleanup.
        _blurPipeline.Dispose();
        _overlayWindow.Dispose();
    }
}
