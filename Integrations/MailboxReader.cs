using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Gather.Core;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Security;
using MimeKit;

namespace Gather.Integrations;

public sealed record MailboxSnapshot(string Address, IReadOnlyList<MailMessage> Messages);
public sealed record AccountSnapshot(CalendarSnapshot? Calendar, MailboxSnapshot? Mailbox, string? CalendarError, string? MailError);

public static partial class MailboxReader
{
    public const int Limit = 50;
    public static async Task<MailboxSnapshot> GoogleAsync(HttpClient client, string access, CalendarConnection connection, MailboxSnapshot? previous, CancellationToken ct)
    {
        async Task<JsonDocument> Get(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://gmail.googleapis.com/gmail/v1/users/me/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await client.SendAsync(request, ct);
            CalendarHttp.Check(response);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        using var profile = await Get("profile");
        var address = profile.RootElement.GetProperty("emailAddress").GetString() ?? "";
        using var list = await Get("messages?labelIds=INBOX&maxResults=" + Limit);
        var cached = previous?.Messages.ToDictionary(x => x.Id) ?? [];
        var messages = new List<MailMessage>();
        if (list.RootElement.TryGetProperty("messages", out var rows))
            foreach (var row in rows.EnumerateArray().Take(Limit))
            {
                var remoteId = row.GetProperty("id").GetString()!;
                var id = connection.Id + "/" + remoteId;
                if (cached.TryGetValue(id, out var old)) { messages.Add(old); continue; }
                using var raw = await Get("messages/" + Uri.EscapeDataString(remoteId) + "?format=full");
                var payload = raw.RootElement.GetProperty("payload");
                var headerText = string.Join("\r\n", payload.GetProperty("headers").EnumerateArray().Select(x => x.GetProperty("name").GetString() + ": " + x.GetProperty("value").GetString()));
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(headerText + "\r\n\r\n"));
                var mime = await MimeMessage.LoadAsync(stream, ct);
                mime.Body = new TextPart("plain") { Text = GoogleText(payload) };
                var labels = raw.RootElement.TryGetProperty("labelIds", out var labelRows) ? labelRows.EnumerateArray().Select(x => x.GetString()).ToHashSet() : [];
                var received = raw.RootElement.TryGetProperty("internalDate", out var timestamp) && long.TryParse(timestamp.GetString(), out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : mime.Date;
                messages.Add(ConvertMessage(mime, connection.Id, remoteId, received, !labels.Contains("UNREAD"), labels.Contains("STARRED")));
            }
        return new MailboxSnapshot(address, messages);
    }

    public static async Task<MailboxSnapshot> NaverAsync(CalendarConnection connection, MailboxSnapshot? previous, CancellationToken ct)
    {
        using var client = new ImapClient { Timeout = 30000 };
        await client.ConnectAsync("imap.naver.com", 993, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(connection.UserName, connection.Password, ct);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var count = client.Inbox.Count;
        var messages = new List<MailMessage>();
        var cached = previous?.Messages.ToDictionary(x => x.Id) ?? [];
        if (count > 0)
        {
            var summaries = await client.Inbox.FetchAsync(Math.Max(0, count - Limit), count - 1,
                MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate | MessageSummaryItems.Flags | MessageSummaryItems.Size | MessageSummaryItems.Envelope, ct);
            foreach (var summary in summaries.Reverse())
            {
                var remoteId = client.Inbox.UidValidity + "/" + summary.UniqueId.Id;
                var id = connection.Id + "/" + remoteId;
                if (cached.TryGetValue(id, out var old)) { messages.Add(old); continue; }
                MimeMessage mime;
                if (summary.Size > 10_000_000)
                {
                    mime = new MimeMessage { Subject = summary.Envelope?.Subject ?? "(제목 없음)", Date = summary.Envelope?.Date ?? DateTimeOffset.Now,
                        Body = new TextPart("plain") { Text = "첨부파일이 큰 메일입니다. 본문과 첨부파일은 웹 메일에서 확인하세요." } };
                    if (summary.Envelope?.From != null) mime.From.AddRange(summary.Envelope.From);
                }
                else mime = await client.Inbox.GetMessageAsync(summary.UniqueId, ct);
                messages.Add(ConvertMessage(mime, connection.Id, remoteId, summary.InternalDate ?? mime.Date,
                    summary.Flags?.HasFlag(MessageFlags.Seen) == true, summary.Flags?.HasFlag(MessageFlags.Flagged) == true));
            }
        }
        await client.DisconnectAsync(true, ct);
        return new MailboxSnapshot(connection.UserName.Contains('@') ? connection.UserName : connection.UserName + "@naver.com", messages);
    }

    public static async Task<MailboxSnapshot> HiworksAsync(CalendarConnection connection, MailboxSnapshot? previous, CancellationToken ct)
    {
        using var client = new Pop3Client { Timeout = 30000 };
        await client.ConnectAsync("pop3s.hiworks.com", 995, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(connection.UserName, connection.Password, ct);
        var uids = await client.GetMessageUidsAsync(ct);
        var cached = previous?.Messages.ToDictionary(x => x.Id) ?? [];
        var messages = new List<MailMessage>();
        for (var i = uids.Count - 1; i >= Math.Max(0, uids.Count - Limit); i--)
        {
            var id = connection.Id + "/" + uids[i];
            if (cached.TryGetValue(id, out var old)) { messages.Add(old); continue; }
            MimeMessage mime;
            if (await client.GetMessageSizeAsync(i, ct) > 10_000_000)
            {
                var headers = await client.GetMessageHeadersAsync(i, ct);
                mime = new MimeMessage(headers) { Body = new TextPart("plain") { Text = "첨부파일이 큰 메일입니다. 본문과 첨부파일은 웹 메일에서 확인하세요." } };
            }
            else mime = await client.GetMessageAsync(i, ct);
            messages.Add(ConvertMessage(mime, connection.Id, uids[i], mime.Date, false, false));
        }
        // No DELE / DeleteMessage call: server copies always remain intact.
        await client.DisconnectAsync(true, ct);
        return new MailboxSnapshot(connection.UserName, messages);
    }

    public static MailMessage ConvertMessage(MimeMessage mime, string accountId, string remoteId, DateTimeOffset received, bool read, bool starred)
    {
        var sender = mime.From.Mailboxes.FirstOrDefault();
        var body = mime.TextBody;
        if (string.IsNullOrWhiteSpace(body)) body = System.Net.WebUtility.HtmlDecode(HtmlTags().Replace(mime.HtmlBody ?? "(텍스트 본문 없음)", " "));
        if (body.Length > 200000) body = body[..200000] + "\n\n(긴 본문은 웹 메일에서 확인하세요.)";
        return new MailMessage { Id = accountId + "/" + remoteId, AccountId = accountId, Sender = string.IsNullOrWhiteSpace(sender?.Name) ? sender?.Address ?? "(발신자 없음)" : sender.Name,
            SenderAddress = sender?.Address ?? "", Subject = mime.Subject ?? "(제목 없음)", Body = body, ReceivedAt = received, IsRead = read, IsStarred = starred };
    }
    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 1000)] private static partial Regex HtmlTags();
    private static string GoogleText(JsonElement payload)
    {
        var candidates = new List<(string Type, string Text)>();
        void Walk(JsonElement part)
        {
            if (part.TryGetProperty("mimeType", out var type) && type.GetString() is "text/plain" or "text/html"
                && (!part.TryGetProperty("filename", out var file) || string.IsNullOrEmpty(file.GetString()))
                && part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data))
            {
                var encoded = data.GetString()!.Replace('-', '+').Replace('_', '/'); encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                candidates.Add((type.GetString()!, Encoding.UTF8.GetString(Convert.FromBase64String(encoded))));
            }
            if (part.TryGetProperty("parts", out var parts)) foreach (var child in parts.EnumerateArray()) Walk(child);
        }
        Walk(payload);
        if (candidates.Any(x => x.Type == "text/plain")) return string.Join("\n", candidates.Where(x => x.Type == "text/plain").Select(x => x.Text));
        return candidates.Count == 0 ? "(본문은 웹 메일에서 확인하세요.)" : System.Net.WebUtility.HtmlDecode(HtmlTags().Replace(string.Join("\n", candidates.Select(x => x.Text)), " "));
    }
}
