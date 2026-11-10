using System.Net;
using System.Net.Http.Json;
using FlyGuy.Messaging;
using FlyGuy.Relay;

static void Check(bool value, string description)
{
    if (!value) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}
static async Task Rejected(Func<Task> request, HttpStatusCode status)
{
    try { await request(); throw new Exception("Unauthorized request succeeded"); }
    catch (RelayException ex) { Check(ex.StatusCode == status, $"relay rejected request with {(int)status}"); }
}
static string Secret() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
string directory = Path.Combine(Path.GetTempPath(), "fly-guy-messaging-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var clock = new TestClock();
    string path = Path.Combine(directory, "state.json");
    var store = new MessageStore(path, clock);
    var alice = store.Join("Alice"); var bob = store.Join("Bob");
    Check(alice.Id != bob.Id && alice.Token != bob.Token && store.Authenticate(alice.Token) == alice.Id,
        "recipients have unique private credentials");
    Guid id = store.Send(alice.Id, "Hello from the fly!");
    Check(store.Poll(bob.Id) is null && !store.Acknowledge(bob.Id, id) && store.Poll(alice.Id)?.Id == id,
        "recipient isolation covers both polling and acknowledgements");
    Check(!File.ReadAllText(path).Contains(alice.Token), "relay persists recipient token hashes, not raw keys");
    store = new MessageStore(path, clock);
    Check(store.Authenticate(alice.Token) == alice.Id && store.Poll(alice.Id)?.Text == "Hello from the fly!",
        "recipient credentials and queued messages survive relay restart");
    Check(store.Poll(alice.Id)?.Id == id, "unacknowledged delivery can be retried");
    Check(store.Acknowledge(alice.Id, id) && store.Poll(alice.Id) is null, "acknowledged messages leave the inbox");
    clock.Advance(TimeSpan.FromSeconds(46));
    Check(store.Users().All(u => !u.Online), "inactive clients become offline");
    store.Send(alice.Id, "Expired"); clock.Advance(TimeSpan.FromHours(25));
    Check(store.Poll(alice.Id) is null, "offline messages expire after 24 hours");
    for (int i = 0; i < 50; i++) store.Send(bob.Id, "Queued " + i);
    try { store.Send(bob.Id, "Overflow"); throw new Exception("Inbox overflow accepted"); }
    catch (InvalidOperationException) { Check(true, "offline inbox is bounded to 50 messages"); }
    try { RelayClient.ValidateAddress("http://example.com"); throw new Exception("Remote HTTP accepted"); }
    catch (ArgumentException) { Check(true, "remote clients require HTTPS"); }

    string adminKey = Secret(), invitation = Secret();
    await using var app = RelayApplication.Build([
        "--urls", "http://127.0.0.1:0", "--FLYGUY_ADMIN_KEY", adminKey,
        "--FLYGUY_INVITE_KEY", invitation, "--FLYGUY_DATA_PATH", Path.Combine(directory, "http.json"),
        "--Logging:LogLevel:Default", "None"]);
    await app.StartAsync();
    string address = app.Urls.Single();
    using var admin = new RelayClient(address, adminKey);
    using var join = new RelayClient(address, invitation);
    using var invalid = new RelayClient(address, Secret());
    await Rejected(() => invalid.Users(), HttpStatusCode.Unauthorized);
    await Rejected(() => join.Users(), HttpStatusCode.Unauthorized);
    await Rejected(() => invalid.Join("Uninvited"), HttpStatusCode.Unauthorized);
    await Rejected(() => admin.Join("Wrong key type"), HttpStatusCode.Unauthorized);
    var one = await join.Join("First fly"); var two = await join.Join("Second fly");
    using var first = new RelayClient(address, one.Token);
    using var second = new RelayClient(address, two.Token);
    await Rejected(() => first.Users(), HttpStatusCode.Unauthorized);
    await Rejected(() => first.Send(two.Id, "Not an admin"), HttpStatusCode.Unauthorized);
    await Rejected(() => admin.Send(one.Id, " "), HttpStatusCode.BadRequest);
    await Rejected(() => admin.Send(one.Id, new string('x', 501)), HttpStatusCode.BadRequest);
    await Rejected(() => admin.Send(Guid.NewGuid(), "Missing recipient"), HttpStatusCode.NotFound);
    await Rejected(() => join.Join("\n"), HttpStatusCode.BadRequest);
    string literal = "Hi! <b>This is plain speech, not HTML.</b>\nSecond line.";
    var sent = await admin.Send(one.Id, literal);
    Check(await second.Poll() is null, "HTTP delivery is targeted to the selected user");
    await second.Acknowledge(sent.MessageId);
    Check((await first.Poll()) is { } received && received.Id == sent.MessageId && received.Text == literal,
        "admin message reaches the recipient unchanged");
    await first.Acknowledge(sent.MessageId); await first.Acknowledge(sent.MessageId);
    Check(await first.Poll() is null, "HTTP acknowledgement is idempotent");
    var roster = await admin.Users();
    Check(roster.Length == 2 && roster.All(u => u.Online) && roster.All(u => u.PendingMessages == 0),
        "admin roster reports connected users and queue counts");
    using var anonymous = new HttpClient();
    Check((await anonymous.GetAsync(address + "/api/admin/users")).StatusCode == HttpStatusCode.Unauthorized,
        "anonymous requests cannot open the admin user list");
    await app.StopAsync();
    Console.WriteLine("PASS: messaging storage, live HTTP endpoints, authentication, isolation, expiry and validation.");
}
finally { Directory.Delete(directory, true); }

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
