using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Gather.Core;

namespace Gather;
public partial class DesktopWidget : Window
{
    private readonly Action open;
    private readonly Action<bool> pin;
    internal int EventCount { get; private set; }
    public DesktopWidget(Action open, bool topmost, Action<bool> pin)
    {
        this.open = open; this.pin = pin;
        InitializeComponent();
        PinOnTop.IsChecked = topmost; Topmost = topmost;
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 20); Top = area.Top + 24;
    }
    public void Refresh(IEnumerable<CalendarEvent> events, int unread, bool hasMail)
    {
        DateLabel.Text = DateTime.Today.ToString("M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"));
        MailCount.Text = hasMail ? $"안 읽은 메일 {unread}통 · 최근 조회 기준" : "메일 계정을 연결해 주세요";
        Events.Children.Clear();
        var today = events.OrderByDescending(x => x.IsAllDay).ThenBy(x => x.Start).ToList();
        EventCount = today.Count;
        if (today.Count == 0) Events.Children.Add(new TextBlock { Text = "오늘 일정이 없습니다.", Margin = new Thickness(0, 14, 0, 0) });
        foreach (var item in today)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = item.TimeLabel, Foreground = Brushes.SteelBlue, FontSize = 12 });
            content.Children.Add(new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            Events.Children.Add(new Border { Child = content, Background = Brushes.White, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) });
        }
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock || e.OriginalSource is Grid) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Open_Click(object sender, RoutedEventArgs e) => open();
    private void Pin_Changed(object sender, RoutedEventArgs e) { Topmost = PinOnTop.IsChecked == true; pin(Topmost); }
}
