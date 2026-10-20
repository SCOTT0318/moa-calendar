using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gather.Core;
namespace Gather;

public partial class MainWindow : Window
{
    private readonly StateStore store;
    private readonly Workspace workspace;
    private readonly bool readOnlySession;
    private string? accountId;
    private bool unreadOnly, starredOnly, ready, refreshing;
    private string page = "mail";
    private DateTime month = DateTime.Today, selectedDay = DateTime.Today;
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");
    private MailMessage? SelectedMessage => MessageList.SelectedItem as MailMessage;
    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs();
        var smoke = args.Contains("--smoke-test");
        store = new StateStore(Path.Combine(AppPaths.DataDirectory, "demo-state.json"));
        string? warning = null;
        workspace = new Workspace(smoke ? DemoFactory.Create(DateTime.Today) : store.Load(out warning));
        readOnlySession = smoke || warning != null;
        InitializeComponent();
        InitializeCalendarSync(smoke);
        InitializeNotifications(smoke);
        ProviderCards.ItemsSource = ProviderCatalog.All;
        ready = true;
        RefreshAccounts(); RefreshMessages(); RenderCalendar();
        if (!smoke) ShowPage("calendar");
        if (warning != null) StatusText.Text = warning;
        if (smoke) Loaded += (_, _) => Dispatcher.BeginInvoke(async () => await SmokeTest(args), DispatcherPriority.ApplicationIdle);
    }
    private void RefreshAccounts()
    {
        refreshing = true;
        AccountList.ItemsSource = MailAccounts().ToList();
        AccountList.SelectedItem = page == "mail" ? MailAccounts().FirstOrDefault(a => a.Id == accountId) : null;
        MailAccountHeading.Text = HasRealMail ? "내 메일 계정" : "예시 메일 계정";
        refreshing = false;
    }
    private void RefreshMessages(string? keepId = null)
    {
        if (!ready) return;
        refreshing = true;
        var messages = QueryMailMessages().ToList();
        MessageList.ItemsSource = messages;
        MessageList.SelectedItem = messages.FirstOrDefault(x => x.Id == keepId) ?? messages.FirstOrDefault();
        ResultCount.Text = $"{messages.Count}개 · {(starredOnly ? "중요 메일" : unreadOnly ? "안 읽은 메일" : "전체 메일")}";
        foreach (var (button, active) in new[] { (AllFilter, !unreadOnly && !starredOnly), (UnreadFilter, unreadOnly), (StarredFilter, starredOnly) })
        {
            button.Background = Brush(active ? "#3B69DC" : "#EDF2F8");
            button.Foreground = Brush(active ? "#FFFFFF" : "#17283F");
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "선택됨" : "선택 안 됨");
        }
        EmptyMail.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        refreshing = false;
        RenderReader();
    }
    private void RenderReader()
    {
        var m = SelectedMessage;
        ReaderSubject.Text = m?.Subject ?? "메일을 선택해 주세요";
        ReaderSender.Text = m == null ? "" : $"{m.Sender}  <{m.SenderAddress}>";
        var a = MailAccounts().FirstOrDefault(x => x.Id == m?.AccountId);
        ReaderMeta.Text = m == null ? "" : $"{a?.Label} · {a?.ProviderLabel}  |  {m.ReceivedAt.ToLocalTime():yyyy.MM.dd HH:mm}" + (a?.IsDemo == true ? " · 예시" : " · 읽기 전용");
        ReaderBody.Text = m?.Body ?? "검색 결과가 없거나 메일함이 비어 있습니다.";
        ReadButton.IsEnabled = StarButton.IsEnabled = m != null;
        ReadButton.Content = m?.IsRead == true ? "안 읽음으로 표시" : "읽음으로 표시";
        StarButton.Content = m?.IsStarred == true ? "★ 중요 해제" : "☆ 중요 표시";
    }
    private void Save(string message)
    {
        if (readOnlySession) { StatusText.Text = message + " · 임시 세션, 파일 저장 안 함"; return; }
        try { store.Save(workspace.State); StatusText.Text = message + " · 저장됨"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "저장하지 못했습니다. 폴더 쓰기 권한을 확인해 주세요. 변경은 현재 창에서만 유지됩니다."; }
    }
    private void ShowPage(string value)
    {
        page = value;
        refreshing = true;
        AccountList.SelectedItem = value == "mail" ? MailAccounts().FirstOrDefault(a => a.Id == accountId) : null;
        refreshing = false;
        MailPanel.Visibility = value == "mail" ? Visibility.Visible : Visibility.Collapsed;
        CalendarPanel.Visibility = value == "calendar" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = value == "settings" ? Visibility.Visible : Visibility.Collapsed;
        MailNotice.Visibility = value == "mail" ? Visibility.Visible : Visibility.Collapsed;
        var account = MailAccounts().FirstOrDefault(x => x.Id == accountId);
        MailNoticeText.Text = HasRealMail ? "받은 편지함의 최근 50통을 표시합니다. 읽음·중요 표시는 이 앱에서만 바뀌며 서버에 반영하지 않습니다." : "예시 메일함입니다. 설정에서 계정을 추가하면 실제 받은 편지함을 볼 수 있습니다.";
        PageTitle.Text = value switch { "calendar" => "캘린더", "settings" => "설정", _ => account?.Label ?? "통합 메일함" };
        PageSubtitle.Text = value switch { "calendar" => "내 일정과 연결한 캘린더를 함께 봅니다.", "settings" => "캘린더·메일 연결 및 저장 안내", _ => account == null ? "Gmail · 네이버 · 하이웍스" : $"{account.ProviderLabel} · {account.Address}" };
        InboxNav.Background = Brush(value == "mail" ? "#3B69DC" : "#223C5B");
        CalendarNav.Background = Brush(value == "calendar" ? "#3B69DC" : "#223C5B");
        SettingsNav.Background = Brush(value == "settings" ? "#3B69DC" : "#223C5B");
        if (value == "mail") RefreshMessages(SelectedMessage?.Id);
    }
    private void Inbox_Click(object sender, RoutedEventArgs e) { accountId = null; RefreshAccounts(); RefreshMessages(); ShowPage("mail"); }
    private void Calendar_Click(object sender, RoutedEventArgs e) { ShowPage("calendar"); RenderCalendar(); RefreshCalendarMonthIfNeeded(); }
    private void Settings_Click(object sender, RoutedEventArgs e) { ShowPage("settings"); RefreshAccounts(); }
    private void Account_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || refreshing) return;
        accountId = (AccountList.SelectedItem as Account)?.Id;
        RefreshMessages();
        ShowPage("mail");
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (ready) RefreshMessages(); }
    private void Message_Changed(object sender, SelectionChangedEventArgs e) { if (ready && !refreshing) RenderReader(); }
    private void All_Click(object sender, RoutedEventArgs e) { unreadOnly = starredOnly = false; RefreshMessages(); }
    private void Unread_Click(object sender, RoutedEventArgs e) { unreadOnly = true; starredOnly = false; RefreshMessages(); }
    private void Starred_Click(object sender, RoutedEventArgs e) { unreadOnly = false; starredOnly = true; RefreshMessages(); }
    private void Read_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMessage is not { } m) return;
        m.IsRead = !m.IsRead; RefreshMessages(m.Id); if (HasRealMail) StatusText.Text = "읽음 표시 변경 · 이 앱에서만 적용"; else Save("예시 읽음 상태 변경");
    }
    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMessage is not { } m) return;
        m.IsStarred = !m.IsStarred; RefreshMessages(m.Id); if (HasRealMail) StatusText.Text = "중요 표시 변경 · 이 앱에서만 적용"; else Save("예시 중요 표시 변경");
    }
    private void ChangeMonth(int offset)
    {
        month = new DateTime(month.Year, month.Month, 1).AddMonths(offset);
        selectedDay = month.AddDays(Math.Min(selectedDay.Day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
        RenderCalendar();
        RefreshCalendarMonthIfNeeded();
    }
    private void PreviousMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(-1);
    private void NextMonth_Click(object sender, RoutedEventArgs e) => ChangeMonth(1);
    private void SampleEvents_Changed(object sender, RoutedEventArgs e) { if (ready) RenderCalendar(); }
    private IEnumerable<CalendarEvent> CalendarEventsOn(DateTime date) => workspace.EventsOn(date)
        .Where(x => x.AccountId == Workspace.LocalCalendarId || ShowSampleEvents.IsChecked == true)
        .Concat(RemoteEventsOn(date)).OrderByDescending(x => x.IsAllDay).ThenBy(x => x.Start);
    private void Today_Click(object sender, RoutedEventArgs e) { month = selectedDay = DateTime.Today; RenderCalendar(); RefreshCalendarMonthIfNeeded(); }
    private void NewEvent_Click(object sender, RoutedEventArgs e) => EditEvent(null);
    private void EditEvent(CalendarEvent? item)
    {
        var editor = new EventEditor(workspace, selectedDay, item) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            accountId = null; RefreshAccounts();
            selectedDay = month = editor.SavedEvent!.Start.LocalDateTime.Date;
            RenderCalendar(); ShowPage("calendar"); Save("로컬 일정 저장");
            RefreshCalendarMonthIfNeeded();
        }
    }
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private void RenderCalendar()
    {
        CalendarDays.Children.Clear();
        MonthTitle.Text = month.ToString("yyyy년 M월", Korean);
        foreach (var date in Workspace.MonthDays(month))
        {
            var content = new StackPanel();
            var events = CalendarEventsOn(date).ToList();
            var dateHeader = new Grid { Margin = new Thickness(2, 2, 0, 5) };
            dateHeader.Children.Add(new TextBlock { Text = date.Day.ToString(), FontWeight = date.Date == DateTime.Today ? FontWeights.Bold : FontWeights.Normal, Foreground = Brush(date.Month != month.Month ? "#ABB5C4" : date.DayOfWeek == DayOfWeek.Sunday ? "#CB6B70" : "#263D5B") });
            if (events.Count > 0) dateHeader.Children.Add(new TextBlock { Text = events.Count.ToString(), FontSize = 10, Foreground = Brush("#42608C"), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = $"일정 {events.Count}개" });
            content.Children.Add(dateHeader);
            foreach (var ev in events.Take(2))
            {
                var a = workspace.State.Accounts.FirstOrDefault(x => x.Id == ev.AccountId);
                content.Children.Add(new TextBlock { Text = "● " + ev.Title, Foreground = Brush(ev.SourceColor ?? a?.Color ?? "#147D87"), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 2, 0, 0) });
            }
            if (events.Count > 2) content.Children.Add(new TextBlock { Text = $"+{events.Count - 2}개", FontSize = 10, Foreground = Brush("#68788E") });
            var button = new Button { Content = content, Tag = date, Padding = new Thickness(5), Margin = new Thickness(2), Background = Brush(date == selectedDay ? "#E5EEFF" : "#F7F9FC"), BorderBrush = Brush(date == selectedDay ? "#3B69DC" : date == DateTime.Today ? "#8399B7" : "#F7F9FC"), BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = date.ToString("D", Korean) };
            button.ToolTip = date.ToString("D", Korean) + (events.Count == 0 ? "" : "\n" + string.Join("\n", events.Take(20).Select(x => x.TimeLabel + " · " + x.Title)));
            System.Windows.Automation.AutomationProperties.SetName(button, date.ToString("yyyy-MM-dd") + $" 일정 {events.Count}개");
            button.Click += (_, _) => { selectedDay = month = date; RenderCalendar(); RefreshCalendarMonthIfNeeded(); };
            CalendarDays.Children.Add(button);
        }
        AgendaTitle.Text = selectedDay.ToString("M월 d일 dddd", Korean);
        AgendaItems.Children.Clear();
        var dayEvents = CalendarEventsOn(selectedDay).ToList();
        if (dayEvents.Count == 0) AgendaItems.Children.Add(new TextBlock { Text = "일정이 없습니다.\n‘새 일정’으로 추가하세요.", Foreground = Brush("#68788E"), Margin = new Thickness(0, 15, 0, 0) });
        foreach (var ev in dayEvents)
        {
            var a = workspace.State.Accounts.FirstOrDefault(x => x.Id == ev.AccountId);
            var color = ev.SourceColor ?? a?.Color ?? "#147D87";
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = ev.TimeLabel, Foreground = Brush(color), FontWeight = FontWeights.Bold, FontSize = 12 });
            stack.Children.Add(new TextBlock { Text = ev.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 8) });
            stack.Children.Add(new TextBlock { Text = (string.IsNullOrWhiteSpace(ev.Location) ? "" : ev.Location + "\n") + (ev.SourceLabel == null ? a == null ? "내 일정" : a.Label + " · 예시" : ev.SourceLabel + " · 읽기 전용"), Foreground = Brush("#68788E"), FontSize = 12 });
            if (ev.AccountId == Workspace.LocalCalendarId)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
                var edit = new Button { Content = "수정", Padding = new Thickness(10, 6, 10, 6) };
                edit.Click += (_, _) => EditEvent(ev);
                var delete = new Button { Content = "삭제", Padding = new Thickness(10, 6, 10, 6) };
                delete.Click += (_, _) => { if (MessageBox.Show(this, $"'{ev.Title}' 일정을 이 PC에서 삭제할까요?", "로컬 일정 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { workspace.DeleteLocalEvent(ev.Id); RenderCalendar(); Save("로컬 일정 삭제"); } };
                actions.Children.Add(edit); actions.Children.Add(delete); stack.Children.Add(actions);
            }
            AgendaItems.Children.Add(new Border { Child = stack, Background = Brush("#F5F8FD"), BorderBrush = Brush(color), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(13), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 14) });
        }
    }
    private async Task SmokeTest(string[] args)
    {
        var i = Array.IndexOf(args, "--smoke-test");
        var output = i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : Path.Combine(AppContext.BaseDirectory, "Smoke");
        var checks = new List<string>();
        var skipped = new List<string>();
        try
        {
            Directory.CreateDirectory(output);
            void Check(bool success, string name) { if (!success) throw new InvalidOperationException(name); checks.Add(name); }
            async Task Capture(string name, FrameworkElement? target = null) { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); var root = target ?? (FrameworkElement)Content; var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file); }
            ShowPage("mail");
            Check(MessageList.Items.Count == 8 && ReaderBody.Text.Contains("가상의 데모"), "startup inbox and reader");
            await Capture("01-inbox");
            AccountList.SelectedIndex = 1; Check(MessageList.Items.Count == 3, "account filter");
            InboxNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SearchBox.Text = "북클럽"; Check(MessageList.Items.Count == 1, "search sender");
            SearchBox.Text = "no-match-xyz"; Check(MessageList.Items.Count == 0 && !ReadButton.IsEnabled, "empty search");
            SearchBox.Clear();
            var selected = SelectedMessage!; var oldRead = selected.IsRead; ReadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(selected.IsRead != oldRead, "read toggle");
            var oldStar = selected.IsStarred; StarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(selected.IsStarred != oldStar, "star toggle");
            CalendarNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(CalendarDays.Children.Count == 42 && !CalendarEventsOn(selectedDay).Any(), "calendar hides sample events by default");
            ShowSampleEvents.IsChecked = true;
            Check(AgendaItems.Children.Count == 3, "optional sample events");
            await Capture("02-calendar");
            var editor = new EventEditor(workspace, selectedDay) { Owner = this };
            editor.Loaded += (_, _) => { editor.TitleInput.Text = "로컬 테스트 일정"; editor.SaveEventButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            Check(editor.ShowDialog() == true && editor.SavedEvent != null, "local event editor create");
            var localEvent = editor.SavedEvent!;
            RenderCalendar(); Check(AgendaItems.Children.Count == 4, "local event in agenda");
            accountId = "naver-demo";
            ShowSampleEvents.IsChecked = false;
            Check(CalendarEventsOn(selectedDay).Single().Id == localEvent.Id, "mail account filter cannot hide local events");
            await Capture("04-local-event");
            Width = MinWidth; Height = MinHeight;
            await Capture("05-compact-calendar");
            if (ActualWidth + 1 < MinWidth || ((FrameworkElement)Content).ActualWidth < MinWidth - 32)
                skipped.Add("minimum window calendar layout: available desktop is smaller than supported 1120px width");
            else Check(MonthTitle.ActualWidth > 0 && NewEventButton.ActualWidth > 0
                && MonthTitle.TranslatePoint(new Point(MonthTitle.ActualWidth, 0), CalendarPanel).X <= NewEventButton.TranslatePoint(new Point(0, 0), CalendarPanel).X
                && CalendarDays.ActualWidth / 7 > 65, "minimum window calendar layout");
            ShowPage("mail");
            await Capture("06-compact-inbox");
            Check(ReaderBody.ActualWidth > 250 && ReaderBody.ActualHeight > 100, "minimum window mail reader layout");
            ShowPage("calendar");
            var remoteDay = new DateTimeOffset(selectedDay.Date);
            calendarSnapshots["google-ui-test"] = new CalendarSnapshot([], [new CalendarEvent("remote-ui-google", "google-ui-test", "Google 일정 · 검증용", remoteDay, remoteDay.AddDays(2), "") { IsAllDay = true, SourceLabel = "Google · 테스트 캘린더", SourceColor = "#4979E7" }]);
            calendarSnapshots["naver-ui-test"] = new CalendarSnapshot([], [new CalendarEvent("remote-ui-naver", "naver-ui-test", "네이버 일정 · 검증용", remoteDay.AddHours(14), remoteDay.AddHours(15), "온라인") { SourceLabel = "네이버 · 테스트 캘린더", SourceColor = "#1F9B69" }]);
            RenderCalendar();
            Check(CalendarEventsOn(selectedDay).Count() == 3 && CalendarEventsOn(selectedDay.AddDays(1)).Any(x => x.Id == "remote-ui-google"), "remote calendar events show independently of sample and mail filters");
            Check(AgendaItems.Children.Cast<Border>().Where(b => ((StackPanel)b.Child).Children.OfType<TextBlock>().Any(t => t.Text.Contains("읽기 전용"))).All(b => !((StackPanel)b.Child).Children.OfType<StackPanel>().Any()), "remote events have no edit or delete actions");
            Check(!JsonSerializer.Serialize(workspace.State).Contains("remote-ui-google"), "remote events never enter plaintext local calendar store");
            await Capture("08-connected-calendar-test-data");
            calendarSnapshots.Clear(); RenderCalendar();
            Width = 1440; Height = 900;
            ShowSampleEvents.IsChecked = true;
            var editDialog = new EventEditor(workspace, selectedDay, localEvent) { Owner = this };
            editDialog.Loaded += (_, _) => { editDialog.TitleInput.Text = "수정한 일정"; editDialog.SaveEventButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            Check(editDialog.ShowDialog() == true && workspace.State.Events.Single(x => x.Id == localEvent.Id).Title == "수정한 일정", "local event editor update");
            workspace.DeleteLocalEvent(localEvent.Id); RenderCalendar(); Check(AgendaItems.Children.Count == 3, "local event delete");
            var firstDate = month; NextMonth_Click(this, new RoutedEventArgs()); Check(month.Month == firstDate.AddMonths(1).Month && selectedDay.Month == month.Month, "month navigation keeps agenda in displayed month"); Today_Click(this, new RoutedEventArgs());
            var outsideDay = CalendarDays.Children.Cast<Button>().First(b => ((DateTime)b.Tag).Month != month.Month);
            var outsideDate = (DateTime)outsideDay.Tag;
            outsideDay.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(month == outsideDate && selectedDay == outsideDate, "adjacent month date opens that month");
            Today_Click(this, new RoutedEventArgs());
            AccountList.SelectedIndex = 0;
            Check(page == "mail" && MailPanel.Visibility == Visibility.Visible, "sidebar account opens its mailbox");
            UnreadFilter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(workspace.Query(accountId, unreadOnly: true).Count() == MessageList.Items.Count && System.Windows.Automation.AutomationProperties.GetItemStatus(UnreadFilter) == "선택됨", "unread filter and selected state");
            StarredFilter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(MessageList.Items.Cast<MailMessage>().All(m => m.IsStarred), "important filter");
            AllFilter.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SettingsNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ProviderCards.Items.Count == 3 && MailNotice.Visibility == Visibility.Collapsed && AccountList.SelectedItem == null, "settings show provider status without mailbox selection");
            await Capture("03-settings");
            var connectDialog = new CalendarConnectWindow { Owner = this };
            connectDialog.Loaded += (_, _) =>
            {
                connectDialog.UserNameInput.Text = "test@naver.com";
                connectDialog.PasswordInput.Password = "synthetic-test-password";
                connectDialog.ConnectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            Check(connectDialog.ShowDialog() == true && connectDialog.Connection?.UserName == "test" && connectDialog.PasswordInput.Password.Length == 0, "Naver connection dialog normalizes ID and clears password field");
            for (var accountIndex = 0; accountIndex < 4; accountIndex++)
            {
                var real = new Gather.Integrations.CalendarConnection { Provider = accountIndex < 2 ? "Google" : "Naver", Label = "검증 계정 " + (accountIndex + 1), MailEnabled = true, Identity = $"test{accountIndex}@example.invalid" };
                calendarConnections.Add(real);
                mailSnapshots[real.Id] = new Gather.Integrations.MailboxSnapshot(real.Identity, [new MailMessage { Id = real.Id + "/same-id", AccountId = real.Id, Sender = "검증 발신자", Subject = "여러 계정 메일 · " + (accountIndex + 1), Body = "실제 수신이 아닌 화면 검증용 메일입니다.", ReceivedAt = DateTimeOffset.Now }]);
            }
            accountId = null; RefreshAccounts(); ShowPage("mail");
            Check(AccountList.Items.Count == 4 && MessageList.Items.Count == 4 && MailAccountHeading.Text == "내 메일 계정", "two Google and two Naver mailboxes coexist without demo mail");
            AccountList.SelectedIndex = 1;
            Check(MessageList.Items.Count == 1 && SelectedMessage!.Subject.EndsWith("2"), "real multi-account mailbox selection isolates messages");
            await Capture("09-multi-account-mail-test-data");
            Check(!JsonSerializer.Serialize(workspace.State).Contains("/same-id"), "real mailbox contents never enter plaintext local state");
            var removedId = calendarConnections[0].Id;
            calendarConnections.RemoveAt(0); mailSnapshots.Remove(removedId); RefreshAccounts();
            Check(calendarConnections.Count == 3 && mailSnapshots.Count == 3 && calendarConnections.Any(x => x.Provider == "Google"), "removing one Google account retains second Google and both Naver accounts");
            calendarConnections.Clear(); mailSnapshots.Clear(); accountId = null; RefreshAccounts();
            SettingsNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TestNotification_Click(this, new RoutedEventArgs());
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(toast is { IsVisible: true, Topmost: true, ShowActivated: false, ShowInTaskbar: false }, "visible notification window does not steal focus or add taskbar clutter");
            await Capture("10-notification-test", (FrameworkElement)toast!.Content);
            Hide(); Check(toast.IsVisible, "notification remains visible while main window is hidden");
            ShowMainWindow(); Check(IsVisible, "desktop reactivation restores hidden window");
            WindowState = WindowState.Minimized; ShowMainWindow(); Check(WindowState == WindowState.Normal, "desktop reactivation restores minimized window");
            toast.Close();
            NewMailEnabled.IsChecked = false;
            Check(!preferences.MailNotificationsEnabled, "mail notification setting applies immediately");
            NewMailEnabled.IsChecked = true;
            Check(Icon != null, "window uses packaged application icon");
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { success = true, checks, skipped, utc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { success = false, checks, skipped, error = ex.ToString() })); Application.Current.Shutdown(1); }
    }
}
