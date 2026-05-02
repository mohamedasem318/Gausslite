using NSubstitute;
using System.Windows;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.Tests.WindowTracking;

public sealed class WindowTrackerTests : IDisposable
{
    private readonly IWin32Api _win32 = Substitute.For<IWin32Api>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly List<Rect> _receivedBounds = new();
    private WindowTracker? _tracker;

    private WindowTracker CreateTracker() =>
        new WindowTracker(_win32, _profile, TimeSpan.FromMilliseconds(10));

    private void NoHandle() =>
        _profile.FindWindowHandle().Returns(IntPtr.Zero);

    private void SetupHandleAndRect(IntPtr hwnd, RECT rect, uint dpi = 96)
    {
        _profile.FindWindowHandle().Returns(hwnd);
        RECT dummy = default;
        _win32.GetWindowRect(Arg.Any<IntPtr>(), out dummy)
              .Returns(x => { x[1] = rect; return true; });
        _win32.GetDpiForWindow(hwnd).Returns(dpi);
    }

    private void SetupMonitorWorkArea(IntPtr hwnd, RECT workArea)
    {
        RECT dummy = default;
        _win32.TryGetMonitorWorkArea(hwnd, out dummy)
              .Returns(x => { x[1] = workArea; return true; });
    }

    [Fact]
    public async Task CurrentBounds_IsNull_WhenNoWhatsAppProcess()
    {
        NoHandle();
        _tracker = CreateTracker();
        _tracker.Start();
        await Task.Delay(60);

        Assert.Null(_tracker.CurrentBounds);
    }

    [Fact]
    public async Task CurrentBounds_ReturnsCorrectBounds_WhenWhatsAppRunning()
    {
        var hwnd = new IntPtr(1234);
        SetupHandleAndRect(hwnd, new RECT { Left = 10, Top = 20, Right = 110, Bottom = 120 });

        _tracker = CreateTracker();
        _tracker.Start();
        await Task.Delay(60);

        Assert.NotNull(_tracker.CurrentBounds);
        Assert.Equal(new Rect(10, 20, 100, 100), _tracker.CurrentBounds!.Value);
    }

    [Fact]
    public async Task BoundsChanged_Raised_WhenBoundsChange()
    {
        var hwnd = new IntPtr(42);
        _profile.FindWindowHandle().Returns(hwnd);
        _win32.GetDpiForWindow(hwnd).Returns(96u);

        var rect1 = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var rect2 = new RECT { Left = 50, Top = 50, Right = 200, Bottom = 200 };
        var callCount = 0;
        RECT dummy = default;
        _win32.GetWindowRect(hwnd, out dummy)
              .Returns(x =>
              {
                  x[1] = Interlocked.Increment(ref callCount) <= 3 ? rect1 : rect2;
                  return true;
              });

        _tracker = CreateTracker();
        _tracker.BoundsChanged += (_, r) => _receivedBounds.Add(r);
        _tracker.Start();
        await Task.Delay(150);

        Assert.True(_receivedBounds.Count >= 2,
            $"Expected at least 2 distinct bound events, got {_receivedBounds.Count}");
        Assert.Contains(_receivedBounds, r => r == new Rect(0, 0, 100, 100));
        Assert.Contains(_receivedBounds, r => r == new Rect(50, 50, 150, 150));
    }

    [Fact]
    public async Task BoundsChanged_NotRaised_WhenBoundsUnchanged()
    {
        var hwnd = new IntPtr(99);
        SetupHandleAndRect(hwnd, new RECT { Left = 0, Top = 0, Right = 200, Bottom = 150 });

        _tracker = CreateTracker();
        _tracker.BoundsChanged += (_, r) => _receivedBounds.Add(r);
        _tracker.Start();
        await Task.Delay(100);

        Assert.Single(_receivedBounds);
    }

    [Fact]
    public async Task DpiTranslation_ConvertsPhysicalPixelsToWpfDips_At150Percent()
    {
        var hwnd = new IntPtr(77);
        SetupHandleAndRect(hwnd, new RECT { Left = 150, Top = 300, Right = 450, Bottom = 600 }, dpi: 144);

        _tracker = CreateTracker();
        _tracker.Start();
        await Task.Delay(60);

        Assert.NotNull(_tracker.CurrentBounds);
        Assert.Equal(new Rect(100, 200, 200, 200), _tracker.CurrentBounds!.Value);
    }

