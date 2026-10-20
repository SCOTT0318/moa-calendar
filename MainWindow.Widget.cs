using System.Windows;
using System.Windows.Threading;
namespace Gather;
public partial class MainWindow
{
    private DesktopWidget? desktopWidget;
    private DispatcherTimer? widgetTimer;
    private void InitializeWidget()
    {
        if (!smokeMode)
        {
            Loaded += (_, _) => { if (preferences.WidgetEnabled) ShowDesktopWidget(); };
            widgetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            widgetTimer.Tick += (_, _) => RefreshDesktopWidget(); widgetTimer.Start();
        }
        Closed += (_, _) => { shuttingDown = true; widgetTimer?.Stop(); desktopWidget?.Close(); };
    }
    private void Widget_Click(object sender, RoutedEventArgs e) => ShowDesktopWidget();
    private void ShowDesktopWidget()
    {
        if (desktopWidget != null) { desktopWidget.Activate(); return; }
        preferences.WidgetEnabled = true; SavePreferences();
        desktopWidget = new DesktopWidget(() => { ShowMainWindow(); ShowPage("calendar"); Today_Click(this, new RoutedEventArgs()); }, preferences.WidgetTopmost,
            value => { preferences.WidgetTopmost = value; SavePreferences(); });
        desktopWidget.Closed += (_, _) => { desktopWidget = null; if (!shuttingDown) { preferences.WidgetEnabled = false; SavePreferences(); } };
        RefreshDesktopWidget(); desktopWidget.Show();
    }
    private void RefreshDesktopWidget() => desktopWidget?.Refresh(CalendarEventsOn(DateTime.Today), mailSnapshots.Values.SelectMany(x => x.Messages).Count(x => !x.IsRead), HasRealMail);
}
