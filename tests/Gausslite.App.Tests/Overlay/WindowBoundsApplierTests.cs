using System.Windows;
using System.Windows.Interop;
using Gausslite.Overlay;
using Gausslite.Overlay.Interop;

namespace Gausslite.App.Tests.Overlay;

public sealed class WindowBoundsApplierTests
{
    [Fact]
    public void Apply_WithLargeNegativeBounds_DoesNotClampWpfDimensions()
    {
        RunOnSta(() =>
        {
            var native = new RecordingNativeWindowPositionApi();
            var sut = new WindowBoundsApplier(native);
            var window = new Window();
            var bounds = new Rect(-8, -8, 1936, 1048);

            try
            {
                sut.Apply(window, bounds, "test");

                Assert.Equal(bounds.Left, window.Left);
                Assert.Equal(bounds.Top, window.Top);
                Assert.Equal(bounds.Width, window.Width);
                Assert.Equal(bounds.Height, window.Height);
                Assert.Equal(double.PositiveInfinity, window.MaxWidth);
                Assert.Equal(double.PositiveInfinity, window.MaxHeight);
                Assert.Null(native.LastCall);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Apply_WithHwnd_UsesSetWindowPosWithRequestedBounds()
    {
        RunOnSta(() =>
        {
            var native = new RecordingNativeWindowPositionApi();
            var sut = new WindowBoundsApplier(native);
            var window = new Window();
            var bounds = new Rect(0, 0, 1920, 1032);

            try
            {
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                var source = PresentationSource.FromVisual(window);
                var transform = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;

                sut.Apply(window, bounds, "test");

                Assert.NotNull(native.LastCall);
                Assert.Equal(hwnd, native.LastCall.Value.Hwnd);
                Assert.Equal(ToNativePixel(bounds.Left, transform.M11), native.LastCall.Value.X);
                Assert.Equal(ToNativePixel(bounds.Top, transform.M22), native.LastCall.Value.Y);
                Assert.Equal(ToNativePixel(bounds.Width, transform.M11), native.LastCall.Value.Width);
                Assert.Equal(ToNativePixel(bounds.Height, transform.M22), native.LastCall.Value.Height);
                Assert.Equal(NativeWindow.SWP_NOACTIVATE | NativeWindow.SWP_NOZORDER, native.LastCall.Value.Flags);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static int ToNativePixel(double value, double scale)
        => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    private sealed class RecordingNativeWindowPositionApi : INativeWindowPositionApi
    {
        public SetWindowPosCall? LastCall { get; private set; }

        public bool SetWindowPos(IntPtr hwnd, int x, int y, int width, int height, uint flags)
        {
            LastCall = new SetWindowPosCall(hwnd, x, y, width, height, flags);
            return true;
        }
    }

    private readonly record struct SetWindowPosCall(
        IntPtr Hwnd,
        int X,
        int Y,
        int Width,
        int Height,
        uint Flags);

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
