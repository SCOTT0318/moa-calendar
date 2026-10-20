using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
namespace Gather;
public partial class NotificationWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly Action open;
    public NotificationWindow(string title, string body, Action open)
    {
        InitializeComponent(); this.open = open; NotificationTitle.Text = title; NotificationBody.Text = body;
        timer.Tick += (_, _) => Close(); Closed += (_, _) => timer.Stop();
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = Math.Max(area.Left, area.Right - ActualWidth - 12); Top = Math.Max(area.Top, area.Bottom - ActualHeight - 12); timer.Start(); };
    }
    private void Pause(object sender, MouseEventArgs e) => timer.Stop();
    private void Resume(object sender, MouseEventArgs e) => timer.Start();
    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
    private void Open_Click(object sender, RoutedEventArgs e) { open(); Close(); }
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
}
