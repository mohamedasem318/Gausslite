using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Gausslite.App.Hotkey;

/// <summary>
/// Registers a global Ctrl+Shift+B hotkey via RegisterHotKey and delivers
/// <see cref="HotkeyPressed"/> events on the WPF UI thread via a hidden
/// HWND_MESSAGE window backed by <see cref="HwndSource"/>.
/// </summary>
internal sealed class HotkeyService : IHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkB = 0x42;
    private const int HotkeyId = 9001;

    private HwndSource? _hwndSource;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("Gausslite_HotkeyReceiver")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE — message-only window
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
        RegisterHotKey(_hwndSource.Handle, HotkeyId, ModControl | ModShift, VkB);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwndSource is not null)
        {
            UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
