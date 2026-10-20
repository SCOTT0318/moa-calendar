using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Gather.Core;
using Gather.Integrations;
using MimeKit;

internal static class MailAndReminderChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(9));
        var prefs = new AppPreferences();
        var first = new CalendarEvent("local", Workspace.LocalCalendarId, "약속", now.AddMinutes(10), now.AddMinutes(40), "");
        var events = new[] { first, first with { Id = "future", Start = now.AddHours(2) }, first with { Id = "old", Start = now.AddMinutes(-2) }, first with { Id = "all-day", IsAllDay = true } };
        check(ReminderEngine.Due(events, now, prefs).Single().Id == "local", "reminders honor lead time and exclude expired/all-day events");
        check(ReminderEngine.Due(events, now.AddSeconds(10), prefs).Count == 0, "reminder is delivered once per occurrence");
        check(ReminderEngine.Due([first with { Start = now.AddMinutes(5) }], now, prefs).Count == 1, "changed event start gets a new reminder");
        prefs.NotificationsEnabled = false;
        check(ReminderEngine.Due([first with { Id = "off" }], now, prefs).Count == 0, "disabled reminders stay silent");
        check(prefs.DeliveredReminders.Keys.All(x => x.Length == 64 && !x.Contains("local")), "persisted reminder history contains hashes rather than event titles");
        MailMessage Message(string id, DateTimeOffset at, bool read = false) => new() { Id = id, AccountId = "a", ReceivedAt = at, IsRead = read, Subject = "합성 메일" };
        var old = Message("old", now); var fresh = Message("new", now.AddMinutes(1));
        check(MailDelta.NewUnread(null, [old]).Count == 0, "first mail sync establishes baseline without notification flood");
        check(MailDelta.NewUnread([old], [old, fresh, Message("read", now.AddMinutes(2), true), Message("older", now.AddDays(-1))]).Single().Id == "new", "new mail alerts exclude read and old backfilled mail");
        check(MailDelta.NewUnread([old, fresh], [old, fresh]).Count == 0, "repeated mail refresh does not notify twice");
        var gmail = new CalendarConnection { Provider = "Google", MailEnabled = true, CalendarEnabled = true };
        check(GoogleCalendarLogin.Scopes(gmail).Order().SequenceEqual(new[] { GoogleCalendarLogin.Scope, GoogleCalendarLogin.MailScope }.Order()), "Google requests read-only mail and calendar scopes independently");
        gmail.CalendarEnabled = false;
        check(GoogleCalendarLogin.Scopes(gmail).SequenceEqual([GoogleCalendarLogin.MailScope]), "mail-only Google account does not request calendar access");

        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
            if (request.Method != HttpMethod.Get || request.Headers.Authorization?.Scheme != "Bearer") throw new Exception("Unsafe Gmail request");
            if (url.EndsWith("profile")) return Json("""{"emailAddress":"test@example.invalid"}""");
            if (url.Contains("messages?")) return Json("""{"messages":[{"id":"same-id"}]}""");
            var text = Convert.ToBase64String(Encoding.UTF8.GetBytes("안녕하세요. 실제로 보내지 않은 검증용 본문입니다.")).TrimEnd('=').Replace('+','-').Replace('/','_');
            return Json("{\"labelIds\":[\"INBOX\",\"UNREAD\"],\"internalDate\":\"1792454400000\",\"payload\":{\"mimeType\":\"text/plain\",\"headers\":[{\"name\":\"From\",\"value\":\"Test <sender@example.invalid>\"},{\"name\":\"Subject\",\"value\":\"Test message\"}],\"body\":{\"data\":\"" + text + "\"}}}");
        }));
        var a = new CalendarConnection { Provider = "Google", MailEnabled = true }; var b = new CalendarConnection { Provider = "Google", MailEnabled = true };
        var inboxA = await MailboxReader.GoogleAsync(http, "synthetic-token", a, null, default);
        var inboxB = await MailboxReader.GoogleAsync(http, "synthetic-token", b, null, default);
        check(inboxA.Messages.Single().Id != inboxB.Messages.Single().Id && inboxA.Messages.Single().Body.Contains("안녕하세요") && !inboxA.Messages.Single().IsRead, "two Google accounts with same remote mail ID remain isolated and decode Korean body");
        var before = requests.Count;
        await MailboxReader.GoogleAsync(http, "synthetic-token", a, inboxA, default);
        check(requests.Count - before == 2 && requests.All(x => !x.Contains("attachments")), "Gmail refresh reuses cached messages and does not download attachments");

        using var mimeInput = new MemoryStream(Encoding.UTF8.GetBytes("From: Sender <sender@example.invalid>\r\nSubject: HTML test\r\nMIME-Version: 1.0\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>Hello &amp; welcome</p><img src='https://example.invalid/tracker'>"));
        var mime = await MimeMessage.LoadAsync(mimeInput);
        var decoded = MailboxReader.ConvertMessage(mime, "a", "one", now, false, false);
        check(decoded.Body.Contains("Hello & welcome") && !decoded.Body.Contains("<img"), "HTML mail becomes inert text without loading remote images");
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"), "multi.bin");
        var vault = new CalendarVault(path);
        var connections = new[] { a, b, new CalendarConnection { Provider = "Naver", UserName = "naver-one", MailEnabled = true }, new CalendarConnection { Provider = "Naver", UserName = "naver-two", MailEnabled = true } };
        vault.Save(connections); var loaded = vault.Load();
        check(loaded.Count == 4 && loaded.Count(x => x.Provider == "Google") == 2 && loaded.Count(x => x.Provider == "Naver") == 2 && loaded.Select(x => x.Id).Distinct().Count() == 4, "two Google plus two Naver accounts survive encrypted store reload");
        vault.Save(loaded.Where(x => x.Id != a.Id).ToList());
        check(vault.Load().Any(x => x.Id == b.Id) && vault.Load().Count == 3, "disconnecting one Google account preserves all others");
        var prefsPath = Path.Combine(Path.GetDirectoryName(path)!, "settings.json");
        var store = new Gather.PreferencesStore(prefsPath); store.Save(prefs);
        check(store.Load().DeliveredReminders.Count == prefs.DeliveredReminders.Count && !store.Load().NotificationsEnabled, "notification settings and deduplication survive restart");
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(action(request)); } }
}
