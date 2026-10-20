using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Util.Store;

namespace Gather.Integrations;

public sealed class CalendarConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Provider { get; set; } = "Google";
    public string Label { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public bool MailEnabled { get; set; }
    public bool CalendarEnabled { get; set; } = true;
    public string Identity { get; set; } = "";
    public Dictionary<string, string> Tokens { get; set; } = [];
    public string Summary => string.Join(" · ", new[] { Provider == "Naver" ? "네이버" : Provider, Identity,
        MailEnabled ? "메일" : "", CalendarEnabled ? "일정" : "", "읽기 전용" }.Where(x => x.Length > 0));
}

// Only this user's Windows account can decrypt the connection file. Remote events are memory-only.
public sealed class CalendarVault(string path)
{
    private static readonly byte[] Entropy = "Gather.Calendar.v1"u8.ToArray();
    public List<CalendarConnection> Load()
    {
        if (!File.Exists(path)) return [];
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var items = JsonSerializer.Deserialize<List<CalendarConnection>>(clear) ?? throw new InvalidDataException();
            if (items.Any(x => x is null || !Guid.TryParseExact(x.Id, "N", out _) || x.Provider is not ("Google" or "Naver" or "Hiworks") || x.Tokens is null)
                || items.Select(x => x.Id).Distinct().Count() != items.Count) throw new InvalidDataException();
            return items;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(IReadOnlyList<CalendarConnection> items)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(items);
        try
        {
            var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path + ".tmp", encrypted);
            File.Move(path + ".tmp", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}

// Broker tokens never go through Google's default plaintext FileDataStore.
public sealed class CalendarTokenStore(CalendarConnection connection, Action persist) : IDataStore
{
    public Task StoreAsync<T>(string key, T value) { connection.Tokens[Key<T>(key)] = JsonSerializer.Serialize(value); persist(); return Task.CompletedTask; }
    public Task DeleteAsync<T>(string key) { connection.Tokens.Remove(Key<T>(key)); persist(); return Task.CompletedTask; }
    public Task ClearAsync() { connection.Tokens.Clear(); persist(); return Task.CompletedTask; }
    public Task<T> GetAsync<T>(string key) => Task.FromResult(connection.Tokens.TryGetValue(Key<T>(key), out var value) ? JsonSerializer.Deserialize<T>(value)! : default!);
    private static string Key<T>(string key) => typeof(T).FullName + ":" + key;
}
