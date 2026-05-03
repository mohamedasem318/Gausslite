// SPDX-License-Identifier: AGPL-3.0-or-later
using NSubstitute;
using System.Windows;
using Gausslite.Core.AppProfiles;
using Gausslite.Core.WindowTracking;

namespace Gausslite.Core.Tests.WindowTracking;

public sealed class WindowTrackerMinimizedTests : IDisposable
{
    private readonly IWin32Api _win32 = Substitute.For<IWin32Api>();
    private readonly IAppProfile _profile = Substitute.For<IAppProfile>();
    private readonly List<bool> _minimizedEvents = new();
    private readonly List<Rect> _boundsEvents = new();
    private WindowTracker? _tracker;

    [Fact]
    public async Task MinimizeThenRestore_FiresMinimizedChangedAndSuppressesBoundsWhileMinimized()
    {
        var hwnd = new IntPtr(123);
        var isMinimized = false;
        var currentRect = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 100 };
        var minimizedRect = new Rect(-32000, -32000, 160, 90);
        var restoredRect = new RECT { Left = 25, Top = 35, Right = 225, Bottom = 185 };

        _profile.FindWindowHandle().Returns(hwnd);
        _win32.IsIconic(hwnd).Returns(_ => isMinimized);
        _win32.GetDpiForWindow(hwnd).Returns(96u);

        RECT dummy = default;
        _win32.GetWindowRect(hwnd, out dummy)
              .Returns(x =>
              {
                  x[1] = currentRect;
                  return true;
              });

        _tracker = new WindowTracker(_win32, _profile, TimeSpan.FromMilliseconds(10));
        _tracker.MinimizedChanged += (_, minimized) => _minimizedEvents.Add(minimized);
        _tracker.BoundsChanged += (_, bounds) => _boundsEvents.Add(bounds);

        _tracker.Start();
        await Task.Delay(60);

        currentRect = new RECT { Left = -32000, Top = -32000, Right = -31840, Bottom = -31910 };
        isMinimized = true;
        await Task.Delay(60);

        currentRect = restoredRect;
        isMinimized = false;
        await Task.Delay(80);

        _tracker.Stop();

        Assert.Equal(new[] { true, false }, _minimizedEvents);
        Assert.Contains(_boundsEvents, bounds => bounds == new Rect(0, 0, 100, 100));
        Assert.Contains(_boundsEvents, bounds => bounds == new Rect(25, 35, 200, 150));
        Assert.DoesNotContain(_boundsEvents, bounds => bounds == minimizedRect);
    }

    public void Dispose() => _tracker?.Dispose();
}
