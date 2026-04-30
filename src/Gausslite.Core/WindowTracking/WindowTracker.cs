using System.Windows;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.Diagnostics;

namespace Gausslite.Core.WindowTracking;

public sealed class WindowTracker : IWindowTracker, IDisposable
{
    private readonly IWin32Api _win32;
    private readonly IAppProfile _profile;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(33);

    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private Rect? _lastKnownBounds;
    private bool _lastMinimized;
    private bool _lastOccluded;
    private bool _lastWindowPresent;
    private IntPtr _overlayWindowHandle;

    // Diagnostic state — written from poll thread only (no locking needed).
    private const int BoundsChangeLogInterval = 10;
    private bool _firstWindowFound;
    private int _boundsChangeCount;
    private bool _notFoundWarningLogged;

    public event EventHandler<Rect>? BoundsChanged;
    public event EventHandler<bool>? MinimizedChanged;
    public event EventHandler<bool>? OcclusionChanged;
    public event EventHandler<bool>? WindowPresenceChanged;
    public Rect? CurrentBounds { get; private set; }
    public bool IsWindowPresent { get; private set; }
    public bool IsMinimized { get; private set; }
    public bool IsOccluded { get; private set; }
    public bool IsTracking { get; private set; }

    public WindowTracker(IWin32Api win32, IAppProfile profile, TimeSpan? pollInterval = null)
    {
        _win32 = win32;
        _profile = profile;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public void SetOverlayWindowHandle(IntPtr hwnd) =>
        _overlayWindowHandle = hwnd;

    public void Start()
    {
        if (IsTracking) return;
        IsTracking = true;
        _cts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsTracking) return;
        IsTracking = false;
        _cts?.Cancel();
        _lastKnownBounds = null;
        _lastWindowPresent = false;
        _lastMinimized = false;
        _lastOccluded = false;
        CurrentBounds = null;
        IsWindowPresent = false;
        IsMinimized = false;
        IsOccluded = false;
    }

