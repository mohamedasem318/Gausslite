using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Gausslite.App.Diagnostics;
using Gausslite.App.Hotkey;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Blur;
using Gausslite.Core.Capture;
using Gausslite.Core.Diagnostics;
using Gausslite.Core.WindowTracking;
using Gausslite.Overlay;
using Windows.Graphics.Capture;

namespace Gausslite.App.Orchestration;

/// <summary>
/// Wires together <see cref="IWindowTracker"/>, <see cref="ICaptureEngine"/>,
/// <see cref="IBlurPipeline"/>, and <see cref="IOverlayWindow"/>.
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
    private long _lastBlurredFrameTimestamp;
    private int _lastBlurredFrameWidth;
    private int _lastBlurredFrameHeight;

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
        IAppProfile profile)
        : this(
            windowTracker,
            captureEngine,
            blurPipeline,
            overlayWindow,
            hotkeyService,
            captureItemFactory,
            profile,
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
        _windowTracker.OcclusionChanged += OnOcclusionChanged;
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
        _windowTracker.OcclusionChanged -= OnOcclusionChanged;
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

        if (_overlayVisible)
        {
            StartupLog.Info($"{source}: overlay already active; skipping duplicate MoveToBounds");
            return;
        }

        var bounds = CacheCurrentOrDefaultBounds(source);
        StartupLog.Info($"{source}: moving overlay on-screen to bounds {bounds}");
        _overlayWindow.MoveToBounds(bounds);
        _overlayVisible = true;
        TransitionToActive(source);
        var elapsedText = eventTimestamp.HasValue ? $", event-to-move={ElapsedMilliseconds(eventTimestamp.Value):F3} ms" : string.Empty;
        StartupLog.Info($"{source}: overlay move applied to bounds {bounds}{elapsedText}; expected privacy-critical event-to-move under 20 ms");
    }

    private void OnBoundsChanged(object? sender, Rect bounds)
    {
        StartupLog.Info($"OnBoundsChanged: received tracker bounds {bounds}; dispatching overlay work to UI thread");

        _dispatchToUiThread("OnBoundsChanged", () =>
        {
            if (!IsBlurEnabled)
                return;

            _lastKnownBounds = bounds;
            if (_setupReady)
            {
                if (_activation.State == BlurActivationState.Active && BoundsOutgrewLastBlurredFrame(bounds))
                {
                    StartupLog.Info(
                        "OnBoundsChanged: overlay bounds outgrew last blurred frame " +
                        $"{_lastBlurredFrameWidth}x{_lastBlurredFrameHeight}; showing placeholder until resized frame arrives");
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

            if (_windowTracker.IsOccluded)
            {
                DiagLog.Info($"OnMinimizedChanged: restore arrived while {_profile.Name} is still occluded; keeping overlay hidden");
                TransitionToArmed("OnMinimizedChanged");
                return;
            }

            ShowOverlay("OnMinimizedChanged", eventReceivedAt);
        }, priority);
    }

    private void OnOcclusionChanged(object? sender, bool isOccluded)
    {
        var priority = isOccluded ? DispatcherPriority.Normal : DispatcherPriority.Send;
        var eventReceivedAt = Stopwatch.GetTimestamp();
        DiagLog.Info($"OnOcclusionChanged: received occluded={isOccluded}; dispatching overlay work to UI thread at priority={priority}");

        _dispatchToUiThread("OnOcclusionChanged", () =>
        {
            DiagLog.Info($"OnOcclusionChanged: applying occluded={isOccluded} on UI thread");

            if (!IsBlurEnabled)
                return;

            if (isOccluded)
            {
                TransitionToArmed("OnOcclusionChanged");
                HideOverlay("OnOcclusionChanged");
                return;
            }

            if (!_windowTracker.IsWindowPresent || _windowTracker.IsMinimized)
            {
                TransitionToArmed("OnOcclusionChanged");
                return;
            }

            ShowOverlay("OnOcclusionChanged", eventReceivedAt);
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

        if (!_windowTracker.IsWindowPresent || _windowTracker.IsMinimized || _windowTracker.IsOccluded)
        {
            TransitionToArmed(source);
            HideOverlay(source);
            return;
        }

        ShowOverlay(source);
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

            var blurred = _blurPipeline.BlurFrame(frame);

            if (blurred is null)
            {
                if (Interlocked.Exchange(ref _noOutputLogged, 1) == 0)
                    StartupLog.Warn("FrameArrived: blur pipeline returned no output");
                return;
            }

            _overlayWindow.PresentFrame(blurred);
            Interlocked.Exchange(ref _lastBlurredFrameWidth, sz.Width);
            Interlocked.Exchange(ref _lastBlurredFrameHeight, sz.Height);
            Interlocked.Exchange(ref _lastBlurredFrameTimestamp, Stopwatch.GetTimestamp());
        }
        catch (Exception ex)
        {
            int exceptionCount = Interlocked.Increment(ref _frameExceptionCount);
            StartupLog.Warn($"FrameArrived #{count}: EXCEPTION {ex.GetType().FullName}: {ex.Message} (exception #{exceptionCount})");
            StartupLog.Warn($"FrameArrived #{count}: stack: {ex.StackTrace}");
        }
    }

    private bool BoundsOutgrewLastBlurredFrame(Rect bounds)
    {
        int width = Interlocked.CompareExchange(ref _lastBlurredFrameWidth, 0, 0);
        int height = Interlocked.CompareExchange(ref _lastBlurredFrameHeight, 0, 0);
        if (width <= 0 || height <= 0)
            return true;

        return bounds.Width > width + 1 || bounds.Height > height + 1;
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
            _windowTracker.OcclusionChanged -= OnOcclusionChanged;
            _windowTracker.WindowPresenceChanged -= OnWindowPresenceChanged;
            _windowTracker.Stop();
        }

        // ICaptureEngine and IWindowTracker don't expose Dispose on their interfaces;
        // Stop() handles their internal resource cleanup.
        _blurPipeline.Dispose();
        _overlayWindow.Dispose();
    }
}
