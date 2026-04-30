using System.Windows;
using System.Windows.Interop;
using WAshed.Core.Blur;
using WAshed.Overlay;
using WAshed.Overlay.Interop;

namespace WAshed.App.Tests.Overlay;

public sealed class OverlayWindowTests
{
    [Fact]
    public void ShowOffscreen_CreatesVisibleWindowAtParkCoordinates()
    {
        RunOnSta(() =>
        {
            var boundsApplier = new RecordingWindowBoundsApplier();
            var sut = new OverlayWindow(new NoOpD3DImageBridge(), boundsApplier);
            var initialBounds = new Rect(10, 20, 800, 600);

            try
            {
                sut.ShowOffscreen(initialBounds);

                Assert.NotEqual(IntPtr.Zero, sut.WindowHandle);
                Assert.NotEmpty(boundsApplier.Calls);
                Assert.All(boundsApplier.Calls, call => Assert.NotEqual(Visibility.Hidden, call.Visibility));
                Assert.Equal(OverlayWindow.OffscreenParkX, boundsApplier.Calls[^1].Bounds.Left);
                Assert.Equal(OverlayWindow.OffscreenParkY, boundsApplier.Calls[^1].Bounds.Top);
                Assert.Equal(initialBounds.Width, boundsApplier.Calls[^1].Bounds.Width);
                Assert.Equal(initialBounds.Height, boundsApplier.Calls[^1].Bounds.Height);
            }
            finally
            {
                sut.Dispose();
            }
        });
    }

    [Fact]
    public void MoveToBounds_MovesVisibleWindowToRequestedBounds()
    {
        RunOnSta(() =>
        {
            var boundsApplier = new RecordingWindowBoundsApplier();
            var sut = new OverlayWindow(new NoOpD3DImageBridge(), boundsApplier);
            var initialBounds = new Rect(10, 20, 800, 600);
            var restoredBounds = new Rect(30, 40, 1024, 768);

            try
            {
                sut.ShowOffscreen(initialBounds);
                boundsApplier.Calls.Clear();

                sut.MoveToBounds(restoredBounds);

                Assert.Single(boundsApplier.Calls);
                Assert.Equal(Visibility.Visible, boundsApplier.Calls[0].Visibility);
                Assert.Equal(restoredBounds, boundsApplier.Calls[0].Bounds);
            }
            finally
            {
                sut.Dispose();
            }
        });
    }

    [Fact]
    public void MoveOffscreen_MovesVisibleWindowBackToParkCoordinates()
    {
        RunOnSta(() =>
        {
            var boundsApplier = new RecordingWindowBoundsApplier();
            var sut = new OverlayWindow(new NoOpD3DImageBridge(), boundsApplier);
            var activeBounds = new Rect(30, 40, 1024, 768);

            try
            {
                sut.ShowOffscreen(activeBounds);
                sut.MoveToBounds(activeBounds);
                boundsApplier.Calls.Clear();

                sut.MoveOffscreen();

                Assert.Single(boundsApplier.Calls);
                Assert.Equal(Visibility.Visible, boundsApplier.Calls[0].Visibility);
                Assert.Equal(OverlayWindow.OffscreenParkX, boundsApplier.Calls[0].Bounds.Left);
                Assert.Equal(OverlayWindow.OffscreenParkY, boundsApplier.Calls[0].Bounds.Top);
                Assert.Equal(activeBounds.Width, boundsApplier.Calls[0].Bounds.Width);
                Assert.Equal(activeBounds.Height, boundsApplier.Calls[0].Bounds.Height);
            }
            finally
            {
                sut.Dispose();
            }
        });
    }

    private sealed class RecordingWindowBoundsApplier : IWindowBoundsApplier
    {
        public List<BoundsCall> Calls { get; } = [];

        public void Apply(Window window, Rect bounds, string reason)
        {
            Calls.Add(new BoundsCall(bounds, window.Visibility, reason));
        }
    }

    private sealed class NoOpD3DImageBridge : ID3DImageBridge
    {
        public void UpdateD3DImage(D3DImage d3dImage, IBlurRenderTarget blurTarget) { }

        public void Dispose() { }
    }

    private readonly record struct BoundsCall(Rect Bounds, Visibility Visibility, string Reason);

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
