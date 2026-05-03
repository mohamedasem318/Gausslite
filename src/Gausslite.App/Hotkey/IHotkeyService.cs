// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Gausslite.App.Hotkey;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
}
