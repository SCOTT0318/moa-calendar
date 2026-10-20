using System.Security.Cryptography;
using System.Text;

namespace Gather.Core;
public sealed class AppPreferences
{
    public bool NotificationsEnabled { get; set; } = true;
    public bool MailNotificationsEnabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public int ReminderMinutes { get; set; } = 10;
    public Dictionary<string, DateTimeOffset> DeliveredReminders { get; set; } = [];
}
public static class ReminderEngine
{
    public static IReadOnlyList<CalendarEvent> Due(IEnumerable<CalendarEvent> events, DateTimeOffset now, AppPreferences preferences)
    {
        if (!preferences.NotificationsEnabled) return [];
        var lead = Math.Clamp(preferences.ReminderMinutes, 0, 60);
        var due = events.Where(x => !x.IsAllDay && x.Start >= now.AddMinutes(-1) && x.Start <= now.AddMinutes(lead))
            .DistinctBy(x => x.Id).Where(x => !preferences.DeliveredReminders.ContainsKey(Key(x, lead))).ToList();
        foreach (var item in due) preferences.DeliveredReminders[Key(item, lead)] = item.Start;
        foreach (var key in preferences.DeliveredReminders.Where(x => x.Value < now.AddDays(-7)).Select(x => x.Key).ToList()) preferences.DeliveredReminders.Remove(key);
        return due;
    }
    private static string Key(CalendarEvent item, int lead) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{item.Id}|{item.Start.UtcTicks}|{lead}")));
}
