using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Gather.Core;
using Ical.Net.DataTypes;

namespace Gather.Integrations;

public static class IcsCalendarReader
{
    public static IReadOnlyList<CalendarEvent> Read(string ics, string connectionId, RemoteCalendar source, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (ics.Length > 10_000_000 || !ics.Contains("BEGIN:VCALENDAR", StringComparison.Ordinal)) throw new FormatException("캘린더 데이터 형식이 올바르지 않습니다.");
        var calendar = Ical.Net.Calendar.Load(ics) ?? throw new FormatException("캘린더를 읽지 못했습니다.");
        var result = new List<CalendarEvent>();
        var evaluated = 0;
        // Calendar-level expansion handles EXDATE and RECURRENCE-ID overrides together.
        // Evaluate from the start so a long event beginning before the display range is not lost.
        foreach (var occurrence in calendar.GetOccurrences())
        {
            ct.ThrowIfCancellationRequested();
            if (occurrence.Source is not Ical.Net.CalendarComponents.CalendarEvent item) continue;
            var start = ToOffset(occurrence.Period.StartTime);
            if (start >= to) break;
            if (++evaluated > 100000) throw new InvalidOperationException("반복 일정이 너무 많아 읽기를 중단했습니다.");
            if (string.Equals(item.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            var endValue = occurrence.Period.EffectiveEndTime;
            var end = endValue == null ? (item.IsAllDay ? start.AddDays(1) : start.AddMinutes(1)) : ToOffset(endValue);
            if (end == start) end = start.AddMinutes(1);
            if (end <= from) continue;
            if (end < start) throw new FormatException("일정 시간 범위가 올바르지 않습니다.");
            result.Add(new CalendarEvent($"{connectionId}/{source.Id}/{item.Uid}/{start:O}", connectionId, item.Summary ?? "(제목 없음)", start, end, item.Location ?? "")
                { IsAllDay = item.IsAllDay, SourceLabel = "네이버 · " + source.Name, SourceColor = "#1F9B69" });
            if (result.Count > 20000) throw new InvalidOperationException("표시할 일정이 너무 많습니다.");
        }
        return result.DistinctBy(x => x.Id).ToList();
    }
    private static DateTimeOffset ToOffset(CalDateTime value) => value.IsFloating
        ? new DateTimeOffset(value.Value) : new DateTimeOffset(value.AsUtc);
}

public sealed class NaverCalendarReader(HttpClient client)
{
    private static readonly XNamespace Dav = "DAV:", Cal = "urn:ietf:params:xml:ns:caldav";
    public static Uri Resolve(string href, Uri basis)
    {
        var uri = new Uri(basis, href);
        if (uri.Scheme != "https" || !uri.Host.Equals("caldav.calendar.naver.com", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("네이버 캘린더 서버 주소를 확인하지 못했습니다.");
        return uri;
    }
    public async Task<CalendarSnapshot> ReadAsync(CalendarConnection connection, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var count = 0;
        async Task<XDocument> Send(Uri uri, string method, XElement body, string depth)
        {
            for (var redirects = 0; redirects < 4; redirects++)
            {
                if (++count > 100) throw new InvalidOperationException("캘린더 요청 수가 많아 중단했습니다.");
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(connection.UserName + ":" + connection.Password)));
                request.Headers.Add("Depth", depth);
                request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/xml");
                using var response = await client.SendAsync(request, ct);
                if ((int)response.StatusCode is 301 or 302 or 307 or 308)
                {
                    uri = Resolve(response.Headers.Location?.ToString() ?? throw new InvalidOperationException("캘린더 이동 주소가 없습니다."), uri);
                    continue;
                }
                CalendarHttp.Check(response);
                var text = await response.Content.ReadAsStringAsync(ct);
                using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
                var doc = XDocument.Load(reader);
                if (doc.Root?.Name != Dav + "multistatus") throw new FormatException("CalDAV 응답 형식이 올바르지 않습니다.");
                // A failed resource response must not silently erase a previously loaded calendar.
                foreach (var responseNode in doc.Root.Elements(Dav + "response"))
                {
                    var status = responseNode.Element(Dav + "status")?.Value;
                    if (status != null && !status.Contains(" 200 ")) throw new InvalidOperationException("일부 캘린더 데이터를 읽지 못했습니다.");
                    var properties = responseNode.Elements(Dav + "propstat").ToList();
                    if (properties.Count > 0 && !properties.Any(x => x.Element(Dav + "status")?.Value.Contains(" 200 ") == true))
                        throw new InvalidOperationException("캘린더 항목에 접근할 수 없습니다. 연결 권한을 확인해 주세요.");
                }
                return doc;
            }
            throw new InvalidOperationException("캘린더 서버 이동이 반복되었습니다.");
        }
        static XElement Propfind(params XName[] properties) => new(Dav + "propfind", new XElement(Dav + "prop", properties.Select(p => new XElement(p))));
        static IEnumerable<XElement> Props(XDocument doc) => doc.Descendants(Dav + "propstat")
            .Where(x => x.Element(Dav + "status")?.Value.Contains(" 200 ") == true).Elements(Dav + "prop");
        var root = new Uri("https://caldav.calendar.naver.com/");
        var principalDoc = await Send(root, "PROPFIND", Propfind(Dav + "current-user-principal"), "0");
        var principalHref = Props(principalDoc).Elements(Dav + "current-user-principal").Elements(Dav + "href").FirstOrDefault()?.Value
            ?? throw new InvalidOperationException("네이버 CalDAV 계정 경로를 찾지 못했습니다. 계정 정보와 외부 캘린더 정책을 확인해 주세요.");
        var principal = Resolve(principalHref, root);
        var homeDoc = await Send(principal, "PROPFIND", Propfind(Cal + "calendar-home-set"), "0");
        var homeHref = Props(homeDoc).Elements(Cal + "calendar-home-set").Elements(Dav + "href").FirstOrDefault()?.Value
            ?? throw new InvalidOperationException("네이버 캘린더 목록 경로를 찾지 못했습니다.");
        var home = Resolve(homeHref, principal);
        var list = await Send(home, "PROPFIND", Propfind(Dav + "resourcetype", Dav + "displayname"), "1");
        var calendars = new List<RemoteCalendar>(); var events = new List<CalendarEvent>();
        foreach (var resource in list.Root!.Elements(Dav + "response"))
        {
            var props = resource.Elements(Dav + "propstat").Where(x => x.Element(Dav + "status")?.Value.Contains(" 200 ") == true).Elements(Dav + "prop").ToList();
            if (!props.Elements(Dav + "resourcetype").Elements(Cal + "calendar").Any()) continue;
            var url = Resolve(resource.Element(Dav + "href")?.Value ?? throw new FormatException(), home);
            var source = new RemoteCalendar(url.AbsoluteUri, props.Elements(Dav + "displayname").FirstOrDefault()?.Value ?? "네이버 캘린더", "#1F9B69");
            calendars.Add(source);
            var query = new XElement(Cal + "calendar-query", new XElement(Dav + "prop", new XElement(Cal + "calendar-data")),
                new XElement(Cal + "filter", new XElement(Cal + "comp-filter", new XAttribute("name", "VCALENDAR"),
                    new XElement(Cal + "comp-filter", new XAttribute("name", "VEVENT"), new XElement(Cal + "time-range",
                        new XAttribute("start", from.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")), new XAttribute("end", to.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")))))));
            var report = await Send(url, "REPORT", query, "1");
            foreach (var response in report.Root!.Elements(Dav + "response"))
            {
                var data = response.Elements(Dav + "propstat").Where(x => x.Element(Dav + "status")?.Value.Contains(" 200 ") == true)
                    .Elements(Dav + "prop").Elements(Cal + "calendar-data").FirstOrDefault();
                if (data == null) throw new InvalidOperationException("일부 일정의 내용을 읽지 못했습니다. 기존 일정을 유지합니다.");
                events.AddRange(IcsCalendarReader.Read(data.Value, connection.Id, source, from, to, ct));
                if (events.Count > 20000) throw new InvalidOperationException("표시할 일정이 너무 많습니다.");
            }
        }
        return new CalendarSnapshot(calendars, events.DistinctBy(x => x.Id).ToList());
    }
}
