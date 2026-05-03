using Hardcodet.Wpf.TaskbarNotification;

namespace Gausslite.App.Tray;

/// <summary>
/// Wraps a <see cref="TaskbarIcon"/> to show system-tray balloon notifications.
/// Call <see cref="Attach"/> once the icon is initialized; <see cref="ShowBalloon"/>
/// is a no-op before that.
/// </summary>
internal sealed class TrayNotifier : ITrayNotifier
{
    private TaskbarIcon? _icon;

    public void Attach(TaskbarIcon icon) => _icon = icon;

    public void ShowBalloon(string title, string message, NotificationIcon icon)
    {
        var balloonIcon = icon == NotificationIcon.Warning ? BalloonIcon.Warning : BalloonIcon.Info;
        _icon?.ShowBalloonTip(title, message, balloonIcon);
    }
}
