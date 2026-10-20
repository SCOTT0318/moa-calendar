using System.IO;
using System.Text.Json;
using Gather.Core;

namespace Gather;
public static class AppPaths
{
    public static string DataDirectory { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moa");
    public static void Initialize(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        if (index >= 0 && index + 1 < args.Length) DataDirectory = Path.GetFullPath(args[index + 1]);
        if (args.Contains("--smoke-test")) return;
        // Import the previous portable build's data once, without removing the originals.
        var legacy = Path.Combine(AppContext.BaseDirectory, "Data");
        foreach (var name in new[] { "demo-state.json", "calendar-private.bin" })
        {
            var target = Path.Combine(DataDirectory, name); var source = Path.Combine(legacy, name);
            if (!File.Exists(target) && File.Exists(source) && !Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            { Directory.CreateDirectory(DataDirectory); File.Copy(source, target, false); }
        }
    }
}
public sealed class PreferencesStore(string path)
{
    public bool ReadOnly { get; private set; }
    public AppPreferences Load()
    {
        if (!File.Exists(path)) return new AppPreferences();
        try
        {
            var value = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) ?? throw new JsonException();
            if (value.DeliveredReminders == null) throw new JsonException();
            value.ReminderMinutes = Math.Clamp(value.ReminderMinutes, 0, 60); return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { ReadOnly = true; return new AppPreferences(); }
    }
    public void Save(AppPreferences value)
    {
        if (ReadOnly) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value)); File.Move(path + ".tmp", path, true);
    }
}
