using System.Globalization;
using System.Text.Json;

namespace Gather.Core;

public sealed record RemoteCalendar(string Id, string Name, string Color, bool IsPrimary = false);
public sealed record CalendarSnapshot(IReadOnlyList<RemoteCalendar> Calendars, IReadOnlyList<CalendarEvent> Events);

public static class GoogleCalendarJson
{
    public static IReadOnlyList<RemoteCalendar> Calendars(JsonElement page) => Items(page)
        .Where(x => Text(x, "accessRole") is "owner" or "writer" or "reader" or "writerWithoutPrivateAccess")
        .Where(x => !x.TryGetProperty("deleted", out var deleted) || !deleted.GetBoolean())
        .Select(x => new RemoteCalendar(Text(x, "id"), Text(x, "summaryOverride", Text(x, "summary", "Google 캘린더")), "#4979E7", x.TryGetProperty("primary", out var primary) && primary.GetBoolean())).ToList();

    public static IReadOnlyList<CalendarEvent> Events(JsonElement page, string connectionId, RemoteCalendar calendar)
    {
        var result = new List<CalendarEvent>();
        foreach (var item in Items(page))
        {
            if (Text(item, "status") == "cancelled") continue;
            if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end)) continue;
            var allDay = start.TryGetProperty("date", out _);
            var s = ParseTime(start); var e = ParseTime(end);
            // Zero-duration events remain visible for their start minute.
            if (e == s) e = s.AddMinutes(1);
            if (e < s) throw new FormatException("일정의 종료 시간이 시작 시간보다 빠릅니다.");
            result.Add(new CalendarEvent($"{connectionId}/{calendar.Id}/{Text(item, "id")}", connectionId,
                Text(item, "summary", "(제목 없음)"), s, e, Text(item, "location"))
                { IsAllDay = allDay, SourceLabel = $"Google · {calendar.Name}", SourceColor = calendar.Color });
        }
        return result;
    }

    private static DateTimeOffset ParseTime(JsonElement value)
    {
        if (value.TryGetProperty("dateTime", out var time)) return DateTimeOffset.Parse(time.GetString()!, CultureInfo.InvariantCulture);
        // DATE is a floating civil date, not a UTC instant.
        return new DateTimeOffset(DateTime.ParseExact(value.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
    public static string Text(JsonElement item, string key, string fallback = "") =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static IEnumerable<JsonElement> Items(JsonElement page) => page.TryGetProperty("items", out var items)
        ? items.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
