using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Gather.Core;
using Gather.Integrations;
using Google.Apis.Auth.OAuth2.Responses;

internal static class CalendarIntegrationChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var to = from.AddMonths(1);
        var googleRequests = new List<string>();
        using var googleHttp = new HttpClient(new FakeHandler(async request =>
        {
            check(request.Method == HttpMethod.Get && request.Headers.Authorization?.Scheme == "Bearer", "Google only sends authorized GET requests");
            var url = request.RequestUri!.AbsoluteUri; googleRequests.Add(url);
            await Task.Yield();
            if (url.Contains("calendarList")) return Json(url.Contains("pageToken=next")
                ? """{"items":[{"id":"shared/id@example.com","summary":"공유 일정","accessRole":"reader"},{"id":"busy","accessRole":"freeBusyReader"}]}"""
                : """{"items":[{"id":"primary@example.com","summary":"기본 일정","accessRole":"owner"}],"nextPageToken":"next"}""");
            if (url.Contains("pageToken=events-next")) return Json("{}");
            return Json("""
                {"nextPageToken":"events-next","items":[
                {"id":"one","summary":"종일 휴가","start":{"date":"2026-10-20"},"end":{"date":"2026-10-22"}},
                {"id":"two","summary":"회의","start":{"dateTime":"2026-10-20T09:00:00+09:00"},"end":{"dateTime":"2026-10-20T10:00:00+09:00"}},
                {"id":"gone","status":"cancelled"}]}
                """);
        }));
        var google = await new GoogleCalendarReader(googleHttp).ReadAsync("synthetic-token", "google-1", from, to, default);
        check(google.Calendars.Count == 2 && google.Events.Count == 4 && google.Events.Select(x => x.Id).Distinct().Count() == 4, "Google reads registered calendars and every event page without calendar ID collisions");
        check(google.Events.Count(x => x.IsAllDay && x.TimeLabel == "종일" && x.End.LocalDateTime.Date == new DateTime(2026, 10, 22)) == 2, "Google all-day end dates remain exclusive");
        check(google.Events.Where(x => !x.IsAllDay).All(x => x.Start.Offset == TimeSpan.FromHours(9)), "Google event offsets preserved");
        check(googleRequests.Count == 6 && googleRequests.Any(x => x.Contains("shared%2Fid")) && googleRequests.Where(x => x.Contains("/events?")).All(x => x.Contains("singleEvents=true") && x.Contains("timeMin=")), "Google range and recurrence expansion with escaped calendar IDs");
        using var denied = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var failed = false;
        try { await new GoogleCalendarReader(denied).ReadAsync("synthetic-token", "google-1", from, to, default); }
        catch (InvalidOperationException) { failed = true; }
        check(failed, "Google permission failure never returns an empty successful snapshot");

        const string ics = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Gather tests//EN
            BEGIN:VEVENT
            UID:repeat
            DTSTAMP:20261001T000000Z
            DTSTART;TZID=Asia/Seoul:20261020T090000
            DTEND;TZID=Asia/Seoul:20261020T100000
            RRULE:FREQ=DAILY;COUNT=3
            EXDATE;TZID=Asia/Seoul:20261021T090000
            SUMMARY:반복 회의
            END:VEVENT
            BEGIN:VEVENT
            UID:repeat
            DTSTAMP:20261001T000000Z
            RECURRENCE-ID;TZID=Asia/Seoul:20261022T090000
            DTSTART;TZID=Asia/Seoul:20261022T110000
            DTEND;TZID=Asia/Seoul:20261022T120000
            SUMMARY:변경한 회의
            END:VEVENT
            BEGIN:VEVENT
            UID:all-day
            DTSTAMP:20261001T000000Z
            DTSTART;VALUE=DATE:20261020
            DTEND;VALUE=DATE:20261022
            SUMMARY:휴가
            END:VEVENT
            BEGIN:VEVENT
            UID:overlap
            DTSTAMP:20261001T000000Z
            DTSTART;TZID=Asia/Seoul:20261020T090000
            DTEND;TZID=Asia/Seoul:20261020T100000
            SUMMARY:동시 일정
            END:VEVENT
            BEGIN:VEVENT
            UID:cancelled
            DTSTAMP:20261001T000000Z
            DTSTART:20261025T000000Z
            DTEND:20261025T010000Z
            STATUS:CANCELLED
            SUMMARY:취소
            END:VEVENT
            END:VCALENDAR
            """;
        var naverSource = new RemoteCalendar("naver-1", "업무", "#1F9B69");
        var occurrences = IcsCalendarReader.Read(ics, "naver-connection", naverSource, from, to);
        check(occurrences.Count == 4 && occurrences.All(x => x.Title != "취소") && occurrences.Count(x => x.Start.LocalDateTime.Date == new DateTime(2026, 10, 21)) == 0, "CalDAV recurrence exclusions and cancellations");
        check(occurrences.Single(x => x.Title == "변경한 회의").Start.ToOffset(TimeSpan.FromHours(9)).Hour == 11 && occurrences.Count(x => !x.IsAllDay && x.Start.ToOffset(TimeSpan.FromHours(9)).Hour == 9) == 2, "CalDAV recurrence overrides and simultaneous events preserved");
        check(occurrences.Single(x => x.IsAllDay).End.LocalDateTime.Date == new DateTime(2026, 10, 22), "CalDAV multi-day all-day event");
        check(IcsCalendarReader.Read(ics, "naver-connection", naverSource, from, to).Count == occurrences.Count, "CalDAV repeated reads are stable");
        var cancelled = new CancellationToken(true); failed = false;
        try { IcsCalendarReader.Read(ics, "n", naverSource, from, to, cancelled); } catch (OperationCanceledException) { failed = true; }
        check(failed, "CalDAV recurrence work can be cancelled");

        var methods = new List<string>();
        using var naverHttp = new HttpClient(new FakeHandler(async request =>
        {
            methods.Add(request.Method.Method);
            if (request.RequestUri!.AbsolutePath == "/") return Xml("""<d:response><d:propstat><d:prop><d:current-user-principal><d:href>/users/test/</d:href></d:current-user-principal></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>""");
            if (request.RequestUri.AbsolutePath == "/users/test/") return Xml("""<d:response><d:propstat><d:prop><c:calendar-home-set><d:href>/calendars/test/</d:href></c:calendar-home-set></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>""");
            if (request.Method.Method == "PROPFIND") return Xml("""<d:response><d:href>/calendars/test/one/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/><c:calendar/></d:resourcetype><d:displayname>업무</d:displayname></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>""");
            var body = await request.Content!.ReadAsStringAsync();
            check(body.Contains("time-range") && request.Headers.Authorization?.Scheme == "Basic", "CalDAV uses authenticated bounded REPORT");
            return Xml("<d:response><d:propstat><d:prop><c:calendar-data><![CDATA[" + ics + "]]></c:calendar-data></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>");
        }));
        var snapshot = await new NaverCalendarReader(naverHttp).ReadAsync(new CalendarConnection { Provider = "Naver", UserName = "test", Password = "synthetic-password" }, from, to, default);
        check(snapshot.Calendars.Count == 1 && snapshot.Events.Count == 4 && methods.SequenceEqual(new[] { "PROPFIND", "PROPFIND", "PROPFIND", "REPORT" }), "CalDAV discovers principal home and calendars using read-only requests");
        var redirectRequests = 0;
        using var redirectHttp = new HttpClient(new FakeHandler(_ =>
        {
            redirectRequests++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.example/calendar");
            return Task.FromResult(response);
        }));
        failed = false;
        try { await new NaverCalendarReader(redirectHttp).ReadAsync(connection: new CalendarConnection { Provider = "Naver", UserName = "test", Password = "synthetic-password" }, from, to, default); }
        catch (InvalidOperationException) { failed = true; }
        check(failed && redirectRequests == 1, "CalDAV never forwards credentials to a redirected third-party host");
        using var propertyDenied = new HttpClient(new FakeHandler(_ => Task.FromResult(Xml("""<d:response><d:propstat><d:prop/><d:status>HTTP/1.1 403 Forbidden</d:status></d:propstat></d:response>"""))));
        failed = false;
        try { await new NaverCalendarReader(propertyDenied).ReadAsync(new CalendarConnection(), from, to, default); }
        catch (InvalidOperationException) { failed = true; }
        check(failed, "CalDAV property access errors cannot become empty successful snapshots");
        foreach (var bad in new[] { "http://caldav.calendar.naver.com/", "https://evil.example/", "https://caldav.calendar.naver.com:444/", "https://user@caldav.calendar.naver.com/" })
        {
            failed = false;
            try { NaverCalendarReader.Resolve(bad, new Uri("https://caldav.calendar.naver.com/")); } catch (InvalidOperationException) { failed = true; }
            check(failed, "CalDAV rejects unsafe discovered URI " + bad);
        }

        var path = Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"), "private.bin");
        var vault = new CalendarVault(path);
        var connection = new CalendarConnection { Label = "test", Provider = "Naver", Password = "UNIQUE-TEST-CREDENTIAL" };
        vault.Save([connection]);
        check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(connection.Password) && vault.Load().Single().Password == connection.Password, "Windows encrypted credential storage round trip without plaintext password");
        var tokenStore = new CalendarTokenStore(connection, () => vault.Save([connection]));
        await tokenStore.StoreAsync("test-token", new TokenResponse { AccessToken = "SYNTHETIC-ACCESS", RefreshToken = "SYNTHETIC-REFRESH", Scope = GoogleCalendarLogin.Scope });
        check((await tokenStore.GetAsync<TokenResponse>("test-token")).RefreshToken == "SYNTHETIC-REFRESH" && vault.Load().Single().Tokens.Count == 1, "Google OAuth tokens use encrypted storage");
        await tokenStore.ClearAsync();
        check(vault.Load().Single().Tokens.Count == 0, "clearing connection tokens persists");
        vault.Save([]);
        check(vault.Load().Count == 0, "disconnect removes stored account credentials");
        vault.Save([connection]);
        var bytes = File.ReadAllBytes(path); bytes[0] ^= 0xFF; File.WriteAllBytes(path, bytes);
        failed = false;
        try { vault.Load(); } catch (CryptographicException) { failed = true; }
        check(failed && File.ReadAllBytes(path).SequenceEqual(bytes), "corrupted connection file fails without overwriting original");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Xml(string body) => new((HttpStatusCode)207) { Content = new StringContent("<d:multistatus xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\">" + body + "</d:multistatus>", Encoding.UTF8, "application/xml") };
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return send(request); } }
}
