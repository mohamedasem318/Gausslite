using NSubstitute;
using System.Windows;
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.Tests.WindowTracking;

public sealed class WindowTrackerTests : IDisposable
{
    private readonly IWin32Api _win32 = Substitute.For<IWin32Api>();
    private readonly List<Rect> _receivedBounds = new();
    private WindowTracker? _tracker;

    private WindowTracker CreateTracker() =>
        new WindowTracker(_win32, TimeSpan.FromMilliseconds(10));

    private void NoHandle() =>
        _win32.FindWhatsAppWindowHandle().Returns(IntPtr.Zero);

    private void SetupHandleAndRect(IntPtr hwnd, RECT rect, uint dpi = 96)
    {
        _win32.FindWhatsAppWindowHandle().Returns(hwnd);
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
        _win32.FindWhatsAppWindowHandle().Returns(hwnd);
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

    [Fact]
    public void IsOccludedAtCenter_ReturnsFalse_WhenCenterBelongsToWhatsApp()
    {
        var whatsapp = new IntPtr(10);
        var whatsappChild = new IntPtr(11);
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.WindowFromPoint(Arg.Is<POINT>(p => p.X == 50 && p.Y == 50)).Returns(whatsappChild);
        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetRootWindow(whatsappChild).Returns(whatsapp);

        var isOccluded = WindowTracker.IsOccludedAtCenter(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.False(isOccluded);
    }

    [Fact]
    public void IsOccludedAtCenter_ReturnsTrue_WhenCenterBelongsToAnotherWindow()
    {
        var whatsapp = new IntPtr(10);
        var chrome = new IntPtr(20);
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.WindowFromPoint(Arg.Any<POINT>()).Returns(chrome);
        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetRootWindow(chrome).Returns(chrome);

        var isOccluded = WindowTracker.IsOccludedAtCenter(rect, whatsapp, IntPtr.Zero, _win32);

        Assert.True(isOccluded);
    }

    [Fact]
    public void IsOccludedAtCenter_SkipsOverlayWindowAndChecksNextWindowDown()
    {
        var whatsapp = new IntPtr(10);
        var overlay = new IntPtr(20);
        var overlayChild = new IntPtr(21);
        var rect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };

        _win32.WindowFromPoint(Arg.Any<POINT>()).Returns(overlayChild);
        _win32.GetRootWindow(whatsapp).Returns(whatsapp);
        _win32.GetRootWindow(overlay).Returns(overlay);
        _win32.GetRootWindow(overlayChild).Returns(overlay);
        _win32.GetNextWindow(overlay).Returns(whatsapp);

        var isOccluded = WindowTracker.IsOccludedAtCenter(rect, whatsapp, overlay, _win32);

        Assert.False(isOccluded);
    }

    [Fact]
    public async Task OcclusionChanged_FiresOnlyOnOcclusionTransitions()
    {
        var hwnd = new IntPtr(42);
        var chrome = new IntPtr(99);
        var topWindow = hwnd;
        var occlusionEvents = new List<bool>();
        SetupHandleAndRect(hwnd, new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 });

        _win32.WindowFromPoint(Arg.Any<POINT>()).Returns(_ => topWindow);
        _win32.GetRootWindow(hwnd).Returns(hwnd);
        _win32.GetRootWindow(chrome).Returns(chrome);

        _tracker = CreateTracker();
        _tracker.OcclusionChanged += (_, isOccluded) => occlusionEvents.Add(isOccluded);

        _tracker.Start();
        await Task.Delay(60);

        topWindow = chrome;
        await Task.Delay(60);

        topWindow = hwnd;
        await Task.Delay(60);

        _tracker.Stop();

        Assert.Equal(new[] { true, false }, occlusionEvents);
    }

    [Fact]
    public async Task Stop_HaltsPolling_NoFurtherEventsFireAfterStop()
    {
        var hwnd = new IntPtr(55);
        _win32.FindWhatsAppWindowHandle().Returns(hwnd);
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
