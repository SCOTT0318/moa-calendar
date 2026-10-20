using Gather.Core;
using System.Text.Json;
using System.IO;

var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks.Add(name); Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var today = new DateTime(2026, 10, 20);
var ws = new Workspace(DemoFactory.Create(today));
Check(ws.State.Accounts.Count == 3 && ws.Query().Count() == 8, "three provider demo accounts and eight messages");
Check(ws.Query("gmail-demo").All(x => x.AccountId == "gmail-demo") && ws.Query("gmail-demo").Count() == 3, "account isolation");
Check(ws.Query(search: "  북클럽 ").Count() == 1, "trimmed sender search");
Check(ws.Query(search: "SAMPLE0@").Count() == 1, "case insensitive address search");
Check(ws.Query(search: "가상의 데모").Count() == 8, "body search");
Check(ws.Query(unreadOnly: true).Count() == 3 && ws.Query(starredOnly: true).Count() == 2, "unread and star filters");
Check(ws.Query("naver-demo", unreadOnly: true).Count() == 1, "combined filters");
Check(ws.Query().Select(x => x.ReceivedAt).SequenceEqual(ws.Query().Select(x => x.ReceivedAt).OrderDescending()), "newest first");
var a = ws.AddDemo(Provider.Hiworks, "회사 추가"); var b = ws.AddDemo(Provider.Hiworks, "회사 추가");
Check(a.Id != b.Id && ws.Query(a.Id).Count() == 1, "duplicate names have distinct account identities");
Reject(() => ws.AddDemo(Provider.Gmail, "  "), "empty account label rejected");
ws.RemoveDemo(a.Id); Check(!ws.State.Messages.Any(x => x.AccountId == a.Id) && ws.State.Accounts.Any(x => x.Id == b.Id), "account removal cascades only matching data");
var days = Workspace.MonthDays(new DateTime(2024, 2, 1));
Check(days.Count == 42 && days[0].DayOfWeek == DayOfWeek.Sunday && days.Contains(new DateTime(2024, 2, 29)), "leap year month grid");
Check(Workspace.MonthDays(new DateTime(2026, 12, 1)).Last().Year == 2027, "year crossing month grid");
Check(ws.EventsOn(today).Count() == 3 && ws.EventsOn(today, "naver-demo").Count() == 1, "calendar account filter");
var local = ws.SaveLocalEvent(null, "  내 약속  ", today, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "서울");
Check(local.Title == "내 약속" && ws.EventsOn(today).Count() == 4, "local event create");
ws.SaveLocalEvent(local.Id, "수정", today.AddDays(1), TimeSpan.FromHours(12), TimeSpan.FromHours(13), "온라인");
Check(ws.EventsOn(today).Count() == 3 && ws.EventsOn(today.AddDays(1)).Single().Title == "수정", "local event move and edit without duplication");
Reject(() => ws.SaveLocalEvent(null, "잘못된 시간", today, TimeSpan.FromHours(10), TimeSpan.FromHours(9), ""), "end before start rejected");
Reject(() => ws.SaveLocalEvent(null, "", today, TimeSpan.Zero, TimeSpan.FromHours(1), ""), "blank event title rejected");
Reject(() => ws.SaveLocalEvent("event-1", "예시 변경 금지", today, TimeSpan.Zero, TimeSpan.FromHours(1), ""), "remote demo event cannot be edited as local");
var tempRoot = Path.Combine(AppContext.BaseDirectory, "TestData", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
var storePath = Path.Combine(tempRoot, "state.json"); var store = new StateStore(storePath);
ws.State.Messages[0].IsRead = true; store.Save(ws.State);
var reloaded = store.Load(out var warning);
Check(warning == null && reloaded.Messages.Count == 0 && reloaded.Accounts.Count == 0 && reloaded.Events.Single().Id == local.Id, "upgrade removes synthetic mail and events while preserving personal local event");
Check(store.Load(out _).Events.Single().Id == local.Id && !File.ReadAllText(storePath).Contains("gmail-demo"), "sample cleanup persists and is idempotent");
var ws2 = new Workspace(reloaded); ws2.DeleteLocalEvent(local.Id); store.Save(ws2.State);
Check(!store.Load(out _).Events.Any(x => x.Id == local.Id), "local event deletion persists");
File.WriteAllText(storePath, "{invalid"); var recovered = store.Load(out warning);
Check(warning != null && recovered.Accounts.Count == 0 && recovered.Messages.Count == 0 && recovered.Events.Count == 0 && File.ReadAllText(storePath) == "{invalid", "corrupt file preserved without regenerating examples");
var fresh = new StateStore(Path.Combine(tempRoot, "new.json")).Load(out var freshWarning);
Check(freshWarning == null && fresh.Accounts.Count == 0 && fresh.Messages.Count == 0 && fresh.Events.Count == 0, "first launch is empty without sample data");
var invalid = DemoFactory.Create(today); invalid.Accounts.Add(invalid.Accounts[0]); store.Save(invalid); store.Load(out warning);
Check(warning != null, "duplicate account IDs rejected on load");
try { await new UnconfiguredConnector(Provider.Gmail).ReadInboxAsync(ws.State.Accounts[0], CancellationToken.None); throw new Exception("unconfigured auth unexpectedly proceeded"); }
catch (InvalidOperationException) { Check(true, "unconfigured connector fails closed before network"); }
Check(ProviderCatalog.Get(Provider.Hiworks).Mail.Contains("POP3") && ProviderCatalog.Get(Provider.Naver).Calendar.Contains("미검증"), "provider capabilities remain honest");
await CalendarIntegrationChecks.Run(Check);
await MailAndReminderChecks.Run(Check);
Console.WriteLine($"{checks.Count} checks passed. Test data: {tempRoot}");
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { success = true, count = checks.Count, checks, utc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
