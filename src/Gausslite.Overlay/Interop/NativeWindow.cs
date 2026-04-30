using System.Runtime.InteropServices;

namespace Gausslite.Overlay.Interop;

internal static class NativeWindow
{
    public const int GWL_EXSTYLE = -20;

    /// <summary>Required to receive WM_PAINT messages even when click-through is active.</summary>
    public const int WS_EX_LAYERED = 0x00080000;

    /// <summary>Makes the window pass all mouse/keyboard input through to windows beneath it.</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Hides the window from the taskbar and Alt-Tab switcher.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();
}
