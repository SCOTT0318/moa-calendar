namespace Gather.Core;

public sealed class Workspace(DemoState state)
{
    public const string LocalCalendarId = "__local_calendar";
    public DemoState State { get; } = state;
    public IEnumerable<MailMessage> Query(string? accountId = null, string? search = null, bool unreadOnly = false, bool starredOnly = false)
    {
        var q = (search ?? "").Trim();
        return State.Messages.Where(x => (accountId is null || x.AccountId == accountId)
            && (!unreadOnly || !x.IsRead) && (!starredOnly || x.IsStarred)
            && (q.Length == 0 || x.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.Sender.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Body.Contains(q, StringComparison.OrdinalIgnoreCase)
                || x.SenderAddress.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id);
    }
    public Account AddDemo(Provider provider, string label)
    {
        label = label.Trim();
        if (label.Length is < 1 or > 40) throw new ArgumentException("표시 이름은 1~40자로 입력해 주세요.");
        var id = Guid.NewGuid().ToString("N");
        var account = new Account(id, label, $"demo-{id[..6]}@example.invalid", provider);
        State.Accounts.Add(account);
        State.Messages.Add(new MailMessage { Id = Guid.NewGuid().ToString("N"), AccountId = id, Sender = "모아 안내", SenderAddress = "hello@example.invalid", Subject = $"{label}, 데모 계정을 추가했어요", Body = "이 메일은 화면을 체험하기 위한 예시입니다. 실제 계정에 연결되거나 원격 서버에서 가져온 메일이 아닙니다.\n\n왼쪽 계정을 선택해 계정별 메일을 확인하고, 별표와 읽음 상태를 변경해 보세요.", ReceivedAt = DateTimeOffset.Now });
        return account;
    }
    public void RemoveDemo(string accountId)
    {
        State.Accounts.RemoveAll(x => x.Id == accountId);
        State.Messages.RemoveAll(x => x.AccountId == accountId);
        State.Events.RemoveAll(x => x.AccountId == accountId);
    }
    public IEnumerable<CalendarEvent> EventsOn(DateTime day, string? accountId = null) => State.Events
        .Where(x => (accountId is null || x.AccountId == accountId) && x.Start.LocalDateTime < day.Date.AddDays(1) && x.End.LocalDateTime > day.Date)
        .OrderBy(x => x.Start);
    public static IReadOnlyList<DateTime> MonthDays(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        return Enumerable.Range(0, 42).Select(i => start.AddDays(i)).ToList();
    }
    public CalendarEvent SaveLocalEvent(string? id, string title, DateTime day, TimeSpan start, TimeSpan end, string location)
    {
        title = title.Trim(); location = location.Trim();
        if (title.Length is < 1 or > 120) throw new ArgumentException("일정 제목은 1~120자로 입력해 주세요.");
        if (start < TimeSpan.Zero || end >= TimeSpan.FromDays(1) || start >= end) throw new ArgumentException("같은 날의 시작·종료 시간을 확인해 주세요. 종료는 시작보다 늦어야 합니다.");
        if (location.Length > 200) throw new ArgumentException("장소는 200자 이내로 입력해 주세요.");
        if (id != null && !State.Events.Any(x => x.Id == id && x.AccountId == LocalCalendarId)) throw new ArgumentException("수정할 로컬 일정을 찾지 못했습니다.");
        var item = new CalendarEvent(id ?? Guid.NewGuid().ToString("N"), LocalCalendarId, title, new DateTimeOffset(day.Date + start), new DateTimeOffset(day.Date + end), location);
        State.Events.RemoveAll(x => x.Id == item.Id && x.AccountId == LocalCalendarId);
        State.Events.Add(item);
        return item;
    }
    public void DeleteLocalEvent(string id) => State.Events.RemoveAll(x => x.Id == id && x.AccountId == LocalCalendarId);
}
