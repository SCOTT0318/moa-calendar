namespace Gather.Core;

public enum Provider { Gmail, Naver, Hiworks }
public sealed record Account(string Id, string Label, string Address, Provider Provider)
{
    public bool IsDemo { get; init; } = true;
    public string Color => Provider switch { Provider.Gmail => "#4979E7", Provider.Naver => "#1F9B69", _ => "#9270CC" };
    public string ProviderLabel => Provider switch { Provider.Naver => "NAVER", Provider.Hiworks => "Hiworks", _ => "Gmail" };
    public string Subtitle => IsDemo ? $"{Address} · 예시" : Address;
}
public sealed class MailMessage
{
    public string Id { get; init; } = "";
    public string AccountId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string SenderAddress { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTimeOffset ReceivedAt { get; init; }
    public bool IsRead { get; set; }
    public bool IsStarred { get; set; }
    public string Preview => Body.Replace("\r", "").Replace("\n", " ");
    public string TimeLabel => ReceivedAt.ToLocalTime().ToString("MM.dd HH:mm");
    public string ReadLabel => IsRead ? "읽음" : "● 안 읽음";
    public string StarLabel => IsStarred ? "★" : "";
}
public sealed record CalendarEvent(string Id, string AccountId, string Title, DateTimeOffset Start, DateTimeOffset End, string Location)
{
    public bool IsAllDay { get; init; }
    public string? SourceLabel { get; init; }
    public string? SourceColor { get; init; }
    public string TimeLabel => IsAllDay ? "종일" : Start.LocalDateTime.Date != End.LocalDateTime.Date
        ? $"{Start.ToLocalTime():M.d HH:mm} – {End.ToLocalTime():M.d HH:mm}" : $"{Start.ToLocalTime():HH:mm} – {End.ToLocalTime():HH:mm}";
}
public sealed class DemoState
{
    public int Version { get; set; } = 1;
    public List<Account> Accounts { get; set; } = [];
    public List<MailMessage> Messages { get; set; } = [];
    public List<CalendarEvent> Events { get; set; } = [];
}
public sealed record ProviderInfo(Provider Provider, string Name, string Mail, string Calendar, string Prerequisites);
public static class ProviderCatalog
{
    public static readonly ProviderInfo[] All = [
        new(Provider.Gmail, "Gmail", "Gmail API · 읽기 전용 설계", "Google Calendar API · 별도 읽기 권한", "Google 로그인으로 연결할 예정입니다. 최초 연결 시 브라우저에서 메일 접근을 허용해야 합니다."),
        new(Provider.Naver, "NAVER", "IMAP · SSL 993 · 실제 연결 미구현", "CalDAV의 Windows 호환 미검증", "네이버 메일의 IMAP 사용 설정과 2단계 인증·앱 비밀번호가 필요합니다."),
        new(Provider.Hiworks, "Hiworks", "POP3 · SSL 995 · 실제 연결 미구현", "일정 내보내기·API 지원 미확인", "전용 메일 비밀번호와 회사의 외부 메일 사용 허용이 필요합니다. 받은 편지함 수신을 지원할 예정입니다.")
    ];
    public static ProviderInfo Get(Provider provider) => All.Single(x => x.Provider == provider);
}
