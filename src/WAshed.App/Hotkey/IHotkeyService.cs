namespace WAshed.App.Hotkey;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
}
