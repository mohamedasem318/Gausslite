using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Gausslite.App.Diagnostics;
using Gausslite.App.Hotkey;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.Detection;
using Gausslite.Core.Diagnostics;
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

    private readonly IWindowTracker _windowTracker;
    private readonly ICaptureEngine _captureEngine;
    private readonly IBlurPipeline _blurPipeline;
    private readonly IOverlayWindow _overlayWindow;
    private readonly IHotkeyService _hotkeyService;
    private readonly ICaptureItemFactory _captureItemFactory;
    private readonly IAppProfile _profile;
    private readonly IRegionDetector _regionDetector;
    private readonly UiThreadDispatch _dispatchToUiThread;
    private readonly BackgroundDispatch _dispatchToBackground;
    private readonly BlurActivationStateMachine _activation = new();

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
    private int _detectionDone;      // 0 = not yet run, 1 = run for this capture session

    // Stores the most recent detection result.
    // Written and read on the UI thread only — plain assignment is safe.
    // Null means detection has never run in the current session.
    private RegionDetectionResult? _lastDetectionResult;
    internal RegionDetectionResult? LastDetectionResult => _lastDetectionResult;

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
            DispatchToThreadPool)
    {
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
        BackgroundDispatch? dispatchToBackground = null)
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

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    public void ToggleBlur()
    {
        if (IsBlurEnabled) DisableBlur();
        else EnableBlur();
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
    }

    public void EnableBlur()
    {
        if (IsBlurEnabled) return;
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

        // Reset detection flag so the first frame of the next capture session triggers detection.
        Interlocked.Exchange(ref _detectionDone, 0);
        _lastDetectionResult = null;

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

        // Always (re-)apply the visible-region clip after showing or while already visible,
        // so region changes while the overlay is active are reflected immediately.
        ApplyRegionClip(source, bounds);
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

            // Re-run detection after the overlay has been moved so rects reflect the new layout.
            RunDetection("OnBoundsChanged");
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
            DiagLog.Info($"OnVisibleRegionChanged: applying region ({region.Count} rect(s)) on UI thread");

            if (!IsBlurEnabled)
                return;

            if (fullyOccluded)
            {
                TransitionToArmed("OnVisibleRegionChanged");
                HideOverlay("OnVisibleRegionChanged");
                return;
            }

            if (!_windowTracker.IsWindowPresent || _windowTracker.IsMinimized)
            {
                TransitionToArmed("OnVisibleRegionChanged");
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

    // True when the tracker reports a non-empty visible region (i.e. window is not fully occluded).
    private bool HasVisibleRegion() => (_windowTracker.VisibleRegion?.Count ?? 0) > 0;

    // Applies a WPF clip to the overlay based on the current visible region.
    // Called after MoveToBounds so the clip is in overlay-local DIP coordinates.
    private void ApplyRegionClip(string source, Rect overlayBounds)
    {
        var region = _windowTracker.VisibleRegion;
        if (region is null || region.Count == 0)
        {
            _overlayWindow.SetClip(null);
            return;
        }

        // Full-bounds case → no clip needed.
        if (region.Count == 1 && region[0] == overlayBounds)
        {
            _overlayWindow.SetClip(null);
            StartupLog.Info($"{source}: full visibility — clip cleared");
            return;
        }

        // Convert screen-DIP rects to overlay-local coordinates (origin = overlay top-left).
        var localRects = new List<Rect>(region.Count);
        foreach (var r in region)
            localRects.Add(new Rect(r.X - overlayBounds.X, r.Y - overlayBounds.Y, r.Width, r.Height));

        _overlayWindow.SetClip(localRects);
        StartupLog.Info($"{source}: partial visibility — clip set to {localRects.Count} local rect(s)");
    }

    // Reads the latest GPU frame back to the CPU and runs the region detector.
    // Must be called on the UI thread — both trigger points (OnFrameArrived dispatch and
    // OnBoundsChanged body) ensure this.  _lastDetectionResult is written here and read
    // only on the UI thread, so plain assignment is safe.
    private void RunDetection(string source)
    {
        if (!_blurPipeline.TryReadLatestFrameAsBgra(out var pixels, out var w, out var h, out var s))
        {
            StartupLog.Info($"{source}: detection skipped — no frame available for readback");
            return;
        }

        var result = _regionDetector.Detect(pixels, w, h, s);
        _lastDetectionResult = result;

        if (result.Succeeded)
            StartupLog.Info(
                $"{source}: detection succeeded — rail={result.DetectedRailSide}, " +
                $"chatList={result.ChatListRect}, conversation={result.ConversationRect}");
        else
            StartupLog.Info($"{source}: detection failed — {result.FailureReason}");
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

            // Run detection once on the first successfully blurred frame.
            // BlurFrame has updated _cachedInputFrame, so TryReadLatestFrameAsBgra can succeed.
            // Capture _setupGeneration here (background thread) so a TearDownCaptureAndOverlay
            // that executes before this lambda runs does not write stale layout data for the
            // new session.
            if (Interlocked.Exchange(ref _detectionDone, 1) == 0)
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

    private void OnHotkeyPressed(object? sender, EventArgs e) => ToggleBlur();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
