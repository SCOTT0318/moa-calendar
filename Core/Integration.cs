namespace Gather.Core;

// Legacy mail/demo adapter boundary. Calendar readers are implemented separately in Integrations.
public interface IReadOnlyAccountConnector
{
    Provider Provider { get; }
    Task<IReadOnlyList<MailMessage>> ReadInboxAsync(Account account, CancellationToken cancellationToken);
    Task<IReadOnlyList<CalendarEvent>> ReadEventsAsync(Account account, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
public sealed class UnconfiguredConnector(Provider provider) : IReadOnlyAccountConnector
{
    public Provider Provider => provider;
    public Task<IReadOnlyList<MailMessage>> ReadInboxAsync(Account account, CancellationToken cancellationToken) => throw Blocked();
    public Task<IReadOnlyList<CalendarEvent>> ReadEventsAsync(Account account, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw Blocked();
    private static InvalidOperationException Blocked() => new("실제 계정 연결은 아직 구현되지 않았습니다. OAuth 설정·정책 확인·사용자 동의가 필요합니다.");
}
