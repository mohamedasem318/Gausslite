// SPDX-License-Identifier: AGPL-3.0-or-later
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
    private IReadOnlyList<Rect>? _lastVisibleRegion;
    private bool _lastWindowPresent;
    private IntPtr _overlayWindowHandle;

    // Diagnostic state — written from poll thread only (no locking needed).
    private const int BoundsChangeLogInterval = 10;
    private bool _firstWindowFound;
    private int _boundsChangeCount;
    private bool _notFoundWarningLogged;

    public event EventHandler<Rect>? BoundsChanged;
    public event EventHandler<bool>? MinimizedChanged;
    public event EventHandler<IReadOnlyList<Rect>>? VisibleRegionChanged;
    public event EventHandler<bool>? WindowPresenceChanged;
    public Rect? CurrentBounds { get; private set; }
    public bool IsWindowPresent { get; private set; }
    public bool IsMinimized { get; private set; }
    public IReadOnlyList<Rect>? VisibleRegion { get; private set; }
    public bool IsTracking { get; private set; }

    public WindowTracker(IWin32Api win32, IAppProfile profile, TimeSpan? pollInterval = null)
    {
        _win32 = win32;
        _profile = profile;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public void SetOverlayWindowHandle(IntPtr hwnd) =>
        _overlayWindowHandle = hwnd;

    public void RequestRepaintOfTrackedWindow()
    {
        // Resolve the tracked HWND fresh — it can change across capture sessions if the
        // user closes and re-opens the app.  FindWindowHandle is what the poll loop calls
        // every 33 ms, so a single extra call is negligible overhead.
        var hwnd = _profile.FindWindowHandle();
        if (hwnd == IntPtr.Zero) return;
        _win32.InvalidateClientArea(hwnd);
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
        _lastKnownBounds = null;
        _lastWindowPresent = false;
        _lastMinimized = false;
        _lastVisibleRegion = null;
        CurrentBounds = null;
        IsWindowPresent = false;
        IsMinimized = false;
        VisibleRegion = null;
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
                _lastMinimized = sample.Value.IsMinimized;
                IsMinimized = sample.Value.IsMinimized;

                var newRegion = sample.Value.VisibleRegion;
                var regionChanged = !RegionsEqual(_lastVisibleRegion, newRegion);
                _lastVisibleRegion = newRegion;
                VisibleRegion = newRegion;

                if (minimizedChanged)
                {
                    DiagLog.Info($"WindowTracker: minimized changed to {_lastMinimized}, HWND=0x{sample.Value.Hwnd:X}");
                    MinimizedChanged?.Invoke(this, _lastMinimized);
                }

                if (regionChanged)
                {
                    DiagLog.Info($"WindowTracker: visible region changed to {newRegion.Count} rect(s), HWND=0x{sample.Value.Hwnd:X}");
                    VisibleRegionChanged?.Invoke(this, newRegion);
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
                VisibleRegion = null;
                // Reset so that on re-appearance VisibleRegionChanged fires with the new region.
                _lastVisibleRegion = null;

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

                if (!_firstWindowFound && !_notFoundWarningLogged
                    && (DateTime.UtcNow - loopStart).TotalSeconds > 5)
                {
                    _notFoundWarningLogged = true;
                    DiagLog.Warn($"WindowTracker: {_profile.Name} not found after 5 seconds — capture cannot start");
                }
            }
        }
    }

    private (Rect Bounds, IntPtr Hwnd, bool IsMinimized, IReadOnlyList<Rect> VisibleRegion)? SampleWindowState()
    {
        var hwnd = _profile.FindWindowHandle();
        if (hwnd == IntPtr.Zero) return null;

        if (_win32.IsIconic(hwnd))
            return (Rect.Empty, hwnd, IsMinimized: true, VisibleRegion: Array.Empty<Rect>());

        if (!_win32.GetWindowRect(hwnd, out var rect)) return null;

        var isZoomed = _win32.IsZoomed(hwnd);
        var normalizedRect = NormalizeWindowRect(rect, hwnd, isZoomed, _win32);

        var visiblePhysical = ComputeVisibleRegion(normalizedRect, hwnd, _overlayWindowHandle, _win32);

        var dpi = _win32.GetDpiForWindow(hwnd);
        var dipBounds = ToDeviceIndependentRect(normalizedRect, dpi);
        IReadOnlyList<Rect> dipVisible = visiblePhysical.Count == 0
            ? Array.Empty<Rect>()
            : visiblePhysical.ConvertAll(r => ToDeviceIndependentRect(r, dpi));

        return (dipBounds, hwnd, false, dipVisible);
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

    // WS_EX_TOOLWINDOW: marks helper/system windows (taskbar elements, tray popups, DWM UI).
    // These are visible in the Z-order but are not user-facing covering apps.
    private const int WS_EX_TOOLWINDOW = 0x80;

    /// <summary>
    /// Computes which sub-rectangles of <paramref name="physicalRect"/> remain visible
    /// after subtracting all visible, non-minimized top-level windows above
    /// <paramref name="whatsappHwnd"/> in Z-order (excluding the overlay window).
    /// Returns the full rect when nothing covers it, or an empty list when fully occluded.
    /// </summary>
    internal static List<RECT> ComputeVisibleRegion(
        RECT physicalRect, IntPtr whatsappHwnd, IntPtr overlayHwnd, IWin32Api win32)
    {
        if (whatsappHwnd == IntPtr.Zero) return new List<RECT>();

        var whatsappRoot = NormalizeRoot(whatsappHwnd, win32);
        if (whatsappRoot == IntPtr.Zero) return new List<RECT>();

        var overlayRoot = overlayHwnd == IntPtr.Zero ? IntPtr.Zero : NormalizeRoot(overlayHwnd, win32);

        // WhatsApp's process ID — used to skip the app's own internal HWNDs (e.g. the
        // WinUI 3 InputNonClientPointerSource that sits above the main HWND in Z-order
        // and covers exactly the title bar, creating a "notch" artifact in the clip).
        var whatsappPid = win32.GetWindowProcessId(whatsappHwnd);

        var visibleRects = new List<RECT> { physicalRect };

        // Walk up the Z-order from WhatsApp's root towards the foreground.
        // GetPreviousWindow(hwnd) = GetWindow(hwnd, GW_HWNDPREV) = next window upward in Z-order.
        var current = win32.GetPreviousWindow(whatsappRoot);
        while (current != IntPtr.Zero)
        {
            // Skip the overlay window — it intentionally sits above WhatsApp.
            if (overlayRoot != IntPtr.Zero && current == overlayRoot)
            {
                current = win32.GetPreviousWindow(current);
                continue;
            }

            // Skip windows from the same process as WhatsApp: these are internal HWNDs
            // (WinUI 3 helpers, WebView2 hosts, etc.) that are not separate covering apps.
            if (whatsappPid != 0 && win32.GetWindowProcessId(current) == whatsappPid)
            {
                current = win32.GetPreviousWindow(current);
                continue;
            }

            // Skip toolwindows: system UI (taskbar strips, tray popups, notification
            // islands) that appear above all normal windows but are not covering apps.
            if ((win32.GetWindowExStyle(current) & WS_EX_TOOLWINDOW) != 0)
            {
                current = win32.GetPreviousWindow(current);
                continue;
            }

            if (win32.IsWindowVisible(current) && !win32.IsIconic(current))
            {
                if (win32.GetWindowRect(current, out var coveringRect))
                {
                    visibleRects = SubtractRect(visibleRects, coveringRect);
                    if (visibleRects.Count == 0) return visibleRects; // fully occluded
                }
            }

            current = win32.GetPreviousWindow(current);
        }

        return visibleRects;
    }

    // Subtracts `covering` from each rect in `visibleRects`, producing up to 4 sub-rects per
    // overlap (top/bottom bands and left/right strips at the intersection height).
    private static List<RECT> SubtractRect(List<RECT> visibleRects, RECT covering)
    {
        var result = new List<RECT>(visibleRects.Count * 2);
        foreach (var vis in visibleRects)
        {
            if (!TryIntersect(vis, covering, out var inter))
            {
                result.Add(vis); // no overlap
                continue;
            }

            // Top band (above the covering rect)
            if (inter.Top > vis.Top)
                result.Add(new RECT { Left = vis.Left, Top = vis.Top, Right = vis.Right, Bottom = inter.Top });
            // Bottom band (below the covering rect)
            if (inter.Bottom < vis.Bottom)
                result.Add(new RECT { Left = vis.Left, Top = inter.Bottom, Right = vis.Right, Bottom = vis.Bottom });
            // Left strip (at intersection height)
            if (inter.Left > vis.Left)
                result.Add(new RECT { Left = vis.Left, Top = inter.Top, Right = inter.Left, Bottom = inter.Bottom });
            // Right strip (at intersection height)
            if (inter.Right < vis.Right)
                result.Add(new RECT { Left = inter.Right, Top = inter.Top, Right = vis.Right, Bottom = inter.Bottom });
            // (no addition when covering fully contains vis — that rect is consumed)
        }
        return result;
    }

    private static bool TryIntersect(RECT a, RECT b, out RECT result)
    {
        int left   = Math.Max(a.Left,   b.Left);
        int top    = Math.Max(a.Top,    b.Top);
        int right  = Math.Min(a.Right,  b.Right);
        int bottom = Math.Min(a.Bottom, b.Bottom);

        if (left >= right || top >= bottom)
        {
            result = default;
            return false;
        }
        result = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        return true;
    }

    private static bool RegionsEqual(IReadOnlyList<Rect>? a, IReadOnlyList<Rect>? b)
    {
        if (a is null != b is null) return false;
        if (a is null) return true;
        if (a.Count != b!.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static IntPtr NormalizeRoot(IntPtr hwnd, IWin32Api win32)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        var root = win32.GetRootWindow(hwnd);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
