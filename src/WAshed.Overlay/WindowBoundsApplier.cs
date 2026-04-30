using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WAshed.Core.Diagnostics;
using WAshed.Overlay.Interop;

namespace WAshed.Overlay;

internal interface IWindowBoundsApplier
{
    void Apply(Window window, Rect bounds, string reason);
}

internal interface INativeWindowPositionApi
{
    bool SetWindowPos(IntPtr hwnd, int x, int y, int width, int height, uint flags);
}

internal sealed class NativeWindowPositionApi : INativeWindowPositionApi
{
    public bool SetWindowPos(IntPtr hwnd, int x, int y, int width, int height, uint flags)
        => NativeWindow.SetWindowPos(
            hwnd,
            NativeWindow.HWND_TOPMOST,
            x,
            y,
            width,
            height,
            flags);
}

internal sealed class WindowBoundsApplier : IWindowBoundsApplier
{
    private readonly INativeWindowPositionApi _native;

    public WindowBoundsApplier() : this(new NativeWindowPositionApi()) { }

    internal WindowBoundsApplier(INativeWindowPositionApi native) => _native = native;

    public void Apply(Window window, Rect bounds, string reason)
    {
        window.SizeToContent = SizeToContent.Manual;
        window.MaxWidth = double.PositiveInfinity;
        window.MaxHeight = double.PositiveInfinity;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Transparent borderless WPF windows can fail to visually honor large
        // screen-edge bounds; place the realized HWND natively as the source of truth.
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;

        int x = ToNativePixel(bounds.Left, transform.M11);
        int y = ToNativePixel(bounds.Top, transform.M22);
        int width = ToNativePixel(bounds.Width, transform.M11);
        int height = ToNativePixel(bounds.Height, transform.M22);
        const uint flags = NativeWindow.SWP_NOACTIVATE | NativeWindow.SWP_NOZORDER;

        if (_native.SetWindowPos(hwnd, x, y, width, height, flags))
        {
            DiagLog.Info($"OverlayWindow.ApplyBounds ({reason}): SetWindowPos applied X={x}, Y={y}, Width={width}, Height={height}, Scale={transform.M11:0.###}x{transform.M22:0.###}");
            return;
        }

        int error = Marshal.GetLastWin32Error();
        DiagLog.Warn($"OverlayWindow.ApplyBounds ({reason}): SetWindowPos failed with Win32 error {error}: {new Win32Exception(error).Message}");
    }

    private static int ToNativePixel(double value, double scale)
        => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}
