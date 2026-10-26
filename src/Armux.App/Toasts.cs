using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Armux.App;

/// <summary>
/// Windows toast notifications (cmux uses the macOS notification center, deviation D-04). Registered on first use, so
/// a session without notifications loads none of the notification stack. Clicking a toast brings the window forward
/// and jumps to the surface.
/// </summary>
public static class Toasts
{
    static bool registered, failed;

    /// <summary>Called with the surface id when the user clicks a toast.</summary>
    public static event Action<int>? Invoked;

    public static void ShowIfInactive(MainWindow window, int surfaceId, string workspace, string title, string body)
    {
        if (window.IsActiveWindow || !Theme.Settings.Toasts) return;
        Show(window, surfaceId, workspace, title, body);
    }

    /// <summary>Shows a toast now (Settings > Notifications > Test uses this while armux is in front).</summary>
    public static void Show(MainWindow window, int surfaceId, string workspace, string title, string body)
    {
        if (failed) return;
        try
        {
            if (!registered)
            {
                AppNotificationManager.Default.NotificationInvoked += (_, e) =>
                {
                    if (e.Arguments.TryGetValue("surface", out var id) && int.TryParse(id, out int sid)) window.DispatcherQueue.TryEnqueue(() => Invoked?.Invoke(sid));
                };
                AppNotificationManager.Default.Register();
                registered = true;
            }
            var toast = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .SetAttributionText(workspace)
                .AddArgument("surface", surfaceId.ToString())
                .BuildNotification();
            AppNotificationManager.Default.Show(toast);
        }
        catch (Exception e)
        {
            failed = true;
            App.Log(nameof(Toasts), e);
        }
    }

    /// <summary>Removes the registration made by <see cref="ShowIfInactive"/> (unpackaged apps must do this on exit).</summary>
    public static void Unregister()
    {
        if (!registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch (Exception e) { App.Log(nameof(Toasts), e); }
    }
}
