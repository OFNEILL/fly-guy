using System.Threading.RateLimiting;
using FlyGuy.Messaging;
using FlyGuy.Relay;

namespace FlyGuy.Relay;

public static class RelayApplication
{
public static WebApplication Build(string[] args)
{
var builder = WebApplication.CreateBuilder(args);
string RequiredKey(string name)
{
    string key = builder.Configuration[name] ?? "";
    if (key.Length is < 32 or > 256) throw new InvalidOperationException($"Set {name} to a random secret of 32-256 characters.");
    return key;
}
string adminKey = RequiredKey("FLYGUY_ADMIN_KEY"), invitation = RequiredKey("FLYGUY_INVITE_KEY");
if (adminKey == invitation) throw new InvalidOperationException("Admin and invitation keys must be different.");
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5187");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
var store = new MessageStore(builder.Configuration["FLYGUY_DATA_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "relay-state.json"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try { await next(context); }
    catch (ArgumentException ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new RelayError(ex.Message)); }
    catch (KeyNotFoundException ex) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new RelayError(ex.Message)); }
    catch (InvalidOperationException ex) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new RelayError(ex.Message)); }
});
string? Token(HttpContext context)
{
    string header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null;
}
app.MapGet("/health", () => Results.Ok(new { service = "Fly Guy relay" }));
app.MapPost("/api/join", (HttpContext context, JoinRequest request) =>
    MessageStore.Matches(Token(context), invitation) ? Results.Ok(store.Join(request.DisplayName)) : Results.Unauthorized());
app.MapGet("/api/inbox", (HttpContext context) =>
{
    if (store.Authenticate(Token(context)) is not Guid userId) return Results.Unauthorized();
    return store.Poll(userId) is { } message ? Results.Ok(message) : Results.NoContent();
});
app.MapPost("/api/inbox/{messageId:guid}/ack", (HttpContext context, Guid messageId) =>
{
    if (store.Authenticate(Token(context)) is not Guid userId) return Results.Unauthorized();
    // Idempotent ACK; one user cannot acknowledge another user's message.
    store.Acknowledge(userId, messageId);
    return Results.NoContent();
});
app.MapGet("/api/admin/users", (HttpContext context) =>
    MessageStore.Matches(Token(context), adminKey) ? Results.Ok(store.Users()) : Results.Unauthorized());
app.MapPost("/api/admin/messages", (HttpContext context, SendRequest request) =>
    MessageStore.Matches(Token(context), adminKey) ? Results.Ok(new SendReceipt(store.Send(request.UserId, request.Text))) : Results.Unauthorized());
return app;
}
}
