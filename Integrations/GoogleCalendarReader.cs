using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Gather.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;

namespace Gather.Integrations;

public static class CalendarHttp
{
    public static HttpClient Create() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(40), MaxResponseContentBufferSize = 20_000_000 };
    public static void Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "로그인이 만료되었거나 계정 정보가 맞지 않습니다. 연결을 해제한 뒤 다시 연결해 주세요.",
            HttpStatusCode.Forbidden => "캘린더 접근 권한, API 활성화 상태 또는 계정 정책을 확인해 주세요.",
            HttpStatusCode.TooManyRequests => "요청이 많아 잠시 제한되었습니다. 잠시 후 새로고침해 주세요.",
            _ => $"캘린더 서버가 요청을 처리하지 못했습니다. (HTTP {(int)response.StatusCode})"
        });
    }
}

public static class GoogleCalendarLogin
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    public const string MailScope = "https://www.googleapis.com/auth/gmail.readonly";
    public static string[] Scopes(CalendarConnection connection) => new[] { connection.CalendarEnabled ? Scope : "", connection.MailEnabled ? MailScope : "" }.Where(x => x.Length > 0).ToArray();
    public static async Task<string> AccessTokenAsync(CalendarConnection connection, bool interactive, Action persist, CancellationToken ct)
    {
        var store = new CalendarTokenStore(connection, persist);
        var scopes = Scopes(connection);
        if (scopes.Length == 0) throw new InvalidOperationException("메일 또는 캘린더를 선택해 주세요.");
        var initializer = new GoogleAuthorizationCodeFlow.Initializer
            { ClientSecrets = new ClientSecrets { ClientId = connection.ClientId, ClientSecret = connection.ClientSecret }, Scopes = scopes, DataStore = store, Prompt = "select_account consent" };
        UserCredential credential;
        if (interactive)
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(initializer, scopes, connection.Id, true, ct, store);
        else
        {
            var flow = new PkceGoogleAuthorizationCodeFlow(initializer);
            var token = await flow.LoadTokenAsync(connection.Id, ct);
            if (token == null) throw new InvalidOperationException("Google에 다시 연결해 주세요.");
            credential = new UserCredential(flow, connection.Id, token);
        }
        using (credential.Flow)
        {
            if (!scopes.All(x => credential.Token.Scope?.Split(' ').Contains(x) == true))
                throw new InvalidOperationException("선택한 메일·캘린더 읽기 권한을 모두 허용해 주세요. 연결 해제 후 다시 연결할 수 있습니다.");
            return await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        }
    }
}

public sealed class GoogleCalendarReader(HttpClient client)
{
    // A full snapshot is returned only after every page and calendar succeeds.
    public async Task<CalendarSnapshot> ReadAsync(string token, string connectionId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var calendars = new List<RemoteCalendar>(); var events = new List<CalendarEvent>();
        var requestCount = 0;
        async Task<JsonDocument> Get(string url)
        {
            if (++requestCount > 200) throw new InvalidOperationException("한 번에 읽을 수 있는 캘린더 범위를 초과했습니다.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, ct);
            CalendarHttp.Check(response);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        const string api = "https://www.googleapis.com/calendar/v3/";
        string page = "";
        do
        {
            using var doc = await Get(api + "users/me/calendarList?maxResults=250&minAccessRole=reader&showHidden=true&pageToken=" + Uri.EscapeDataString(page));
            calendars.AddRange(GoogleCalendarJson.Calendars(doc.RootElement));
            page = GoogleCalendarJson.Text(doc.RootElement, "nextPageToken");
        } while (page.Length > 0);
        foreach (var calendar in calendars.DistinctBy(x => x.Id))
        {
            page = "";
            do
            {
                var url = api + "calendars/" + Uri.EscapeDataString(calendar.Id) + "/events?singleEvents=true&showDeleted=false&maxResults=2500&timeMin="
                    + Uri.EscapeDataString(from.ToString("O")) + "&timeMax=" + Uri.EscapeDataString(to.ToString("O")) + "&pageToken=" + Uri.EscapeDataString(page);
                using var doc = await Get(url);
                events.AddRange(GoogleCalendarJson.Events(doc.RootElement, connectionId, calendar));
                if (events.Count > 20000) throw new InvalidOperationException("한 번에 표시할 수 있는 일정 수를 초과했습니다.");
                page = GoogleCalendarJson.Text(doc.RootElement, "nextPageToken");
            } while (page.Length > 0);
        }
        return new CalendarSnapshot(calendars.DistinctBy(x => x.Id).ToList(), events.DistinctBy(x => x.Id).ToList());
    }
}
