namespace Gausslite.App.Hotkey;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
}
