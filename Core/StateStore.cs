using System.Text.Json;
using System.IO;
namespace Gather.Core;

// Contains synthetic mail and user-created LOCAL calendar events; never credentials or real mail.
public sealed class StateStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public DemoState Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(path)) return new DemoState();
        try
        {
            var data = JsonSerializer.Deserialize<DemoState>(File.ReadAllText(path), Options);
            if (data is null || data.Version != 1 || data.Accounts is null || data.Messages is null || data.Events is null
                || data.Accounts.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || !Enum.IsDefined(x.Provider))
                || data.Accounts.Select(x => x.Id).Distinct().Count() != data.Accounts.Count
                || data.Messages.Any(x => x is null || x.Body is null || x.Subject is null || x.Sender is null || x.SenderAddress is null || !data.Accounts.Any(a => a.Id == x.AccountId))
                || data.Events.Any(x => x is null || x.Title is null || x.Location is null || x.End <= x.Start || (x.AccountId != Workspace.LocalCalendarId && !data.Accounts.Any(a => a.Id == x.AccountId))))
                throw new JsonException("Unsupported demo data");
            // Old releases saved synthetic accounts alongside personal local events.
            // Remove only those accounts and their data; never match user event titles.
            var demoIds = data.Accounts.Where(x => x.IsDemo).Select(x => x.Id).ToHashSet();
            if (demoIds.Count > 0)
            {
                data.Messages.RemoveAll(x => demoIds.Contains(x.AccountId));
                data.Events.RemoveAll(x => x.AccountId != Workspace.LocalCalendarId && demoIds.Contains(x.AccountId));
                data.Accounts.RemoveAll(x => demoIds.Contains(x.Id));
                Save(data);
            }
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            warning = "저장된 내 일정을 읽지 못해 빈 화면을 열었습니다. 원본 보호를 위해 이번 실행은 저장하지 않습니다.";
            return new DemoState();
        }
    }
    public void Save(DemoState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, path, true);
    }
}
