using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FlyGuy.Messaging;

public sealed record JoinRequest(string DisplayName);
public sealed record ClientIdentity(Guid Id, string Token, string DisplayName);
public sealed record RelayUser(Guid Id, string DisplayName, DateTimeOffset LastSeen, bool Online, int PendingMessages);
public sealed record FlyMessage(Guid Id, string Text, DateTimeOffset CreatedAt);
public sealed record SendRequest(Guid UserId, string Text);
public sealed record SendReceipt(Guid MessageId);
public sealed record RelayError(string Error);

public sealed class RelayException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class RelayClient : IDisposable
{
    private readonly HttpClient http;
    public static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("Use an HTTPS relay address (HTTP is allowed only on localhost).");
        return uri;
    }

    public RelayClient(string address, string token)
    {
        http = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = ValidateAddress(address), Timeout = TimeSpan.FromSeconds(10)
        };
        if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task Check(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Access denied. Check the key or reconnect with your invitation.",
            HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
            _ => $"Relay returned {(int)response.StatusCode}."
        };
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try { message = (await response.Content.ReadFromJsonAsync<RelayError>(ct))?.Error ?? message; }
            catch (System.Text.Json.JsonException) { }
        }
        throw new RelayException(message, response.StatusCode);
    }

    public async Task<ClientIdentity> Join(string displayName, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/join", new JoinRequest(displayName), ct);
        await Check(response, ct);
        return (await response.Content.ReadFromJsonAsync<ClientIdentity>(ct))!;
    }
    public async Task<FlyMessage?> Poll(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/inbox", ct);
        await Check(response, ct);
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<FlyMessage>(ct);
    }
    public async Task Acknowledge(Guid messageId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"api/inbox/{messageId}/ack", null, ct);
        await Check(response, ct);
    }
    public async Task<RelayUser[]> Users(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/admin/users", ct);
        await Check(response, ct);
        return (await response.Content.ReadFromJsonAsync<RelayUser[]>(ct))!;
    }
    public async Task<SendReceipt> Send(Guid userId, string text, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/admin/messages", new SendRequest(userId, text), ct);
        await Check(response, ct);
        return (await response.Content.ReadFromJsonAsync<SendReceipt>(ct))!;
    }
    public void Dispose() => http.Dispose();
}
