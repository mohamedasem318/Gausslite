using NSubstitute;
using System.Windows;
using WAshed.Core.WindowTracking;

namespace WAshed.Core.Tests.WindowTracking;

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
    public async Task DpiTranslation_CorrectPhysicalPixels_At150Percent()
    {
        var hwnd = new IntPtr(77);
        SetupHandleAndRect(hwnd, new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 }, dpi: 144);

        _tracker = CreateTracker();
        _tracker.Start();
        await Task.Delay(60);

        Assert.NotNull(_tracker.CurrentBounds);
        Assert.Equal(new Rect(0, 0, 150, 150), _tracker.CurrentBounds!.Value);
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
