using Gausslite.Core.ScreenShare;
using Gausslite.Core.WindowTracking;
using NSubstitute;

namespace Gausslite.Core.Tests.ScreenShare;

public sealed class WindowSignalScreenShareDetectorTests
{
    // Synthetic signature: matches any window whose className == "FakeShareToolbar".
    private static readonly ShareSignature FakeSignature = new()
    {
        AppName = "FakeApp",
        ProcessNameMatches = _ => true,
        ClassNameMatches = c => c == "FakeShareToolbar",
        TitleMatches = _ => true,
    };

    private readonly IWin32Api _win32 = Substitute.For<IWin32Api>();
    private readonly List<Action> _scheduledTicks = new();

    /// <summary>Test scheduler: captures the tick action so tests can drive it manually.</summary>
    private IDisposable CaptureScheduler(TimeSpan _, Action onTick)
    {
        _scheduledTicks.Add(onTick);
        return Substitute.For<IDisposable>();
    }

    private void FireTick() => _scheduledTicks[0]();

    private WindowSignalScreenShareDetector CreateSut(IReadOnlyList<ShareSignature>? sigs = null) =>
        new(_win32,
            sigs ?? new[] { FakeSignature },
            CaptureScheduler,
            TimeSpan.FromMilliseconds(1));

    private static WindowInfo Window(string className, string title = "", string proc = "fake.exe", IntPtr? hwnd = null) =>
        new(hwnd ?? new IntPtr(0xABCD), 1234u, proc, className, title);

    [Fact]
    public void InitialState_IsIdle_WithNoEvidence()
    {
        var sut = CreateSut();
        Assert.Equal(ScreenShareState.Idle, sut.CurrentState);
        Assert.Null(sut.CurrentEvidence);
    }

    [Fact]
    public void Start_IsIdempotent()
    {
        _win32.EnumerateVisibleWindows().Returns(Array.Empty<WindowInfo>());

        using var sut = CreateSut();
        sut.Start();
        sut.Start(); // second call should not double-schedule

        Assert.Single(_scheduledTicks);
    }

    [Fact]
    public void Poll_NoMatchingWindow_StaysIdle_NoEventFires()
    {
        _win32.EnumerateVisibleWindows().Returns(new[]
        {
            Window("RandomClass1"),
            Window("RandomClass2"),
        });

        using var sut = CreateSut();
        var events = new List<ScreenShareState>();
        sut.StateChanged += (_, s) => events.Add(s);

        sut.Start();
        FireTick();

        Assert.Equal(ScreenShareState.Idle, sut.CurrentState);
        Assert.Empty(events);
        Assert.Null(sut.CurrentEvidence);
    }

    [Fact]
    public void Poll_MatchingWindow_TransitionsToActive_FiresEventOnce()
    {
        _win32.EnumerateVisibleWindows().Returns(new[]
        {
            Window("OtherClass"),
            Window("FakeShareToolbar", title: "Sharing controls"),
        });

        using var sut = CreateSut();
        var events = new List<ScreenShareState>();
        sut.StateChanged += (_, s) => events.Add(s);

        sut.Start();
        FireTick();

        Assert.Equal(ScreenShareState.Active, sut.CurrentState);
        Assert.Single(events);
        Assert.Equal(ScreenShareState.Active, events[0]);
        Assert.NotNull(sut.CurrentEvidence);
        Assert.Equal("FakeApp", sut.CurrentEvidence!.Value.AppName);
        Assert.Equal("FakeShareToolbar", sut.CurrentEvidence.Value.WindowClass);
    }

    [Fact]
    public void Poll_StableActive_DoesNotRefireEvent()
    {
        _win32.EnumerateVisibleWindows().Returns(new[] { Window("FakeShareToolbar") });

        using var sut = CreateSut();
        var events = new List<ScreenShareState>();
        sut.StateChanged += (_, s) => events.Add(s);

        sut.Start();
        FireTick();
        FireTick();
        FireTick();

        Assert.Single(events); // still only the initial Idle→Active transition
    }

    [Fact]
    public void Poll_ActiveThenGone_FiresIdleTransition()
    {
        var calls = 0;
        _win32.EnumerateVisibleWindows().Returns(_ =>
        {
            calls++;
            return calls == 1
                ? new[] { Window("FakeShareToolbar") }
                : Array.Empty<WindowInfo>();
        });

        using var sut = CreateSut();
        var events = new List<ScreenShareState>();
        sut.StateChanged += (_, s) => events.Add(s);

        sut.Start();
        FireTick(); // Active
        FireTick(); // Idle

        Assert.Equal(ScreenShareState.Idle, sut.CurrentState);
        Assert.Equal(2, events.Count);
        Assert.Equal(ScreenShareState.Active, events[0]);
        Assert.Equal(ScreenShareState.Idle, events[1]);
        Assert.Null(sut.CurrentEvidence);
    }

    [Fact]
    public void EmptySignatureSet_AlwaysIdle()
    {
        _win32.EnumerateVisibleWindows().Returns(new[] { Window("FakeShareToolbar") });

        using var sut = CreateSut(Array.Empty<ShareSignature>());
        var events = new List<ScreenShareState>();
        sut.StateChanged += (_, s) => events.Add(s);

        sut.Start();
        FireTick();

        Assert.Equal(ScreenShareState.Idle, sut.CurrentState);
        Assert.Empty(events);
    }

    [Fact]
    public void Stop_DisposesScheduledTicker()
    {
        _win32.EnumerateVisibleWindows().Returns(Array.Empty<WindowInfo>());

        var disposable = Substitute.For<IDisposable>();
        IDisposable Scheduler(TimeSpan _, Action a) { _scheduledTicks.Add(a); return disposable; }

        var sut = new WindowSignalScreenShareDetector(_win32, new[] { FakeSignature }, Scheduler, TimeSpan.FromMilliseconds(1));
        sut.Start();
        sut.Stop();

        disposable.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_AfterStart_StopsTicker()
    {
        _win32.EnumerateVisibleWindows().Returns(Array.Empty<WindowInfo>());

        var disposable = Substitute.For<IDisposable>();
        IDisposable Scheduler(TimeSpan _, Action a) { _scheduledTicks.Add(a); return disposable; }

        var sut = new WindowSignalScreenShareDetector(_win32, new[] { FakeSignature }, Scheduler, TimeSpan.FromMilliseconds(1));
        sut.Start();
        sut.Dispose();

        disposable.Received(1).Dispose();
    }

    [Fact]
    public void Start_AfterDispose_Throws()
    {
        var sut = CreateSut();
        sut.Dispose();
        Assert.Throws<ObjectDisposedException>(() => sut.Start());
    }
}
