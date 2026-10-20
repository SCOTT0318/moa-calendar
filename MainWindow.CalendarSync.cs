using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Gather.Core;
using Gather.Integrations;
using Microsoft.Win32;

namespace Gather;
public partial class MainWindow
{
    private CalendarVault? calendarVault;
    private List<CalendarConnection> calendarConnections = [];
    private readonly Dictionary<string, CalendarSnapshot> calendarSnapshots = [];
    private readonly Dictionary<string, MailboxSnapshot> mailSnapshots = [];
    private CancellationTokenSource? calendarOperation;
    private bool pendingCalendarRefresh, calendarStorageBlocked;
    private DateTime? loadedCalendarMonth;
    private bool HasRealMail => calendarConnections.Any(x => x.MailEnabled);
    private IEnumerable<Account> MailAccounts() => HasRealMail
        ? calendarConnections.Where(x => x.MailEnabled).Select(x => new Account(x.Id, x.Label, x.Identity, x.Provider == "Google" ? Provider.Gmail : x.Provider == "Naver" ? Provider.Naver : Provider.Hiworks) { IsDemo = false })
        : workspace.State.Accounts;
    private IEnumerable<MailMessage> QueryMailMessages() => new Workspace(new DemoState { Messages = HasRealMail ? mailSnapshots.Values.SelectMany(x => x.Messages).ToList() : workspace.State.Messages })
        .Query(accountId, SearchBox.Text, unreadOnly, starredOnly);

    private void InitializeCalendarSync(bool smoke)
    {
        if (smoke) { CalendarSyncText.Text = "검증 모드 · 실제 계정 연결 안 함"; return; }
        calendarVault = new CalendarVault(Path.Combine(AppPaths.DataDirectory, "calendar-private.bin"));
        try { calendarConnections = calendarVault.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            calendarStorageBlocked = true;
            CalendarSyncText.Text = "연결 정보를 읽지 못했습니다. 원본은 보존됩니다. 원래 Windows 사용자 계정에서 실행해 주세요.";
            GoogleCalendarConnect.IsEnabled = NaverCalendarConnect.IsEnabled = HiworksConnect.IsEnabled = false;
        }
        RefreshCalendarConnections();
        Closed += (_, _) => calendarOperation?.Cancel();
        if (calendarConnections.Count > 0) Loaded += async (_, _) => await RefreshRemoteCalendarsAsync();
    }
    private void PersistCalendarConnections() => calendarVault?.Save(calendarConnections);
    private void RefreshCalendarConnections()
    {
        CalendarConnections.ItemsSource = calendarConnections.ToList();
        CalendarRefresh.IsEnabled = MailRefresh.IsEnabled = calendarConnections.Count > 0 && calendarOperation == null;
        CalendarRefresh.ToolTip = "연결한 모든 계정의 메일·일정을 새로고침합니다.";
    }
    private IEnumerable<CalendarEvent> RemoteEventsOn(DateTime date) => calendarSnapshots.Values.SelectMany(x => x.Events)
        .Where(x => x.Start.LocalDateTime < date.Date.AddDays(1) && x.End.LocalDateTime > date.Date);
    private static string SafeConnectionError(Exception ex) => ex is HttpRequestException ? "네트워크·TLS 연결을 확인하세요."
        : ex is MailKit.Security.AuthenticationException ? "아이디·앱 비밀번호와 외부 메일 허용 설정을 확인하세요."
        : ex is Google.Apis.Auth.OAuth2.Responses.TokenResponseException ? "Google 접근 승인을 다시 확인하세요."
        : ex.GetType() == typeof(InvalidOperationException) ? ex.Message : "계정 정책이나 서버 응답을 확인하세요.";

