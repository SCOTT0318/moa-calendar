using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Gather.Core;
using Gather.Integrations;
using Forms = System.Windows.Forms;

namespace Gather;
public partial class MainWindow
{
    private AppPreferences preferences = new();
    private PreferencesStore? preferencesStore;
    private Forms.NotifyIcon? tray;
    private NotificationWindow? toast;
    private readonly Queue<(string Title, string Body, Action Open)> notificationQueue = [];
    private DispatcherTimer? reminderTimer, syncTimer;
    private bool shuttingDown, notificationReady, smokeMode;
    private void InitializeNotifications(bool smoke)
    {
        smokeMode = smoke;
        if (!smoke) { preferencesStore = new PreferencesStore(Path.Combine(AppPaths.DataDirectory, "settings.json")); preferences = preferencesStore.Load(); }
        ReminderEnabled.IsChecked = preferences.NotificationsEnabled; NewMailEnabled.IsChecked = preferences.MailNotificationsEnabled; TrayOnClose.IsChecked = preferences.CloseToTray;
        ReminderLead.Text = preferences.ReminderMinutes.ToString(); notificationReady = true;
        if (smoke) return;
        tray = new Forms.NotifyIcon { Text = "모아 · 메일과 일정", Visible = true, Icon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Assets/moa.ico")).Stream) };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("모아 열기", null, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow));
        menu.Items.Add("새로고침", null, (_, _) => Dispatcher.BeginInvoke(async () => await RefreshRemoteCalendarsAsync()));
        menu.Items.Add("종료", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication)); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
        Closing += OnWindowClosing;
        Closed += (_, _) => { reminderTimer?.Stop(); syncTimer?.Stop(); tray.Visible = false; tray.Dispose(); toast?.Close(); };
        reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; reminderTimer.Tick += (_, _) => CheckReminders(); reminderTimer.Start();
        syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) }; syncTimer.Tick += async (_, _) => { if (calendarOperation == null) await RefreshRemoteCalendarsAsync(); }; syncTimer.Start();
        Loaded += (_, _) => { CheckReminders(); if (Environment.GetCommandLineArgs().Contains("--startup")) Hide(); };
    }
    private void SavePreferences()
    {
        if (smokeMode) return;
        try { preferencesStore?.Save(preferences); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "알림 설정을 저장하지 못했습니다. 이번 실행에서만 적용합니다."; }
    }
    private void NotificationSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (!notificationReady) return;
        preferences.NotificationsEnabled = ReminderEnabled.IsChecked == true; preferences.MailNotificationsEnabled = NewMailEnabled.IsChecked == true; preferences.CloseToTray = TrayOnClose.IsChecked == true;
        if (int.TryParse(ReminderLead.Text, out var minutes)) preferences.ReminderMinutes = Math.Clamp(minutes, 0, 60);
        SavePreferences();
    }
    private void ReminderLead_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => NotificationSettings_Changed(sender, e);
    private void CheckReminders()
    {
        if (!notificationReady) return;
        var events = workspace.State.Events.Where(x => x.AccountId == Workspace.LocalCalendarId).Concat(calendarSnapshots.Values.SelectMany(x => x.Events));
        var due = ReminderEngine.Due(events, DateTimeOffset.Now, preferences);
        if (due.Count == 0) return;
        SavePreferences();
        QueueNotification($"곧 시작할 일정 {due.Count}개", string.Join("\n", due.Take(3).Select(x => $"{x.Start.ToLocalTime():HH:mm} · {x.Title}")), () => { selectedDay = month = due[0].Start.LocalDateTime.Date; ShowMainWindow(); ShowPage("calendar"); RenderCalendar(); });
    }
    private void ShowNewMailNotification(CalendarConnection connection, IReadOnlyList<MailMessage> messages)
    {
        if (!preferences.MailNotificationsEnabled) return;
        QueueNotification($"{connection.Label} · 새 메일 {messages.Count}통", string.Join("\n", messages.Take(3).Select(x => x.Sender + " · " + x.Subject)), () => { accountId = connection.Id; ShowMainWindow(); RefreshAccounts(); ShowPage("mail"); });
    }
    private void QueueNotification(string title, string body, Action open)
    {
        if (shuttingDown) return;
        if (notificationQueue.Count >= 20) notificationQueue.Dequeue();
        notificationQueue.Enqueue((title, body, open)); DisplayNextNotification();
    }
    private void DisplayNextNotification()
    {
        if (toast != null || notificationQueue.Count == 0 || shuttingDown) return;
        var item = notificationQueue.Dequeue(); toast = new NotificationWindow(item.Title, item.Body, item.Open);
        toast.Closed += (_, _) => { toast = null; DisplayNextNotification(); }; toast.Show();
    }
    private void TestNotification_Click(object sender, RoutedEventArgs e) => QueueNotification("알림 테스트", "새 메일과 일정 알림은 이렇게 표시됩니다. 앱이 트레이에 있어도 확인할 수 있습니다.", ShowMainWindow);
    internal void ShowMainWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!shuttingDown && preferences.CloseToTray) { e.Cancel = true; Hide(); }
    }
    private void ExitApplication() { shuttingDown = true; notificationQueue.Clear(); toast?.Close(); Close(); Application.Current.Shutdown(); }
}