    private async Task PollLoop(CancellationToken ct)
    {
        var loopStart = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var sample = SampleWindowState();
            if (sample.HasValue && !sample.Value.IsMinimized)
                CurrentBounds = sample.Value.Bounds;

            if (sample.HasValue)
            {
                if (!_lastWindowPresent)
                {
                    _lastWindowPresent = true;
                    IsWindowPresent = true;
                    DiagLog.Info($"WindowTracker: presence changed to True, HWND=0x{sample.Value.Hwnd:X}");
                    WindowPresenceChanged?.Invoke(this, true);
                }

                var minimizedChanged = sample.Value.IsMinimized != _lastMinimized;
                var occlusionChanged = sample.Value.IsOccluded != _lastOccluded;

                _lastMinimized = sample.Value.IsMinimized;
                IsMinimized = sample.Value.IsMinimized;
                _lastOccluded = sample.Value.IsOccluded;
                IsOccluded = sample.Value.IsOccluded;

                if (minimizedChanged)
                {
                    DiagLog.Info($"WindowTracker: minimized changed to {_lastMinimized}, HWND=0x{sample.Value.Hwnd:X}");
                    MinimizedChanged?.Invoke(this, _lastMinimized);
                }

                if (occlusionChanged)
                {
                    DiagLog.Info($"WindowTracker: occlusion changed to {_lastOccluded}, HWND=0x{sample.Value.Hwnd:X}");
                    OcclusionChanged?.Invoke(this, _lastOccluded);
                }

                if (!_firstWindowFound)
                {
                    _firstWindowFound = true;
                    DiagLog.Info($"WindowTracker: {_profile.Name} window detected, HWND=0x{sample.Value.Hwnd:X}, bounds={sample.Value.Bounds}, minimized={sample.Value.IsMinimized}");
                }

                if (sample.Value.IsMinimized)
                    continue;

                if (!_lastKnownBounds.HasValue || sample.Value.Bounds != _lastKnownBounds.Value)
                {
                    _boundsChangeCount++;
                    if (_boundsChangeCount == 1 || _boundsChangeCount % BoundsChangeLogInterval == 0)
                        DiagLog.Info($"WindowTracker: bounds change #{_boundsChangeCount} to {sample.Value.Bounds}");

                    _lastKnownBounds = sample.Value.Bounds;
                    BoundsChanged?.Invoke(this, sample.Value.Bounds);
                }
            }
            else
            {
                _lastKnownBounds = null;
                CurrentBounds = null;
                IsWindowPresent = false;
                IsMinimized = false;
                IsOccluded = false;

                if (_lastWindowPresent)
                {
                    _lastWindowPresent = false;
                    DiagLog.Info("WindowTracker: presence changed to False");
                    WindowPresenceChanged?.Invoke(this, false);
                }

                if (_lastMinimized)
                {
                    _lastMinimized = false;
                    DiagLog.Info($"WindowTracker: minimized changed to False because {_profile.Name} window is no longer present");
                    MinimizedChanged?.Invoke(this, false);
                }

                _lastOccluded = false;

                if (!_firstWindowFound && !_notFoundWarningLogged
                    && (DateTime.UtcNow - loopStart).TotalSeconds > 5)
                {
                    _notFoundWarningLogged = true;
                    DiagLog.Warn($"WindowTracker: {_profile.Name} not found after 5 seconds — capture cannot start");
                }
            }
        }
    }

    private (Rect Bounds, IntPtr Hwnd, bool IsMinimized, bool IsOccluded)? SampleWindowState()
    {
        var hwnd = _profile.FindWindowHandle();
        if (hwnd == IntPtr.Zero) return null;

        if (_win32.IsIconic(hwnd))
            return (Rect.Empty, hwnd, true, true);

        if (!_win32.GetWindowRect(hwnd, out var rect)) return null;

        var isZoomed = _win32.IsZoomed(hwnd);
        var normalizedRect = NormalizeWindowRect(rect, hwnd, isZoomed, _win32);
        var isOccluded = IsOccludedAtCenter(normalizedRect, hwnd, _overlayWindowHandle, _win32);

        var dpi = _win32.GetDpiForWindow(hwnd);
        return (ToDeviceIndependentRect(normalizedRect, dpi), hwnd, false, isOccluded);
    }

    internal static RECT NormalizeWindowRect(RECT rawRect, IntPtr hwnd, bool isZoomed, IWin32Api win32)
    {
        if (!isZoomed) return rawRect;
        return win32.TryGetMonitorWorkArea(hwnd, out var workArea) ? workArea : rawRect;
    }

    internal static Rect ToDeviceIndependentRect(RECT r, uint dpi)
    {
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        return new Rect(
            r.Left / scale,
            r.Top / scale,
            (r.Right - r.Left) / scale,
            (r.Bottom - r.Top) / scale);
    }

    internal static bool IsOccludedAtCenter(RECT physicalRect, IntPtr whatsappHwnd, IntPtr overlayHwnd, IWin32Api win32)
    {
        if (whatsappHwnd == IntPtr.Zero)
            return true;

        var center = new POINT
        {
            X = physicalRect.Left + ((physicalRect.Right - physicalRect.Left) / 2),
            Y = physicalRect.Top + ((physicalRect.Bottom - physicalRect.Top) / 2)
        };

        var whatsappRoot = NormalizeRoot(whatsappHwnd, win32);
        var overlayRoot = NormalizeRoot(overlayHwnd, win32);
        var candidate = win32.WindowFromPoint(center);
        var candidateRoot = NormalizeRoot(candidate, win32);

        while (candidateRoot != IntPtr.Zero && overlayRoot != IntPtr.Zero && candidateRoot == overlayRoot)
        {
            candidate = win32.GetNextWindow(candidateRoot);
            candidateRoot = NormalizeRoot(candidate, win32);
        }

        return candidateRoot == IntPtr.Zero || candidateRoot != whatsappRoot;
    }

    private static IntPtr NormalizeRoot(IntPtr hwnd, IWin32Api win32)
    {
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;

        var root = win32.GetRootWindow(hwnd);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
