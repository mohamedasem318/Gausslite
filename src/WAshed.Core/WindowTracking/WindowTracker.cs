using System.Windows;

namespace WAshed.Core.WindowTracking;

public sealed class WindowTracker : IWindowTracker, IDisposable
{
    private static readonly string[] WhatsAppProcessNames = { "WhatsApp", "WhatsAppDesktop" };

    private readonly IWin32Api _win32;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private Rect? _lastKnownBounds;

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

            var bounds = SampleBounds();
            CurrentBounds = bounds;

            if (bounds.HasValue)
            {
                if (!_lastKnownBounds.HasValue || bounds.Value != _lastKnownBounds.Value)
                {
                    _lastKnownBounds = bounds.Value;
                    BoundsChanged?.Invoke(this, bounds.Value);
                }
            }
            else
            {
                _lastKnownBounds = null;
            }
        }
    }

    private Rect? SampleBounds()
    {
        foreach (var name in WhatsAppProcessNames)
        {
            var handles = _win32.GetWindowHandlesForProcessName(name);
            if (handles.Count > 0)
            {
                var hwnd = handles[0];
                if (_win32.GetWindowRect(hwnd, out var rect))
                {
                    var dpi = _win32.GetDpiForWindow(hwnd);
                    return ToPhysicalRect(rect, dpi);
                }
            }
        }
        return null;
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
