using System.Windows;
using WAshed.Core.Diagnostics;

namespace WAshed.Core.WindowTracking;

public sealed class WindowTracker : IWindowTracker, IDisposable
{
    private readonly IWin32Api _win32;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private Rect? _lastKnownBounds;

    // Diagnostic state — written from poll thread only (no locking needed).
    private bool _firstWindowFound;
    private bool _firstBoundsChangeLogged;
    private bool _notFoundWarningLogged;

    public event EventHandler<Rect>? BoundsChanged;
    public Rect? CurrentBounds { get; private set; }
    public bool IsTracking { get; private set; }

    public WindowTracker(IWin32Api win32, TimeSpan? pollInterval = null)
    {
        _win32 = win32;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(100);
    }

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

            var sample = SampleBoundsWithHandle();
            CurrentBounds = sample?.Bounds;

            if (sample.HasValue)
            {
                if (!_firstWindowFound)
                {
                    _firstWindowFound = true;
                    DiagLog.Info($"WindowTracker: WhatsApp window detected, HWND=0x{sample.Value.Hwnd:X}, bounds={sample.Value.Bounds}");
                }

                if (!_lastKnownBounds.HasValue || sample.Value.Bounds != _lastKnownBounds.Value)
                {
                    if (!_firstBoundsChangeLogged)
                    {
                        _firstBoundsChangeLogged = true;
                        DiagLog.Info($"WindowTracker: bounds changed to {sample.Value.Bounds}");
                    }

                    _lastKnownBounds = sample.Value.Bounds;
                    BoundsChanged?.Invoke(this, sample.Value.Bounds);
                }
            }
            else
            {
                _lastKnownBounds = null;

                if (!_firstWindowFound && !_notFoundWarningLogged
                    && (DateTime.UtcNow - loopStart).TotalSeconds > 5)
                {
                    _notFoundWarningLogged = true;
                    DiagLog.Warn("WindowTracker: WhatsApp not found after 5 seconds — capture cannot start");
                }
            }
        }
    }

    private (Rect Bounds, IntPtr Hwnd)? SampleBoundsWithHandle()
    {
        var hwnd = _win32.FindWhatsAppWindowHandle();
        if (hwnd == IntPtr.Zero) return null;

        if (!_win32.GetWindowRect(hwnd, out var rect)) return null;

        var dpi = _win32.GetDpiForWindow(hwnd);
        return (ToPhysicalRect(rect, dpi), hwnd);
    }

    private static Rect ToPhysicalRect(RECT r, uint dpi)
    {
        double scale = dpi / 96.0;
        return new Rect(
            r.Left * scale,
            r.Top * scale,
            (r.Right - r.Left) * scale,
            (r.Bottom - r.Top) * scale);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
