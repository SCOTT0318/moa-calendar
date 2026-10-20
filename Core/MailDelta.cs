namespace Gather.Core;
public static class MailDelta
{
    public static IReadOnlyList<MailMessage> NewUnread(IReadOnlyList<MailMessage>? previous, IReadOnlyList<MailMessage> current)
    {
        if (previous == null) return [];
        var known = previous.Select(x => x.Id).ToHashSet();
        var watermark = previous.Count == 0 ? DateTimeOffset.MinValue : previous.Max(x => x.ReceivedAt);
        return current.Where(x => !x.IsRead && !known.Contains(x.Id) && x.ReceivedAt >= watermark).OrderByDescending(x => x.ReceivedAt).ToList();
    }
}
