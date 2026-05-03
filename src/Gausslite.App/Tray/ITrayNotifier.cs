namespace Gausslite.App.Tray;

internal enum NotificationIcon { Info, Warning }

internal interface ITrayNotifier
{
    void ShowBalloon(string title, string message, NotificationIcon icon);
}