    [Fact]
    public void DefaultPollInterval_IsThirtyThreeMilliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(33), WindowTracker.DefaultPollInterval);
    }

    [Theory]
    [InlineData(96, 0, 0, 1920, 1040, 0, 0, 1920, 1040)]
    [InlineData(144, 0, 0, 2880, 1560, 0, 0, 1920, 1040)]
    [InlineData(120, -1600, 0, 0, 860, -1280, 0, 1280, 688)]
    public void MaximizedBounds_NormalizeToMonitorWorkAreaBeforeDpiConversion(
        uint dpi,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        double expectedLeft,
        double expectedTop,
        double expectedWidth,
        double expectedHeight)
    {
        var hwnd = new IntPtr(88);
        var rawMaximizedRect = new RECT
        {
            Left = workLeft - 8,
            Top = workTop - 8,
            Right = workRight + 8,
            Bottom = workBottom + 8
        };
        var workArea = new RECT { Left = workLeft, Top = workTop, Right = workRight, Bottom = workBottom };
        SetupMonitorWorkArea(hwnd, workArea);

        var normalized = WindowTracker.NormalizeWindowRect(rawMaximizedRect, hwnd, isZoomed: true, _win32);
        var dips = WindowTracker.ToDeviceIndependentRect(normalized, dpi);

        Assert.Equal(new Rect(expectedLeft, expectedTop, expectedWidth, expectedHeight), dips);
    }

    [Fact]
    public void NormalBounds_DoNotClampToMonitorWorkArea()
    {
        var hwnd = new IntPtr(89);
        var rawRect = new RECT { Left = -8, Top = -8, Right = 808, Bottom = 608 };
        SetupMonitorWorkArea(hwnd, new RECT { Left = 0, Top = 0, Right = 800, Bottom = 600 });

        var normalized = WindowTracker.NormalizeWindowRect(rawRect, hwnd, isZoomed: false, _win32);

        Assert.Equal(rawRect.Left, normalized.Left);
        Assert.Equal(rawRect.Top, normalized.Top);
        Assert.Equal(rawRect.Right, normalized.Right);
        Assert.Equal(rawRect.Bottom, normalized.Bottom);
    }

    // ── ComputeVisibleRegion (testable static seam) ──────────────────────────

    [Fact]
    public void ComputeVisibleRegion_ReturnsFullBounds_WhenNoCoveringWindow()
    {
        var whatsapp = new IntPtr(10);
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(IntPtr.Zero); // nothing above

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.Single(region);
        Assert.Equal(rect.Left,   region[0].Left);
        Assert.Equal(rect.Top,    region[0].Top);
        Assert.Equal(rect.Right,  region[0].Right);
        Assert.Equal(rect.Bottom, region[0].Bottom);
    }

    [Fact]
    public void ComputeVisibleRegion_ReturnsEmpty_WhenFullyCoveredByAnotherWindow()
    {
        var whatsapp = new IntPtr(10);
        var chrome   = new IntPtr(20);
        var rect     = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(chrome);
        _win32.GetPreviousWindow(chrome).Returns(IntPtr.Zero);
        _win32.IsWindowVisible(chrome).Returns(true);
        _win32.IsIconic(chrome).Returns(false);
        RECT dummy = default;
        _win32.GetWindowRect(chrome, out dummy)
              .Returns(x => { x[1] = rect; return true; }); // covers exactly

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.Empty(region);
    }

    [Fact]
    public void ComputeVisibleRegion_ReturnsPartialRect_WhenRightHalfCovered()
    {
        var whatsapp = new IntPtr(10);
        var chrome   = new IntPtr(20);
        var whatsappRect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var coveringRect = new RECT { Left = 50, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(chrome);
        _win32.GetPreviousWindow(chrome).Returns(IntPtr.Zero);
        _win32.IsWindowVisible(chrome).Returns(true);
        _win32.IsIconic(chrome).Returns(false);
        RECT dummy = default;
        _win32.GetWindowRect(chrome, out dummy)
              .Returns(x => { x[1] = coveringRect; return true; });

        var region = WindowTracker.ComputeVisibleRegion(whatsappRect, whatsapp, IntPtr.Zero, _win32);

        // Visible: left half (0,0,50,100)
        Assert.Single(region);
        Assert.Equal(0,   region[0].Left);
        Assert.Equal(0,   region[0].Top);
        Assert.Equal(50,  region[0].Right);
        Assert.Equal(100, region[0].Bottom);
    }

    [Fact]
    public void ComputeVisibleRegion_ReturnsLShape_WhenTopRightCornerCovered()
    {
        var whatsapp     = new IntPtr(10);
        var chrome       = new IntPtr(20);
        var whatsappRect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var coveringRect = new RECT { Left = 50, Top = 0, Right = 100, Bottom = 50 }; // top-right quadrant

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(chrome);
        _win32.GetPreviousWindow(chrome).Returns(IntPtr.Zero);
        _win32.IsWindowVisible(chrome).Returns(true);
        _win32.IsIconic(chrome).Returns(false);
        RECT dummy = default;
        _win32.GetWindowRect(chrome, out dummy)
              .Returns(x => { x[1] = coveringRect; return true; });

        var region = WindowTracker.ComputeVisibleRegion(whatsappRect, whatsapp, IntPtr.Zero, _win32);

        // L-shape: bottom band (0,50,100,100) + top-left (0,0,50,50)
        Assert.Equal(2, region.Count);
        Assert.Contains(region, r => r.Left == 0  && r.Top == 50 && r.Right == 100 && r.Bottom == 100);
        Assert.Contains(region, r => r.Left == 0  && r.Top == 0  && r.Right == 50  && r.Bottom == 50);
    }

    [Fact]
    public void ComputeVisibleRegion_SkipsOverlayHwnd()
    {
        var whatsapp = new IntPtr(10);
        var overlay  = new IntPtr(20);
        var rect     = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetRootWindow(overlay).Returns(overlay);
        _win32.GetPreviousWindow(whatsapp).Returns(overlay); // overlay is directly above
        _win32.GetPreviousWindow(overlay).Returns(IntPtr.Zero);

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, overlay, _win32);

        // Overlay should be skipped — full region visible
        Assert.Single(region);
    }

    [Fact]
    public void ComputeVisibleRegion_SkipsMinimizedCoveringWindows()
    {
        var whatsapp = new IntPtr(10);
        var chrome   = new IntPtr(20);
        var rect     = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(chrome);
        _win32.GetPreviousWindow(chrome).Returns(IntPtr.Zero);
        _win32.IsWindowVisible(chrome).Returns(true);
        _win32.IsIconic(chrome).Returns(true); // minimized → should be skipped

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.Single(region); // still fully visible
    }

    [Fact]
    public void ComputeVisibleRegion_SkipsInvisibleCoveringWindows()
    {
        var whatsapp = new IntPtr(10);
        var chrome   = new IntPtr(20);
        var rect     = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(chrome);
        _win32.GetPreviousWindow(chrome).Returns(IntPtr.Zero);
        _win32.IsWindowVisible(chrome).Returns(false); // invisible → should be skipped

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.Single(region); // still fully visible
    }

    [Fact]
    public void ComputeVisibleRegion_SkipsSameProcessWindows()
    {
        // WhatsApp's own internal HWNDs (e.g. InputNonClientPointerSource) sit above
        // the main HWND in Z-order and cover the title bar area — they must be ignored.
        var whatsapp = new IntPtr(10);
        var internalHwnd = new IntPtr(11); // same process as WhatsApp
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(internalHwnd);
        _win32.GetPreviousWindow(internalHwnd).Returns(IntPtr.Zero);
        _win32.GetWindowProcessId(whatsapp).Returns(42u);
        _win32.GetWindowProcessId(internalHwnd).Returns(42u); // same PID → skip
        _win32.IsWindowVisible(internalHwnd).Returns(true);
        _win32.IsIconic(internalHwnd).Returns(false);
        RECT dummy = default;
        _win32.GetWindowRect(internalHwnd, out dummy)
              .Returns(x => { x[1] = rect; return true; }); // fully covers

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.Single(region); // internal HWND skipped → still fully visible
    }

    [Fact]
    public void ComputeVisibleRegion_SkipsToolWindows()
    {
        // System UI windows (taskbar strips, tray popups) have WS_EX_TOOLWINDOW and
        // should not be counted as covering apps even when they overlap WhatsApp.
        var whatsapp = new IntPtr(10);
        var taskbarStrip = new IntPtr(20);
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetPreviousWindow(whatsapp).Returns(taskbarStrip);
        _win32.GetPreviousWindow(taskbarStrip).Returns(IntPtr.Zero);
        _win32.GetWindowProcessId(whatsapp).Returns(1u);
        _win32.GetWindowProcessId(taskbarStrip).Returns(2u); // different PID
        _win32.GetWindowExStyle(taskbarStrip).Returns(0x80); // WS_EX_TOOLWINDOW
        _win32.IsWindowVisible(taskbarStrip).Returns(true);
        _win32.IsIconic(taskbarStrip).Returns(false);
        RECT dummy = default;
        _win32.GetWindowRect(taskbarStrip, out dummy)
              .Returns(x => { x[1] = rect; return true; }); // fully covers

        var region = WindowTracker.ComputeVisibleRegion(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.Single(region); // toolwindow skipped → still fully visible
    }

    [Fact]
    public async Task VisibleRegionChanged_FiresOnlyOnRegionTransitions()
    {
        var hwnd   = new IntPtr(42);
        var chrome = new IntPtr(99);
        var whatsappRect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var regionEvents = new List<IReadOnlyList<Rect>>();
        SetupHandleAndRect(hwnd, whatsappRect);

        _win32.GetRootWindow(hwnd).Returns(hwnd);

        // Initially nothing is above WhatsApp
        var topAbove = IntPtr.Zero;
        _win32.GetPreviousWindow(hwnd).Returns(_ => topAbove);
        _win32.GetPreviousWindow(chrome).Returns(IntPtr.Zero);
        _win32.IsWindowVisible(chrome).Returns(true);
        _win32.IsIconic(chrome).Returns(false);
        RECT coverRect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        RECT dummy = default;
        _win32.GetWindowRect(chrome, out dummy)
              .Returns(x => { x[1] = coverRect; return true; });

        _tracker = CreateTracker();
        _tracker.VisibleRegionChanged += (_, r) => regionEvents.Add(r);

        _tracker.Start();
        await Task.Delay(60); // sees full region

        topAbove = chrome; // chrome now above WhatsApp
        await Task.Delay(60); // sees empty region → fires

        topAbove = IntPtr.Zero; // chrome gone
        await Task.Delay(60); // sees full region again → fires

        _tracker.Stop();

        // Three transitions: initial detection (null → full), occlusion (full → empty), clear (empty → full).
        Assert.Equal(3, regionEvents.Count);
        Assert.Single(regionEvents[0]);  // initial: window first seen, fully visible
        Assert.Empty(regionEvents[1]);   // chrome appears → fully occluded
        Assert.Single(regionEvents[2]);  // chrome gone → fully visible again
    }

    [Fact]
    public async Task Stop_HaltsPolling_NoFurtherEventsFireAfterStop()
    {
        var hwnd = new IntPtr(55);
        _profile.FindWindowHandle().Returns(hwnd);
        _win32.GetDpiForWindow(hwnd).Returns(96u);

        RECT currentRect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        RECT dummy = default;
        _win32.GetWindowRect(hwnd, out dummy)
              .Returns(x => { x[1] = currentRect; return true; });

        _tracker = CreateTracker();
        _tracker.BoundsChanged += (_, r) => _receivedBounds.Add(r);
        _tracker.Start();
        await Task.Delay(60);

        _tracker.Stop();
        await Task.Delay(30); // drain any in-flight poll
        var snapCount = _receivedBounds.Count;

        currentRect = new RECT { Left = 999, Top = 999, Right = 1000, Bottom = 1000 };
        await Task.Delay(60);

        Assert.Equal(snapCount, _receivedBounds.Count);
        Assert.False(_tracker.IsTracking);
    }

    public void Dispose() => _tracker?.Dispose();
}