    private async Task<AccountSnapshot> ReadConnection(CalendarConnection connection, DateTime monthStart, bool interactive, CancellationToken ct)
    {
        using var client = CalendarHttp.Create();
        string? access = null;
        if (connection.Provider == "Google")
        {
            void PersistDraft() => calendarVault?.Save(calendarConnections.Any(x => x.Id == connection.Id) ? calendarConnections : calendarConnections.Append(connection).ToList());
            access = await GoogleCalendarLogin.AccessTokenAsync(connection, interactive, PersistDraft, ct);
        }
        CalendarSnapshot? calendar = null; MailboxSnapshot? mail = null;
        string? calendarError = null, mailError = null;
        if (connection.CalendarEnabled)
        {
            try
            {
                async Task<CalendarSnapshot> ReadRange(DateTimeOffset from, DateTimeOffset to) => connection.Provider == "Google"
                    ? await new GoogleCalendarReader(client).ReadAsync(access!, connection.Id, from, to, ct)
                    : await Task.Run(() => new NaverCalendarReader(client).ReadAsync(connection, from, to, ct), ct);
                var days = Workspace.MonthDays(monthStart);
                calendar = await ReadRange(new DateTimeOffset(days[0]), new DateTimeOffset(days[^1].AddDays(1)));
                // Reminders remain current even when the user is browsing an old/future month.
                if (DateTime.Today < days[0] || DateTime.Today.AddDays(1) > days[^1])
                {
                    var upcoming = await ReadRange(new DateTimeOffset(DateTime.Today), new DateTimeOffset(DateTime.Today.AddDays(2)));
                    calendar = new CalendarSnapshot(calendar.Calendars.Concat(upcoming.Calendars).DistinctBy(x => x.Id).ToList(), calendar.Events.Concat(upcoming.Events).DistinctBy(x => x.Id).ToList());
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { calendarError = SafeConnectionError(ex); }
        }
        if (connection.MailEnabled)
        {
            try
            {
                mailSnapshots.TryGetValue(connection.Id, out var previous);
                mail = connection.Provider switch
                {
                    "Google" => await MailboxReader.GoogleAsync(client, access!, connection, previous, ct),
                    "Naver" => await MailboxReader.NaverAsync(connection, previous, ct),
                    _ => await MailboxReader.HiworksAsync(connection, previous, ct)
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { mailError = SafeConnectionError(ex); }
        }
        if (mail == null && calendar == null) throw new InvalidOperationException(mailError ?? calendarError ?? "연결할 기능을 선택해 주세요.");
        return new AccountSnapshot(calendar, mail, calendarError, mailError);
    }
    private void ApplyAccountSnapshot(CalendarConnection connection, AccountSnapshot result, bool notify)
    {
        if (result.Calendar != null) calendarSnapshots[connection.Id] = result.Calendar;
        if (result.Mailbox != null)
        {
            if (notify && mailSnapshots.TryGetValue(connection.Id, out var previous))
            {
                var newMail = MailDelta.NewUnread(previous.Messages, result.Mailbox.Messages);
                if (newMail.Count > 0) ShowNewMailNotification(connection, newMail);
            }
            mailSnapshots[connection.Id] = result.Mailbox;
            connection.Identity = result.Mailbox.Address;
        }
    }
    private async Task RunCalendarOperation(Func<CancellationToken, Task> work)
    {
        if (calendarOperation != null || calendarStorageBlocked || calendarVault == null) return;
        calendarOperation = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        GoogleCalendarConnect.IsEnabled = NaverCalendarConnect.IsEnabled = HiworksConnect.IsEnabled = CalendarRefresh.IsEnabled = MailRefresh.IsEnabled = CalendarConnections.IsEnabled = false;
        CalendarCancel.Visibility = Visibility.Visible;
        CalendarSyncText.Text = "계정의 메일·일정을 확인하는 중…";
        try { await work(calendarOperation.Token); }
        catch (OperationCanceledException) { CalendarSyncText.Text = "확인을 취소했습니다. 기존 데이터는 유지됩니다."; }
        catch (Exception ex) { CalendarSyncText.Text = SafeConnectionError(ex); }
        finally
        {
            calendarOperation.Dispose(); calendarOperation = null;
            GoogleCalendarConnect.IsEnabled = NaverCalendarConnect.IsEnabled = HiworksConnect.IsEnabled = CalendarConnections.IsEnabled = true;
            CalendarCancel.Visibility = Visibility.Collapsed;
            RefreshCalendarConnections(); RefreshAccounts(); RefreshMessages(SelectedMessage?.Id); ShowPage(page); RenderCalendar(); CheckReminders();
        }
        if (pendingCalendarRefresh) { pendingCalendarRefresh = false; await RefreshRemoteCalendarsAsync(); }
    }
    private async Task ConnectCalendar(CalendarConnection connection)
    {
        if (connection.Provider != "Google" && calendarConnections.Any(x => x.Provider == connection.Provider && x.UserName.Equals(connection.UserName, StringComparison.OrdinalIgnoreCase)))
        { CalendarSyncText.Text = "이미 추가한 계정입니다. 다른 계정을 입력하거나 기존 계정을 새로고침하세요."; return; }
        var added = false;
        await RunCalendarOperation(async ct =>
        {
            try
            {
                var target = new DateTime(month.Year, month.Month, 1);
                var result = await ReadConnection(connection, target, true, ct); ct.ThrowIfCancellationRequested();
                var identity = result.Mailbox?.Address ?? result.Calendar?.Calendars.FirstOrDefault(x => x.IsPrimary)?.Id;
                if (connection.Provider == "Google" && identity != null && calendarConnections.Any(x => x.Provider == "Google" && x.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("이미 연결한 Google 계정입니다. 브라우저에서 다른 Google 계정을 선택해 주세요.");
                connection.Identity = identity ?? connection.UserName;
                if (string.IsNullOrWhiteSpace(connection.Label)) connection.Label = !string.IsNullOrWhiteSpace(connection.Identity) ? connection.Identity : "Google 계정 " + (calendarConnections.Count(x => x.Provider == "Google") + 1);
                calendarConnections.Add(connection);
                try { PersistCalendarConnections(); } catch { calendarConnections.Remove(connection); throw; }
                ApplyAccountSnapshot(connection, result, false); added = true;
                loadedCalendarMonth = target;
                CalendarSyncText.Text = $"{connection.Label} 연결됨 · " + ResultSummary(result);
                pendingCalendarRefresh = target != new DateTime(month.Year, month.Month, 1);
            }
            finally { if (!added) PersistCalendarConnections(); }
        });
    }
    private static string ResultSummary(AccountSnapshot result) => string.Join(" · ", new[] {
        result.Mailbox == null ? null : $"메일 {result.Mailbox.Messages.Count}통", result.Calendar == null ? null : $"일정 {result.Calendar.Events.Count}개",
        result.MailError == null ? null : "메일: " + result.MailError, result.CalendarError == null ? null : "일정: " + result.CalendarError }.Where(x => x != null));
    private async void GoogleCalendarConnect_Click(object sender, RoutedEventArgs e)
    {
        var options = new CalendarConnectWindow("Google") { Owner = this };
        if (options.ShowDialog() != true) return;
        var connection = options.Connection!;
        var configured = calendarConnections.FirstOrDefault(x => x.Provider == "Google" && x.ClientId.Length > 0);
        if (configured != null)
        {
            connection.ClientId = configured.ClientId; connection.ClientSecret = configured.ClientSecret;
            await ConnectCalendar(connection); return;
        }
        var dialog = new OpenFileDialog { Title = "Google 데스크톱 OAuth 클라이언트 JSON 선택", Filter = "Google OAuth 설정 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 100000) throw new FormatException();
            using var doc = JsonDocument.Parse(File.ReadAllText(dialog.FileName));
            if (!doc.RootElement.TryGetProperty("installed", out var installed)) throw new FormatException();
            connection.ClientId = GoogleCalendarJson.Text(installed, "client_id"); connection.ClientSecret = GoogleCalendarJson.Text(installed, "client_secret");
            if (!connection.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || connection.ClientSecret.Length == 0) throw new FormatException();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        { CalendarSyncText.Text = "Google Cloud의 데스크톱 앱용 OAuth 클라이언트 JSON을 선택해 주세요."; return; }
        await ConnectCalendar(connection);
    }
    private async void NaverCalendarConnect_Click(object sender, RoutedEventArgs e) { var dialog = new CalendarConnectWindow { Owner = this }; if (dialog.ShowDialog() == true) await ConnectCalendar(dialog.Connection!); }
    private async void HiworksConnect_Click(object sender, RoutedEventArgs e) { var dialog = new CalendarConnectWindow("Hiworks") { Owner = this }; if (dialog.ShowDialog() == true) await ConnectCalendar(dialog.Connection!); }
    private async void CalendarRefresh_Click(object sender, RoutedEventArgs e) => await RefreshRemoteCalendarsAsync();
    private void CalendarCancel_Click(object sender, RoutedEventArgs e) { pendingCalendarRefresh = false; calendarOperation?.Cancel(); }
    private async Task RefreshRemoteCalendarsAsync()
    {
        if (calendarOperation != null) { pendingCalendarRefresh = true; return; }
        if (calendarConnections.Count == 0) return;
        await RunCalendarOperation(async ct =>
        {
            var target = new DateTime(month.Year, month.Month, 1); var errors = new List<string>(); var success = 0;
            foreach (var connection in calendarConnections)
            {
                try
                {
                    var result = await ReadConnection(connection, target, false, ct); ct.ThrowIfCancellationRequested();
                    ApplyAccountSnapshot(connection, result, true); success++;
                    if (result.CalendarError != null || result.MailError != null) errors.Add(connection.Label + ": " + ResultSummary(result));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add(connection.Label + ": " + SafeConnectionError(ex)); }
            }
            loadedCalendarMonth = target; PersistCalendarConnections();
            CalendarSyncText.Text = $"{DateTime.Now:HH:mm} · {success}/{calendarConnections.Count}개 계정 확인" + (errors.Count > 0 ? " · " + string.Join(" / ", errors) : " · 2분마다 자동 확인");
            pendingCalendarRefresh = target != new DateTime(month.Year, month.Month, 1);
        });
    }
    private async void RefreshCalendarMonthIfNeeded() { if (loadedCalendarMonth != new DateTime(month.Year, month.Month, 1)) await RefreshRemoteCalendarsAsync(); }
    private void DisconnectCalendar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string id || calendarOperation != null) return;
        var connection = calendarConnections.First(x => x.Id == id); var index = calendarConnections.IndexOf(connection);
        calendarConnections.Remove(connection);
        try { PersistCalendarConnections(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { calendarConnections.Insert(index, connection); CalendarSyncText.Text = "연결 해제를 저장하지 못했습니다."; return; }
        calendarSnapshots.Remove(id); mailSnapshots.Remove(id); connection.Tokens.Clear(); connection.Password = "";
        if (accountId == id) accountId = null;
        RefreshCalendarConnections(); RefreshAccounts(); RefreshMessages(); ShowPage(page); RenderCalendar();
        CalendarSyncText.Text = "이 PC에서 계정을 해제했습니다. Google의 앱 권한은 Google 계정 설정에서도 철회할 수 있습니다.";
    }
}
