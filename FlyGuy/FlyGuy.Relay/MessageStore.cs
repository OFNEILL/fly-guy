using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyGuy.Messaging;

namespace FlyGuy.Relay;

public sealed class MessageStore
{
    public sealed record User(Guid Id, string DisplayName, string TokenHash, DateTimeOffset LastSeen);
    public sealed record Pending(Guid UserId, FlyMessage Message);
    public sealed record State(List<User> Users, List<Pending> Messages);
    private readonly object gate = new();
    private readonly string path;
    private readonly TimeProvider clock;
    private State state;
    public MessageStore(string path, TimeProvider? clock = null)
    {
        this.path = Path.GetFullPath(path);
        this.clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
        state = File.Exists(this.path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(this.path))
            ?? throw new InvalidDataException("Invalid relay state") : new([], []);
        // Presence must be re-established after a relay restart.
        state.Users.ReplaceAll(u => u with { LastSeen = DateTimeOffset.MinValue });
    }
    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public static bool Matches(string? supplied, string expected) => supplied is { Length: <= 256 } &&
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(supplied)), Convert.FromHexString(Hash(expected)));
    private void Save()
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, path, true);
    }
    private void Prune() => state.Messages.RemoveAll(m => clock.GetUtcNow() - m.Message.CreatedAt >= TimeSpan.FromHours(24));
    public ClientIdentity Join(string? displayName)
    {
        string name = displayName?.Trim() ?? "";
        if (name.Length is < 1 or > 40 || name.Any(char.IsControl)) throw new ArgumentException("Choose a name of 1-40 visible characters.");
        lock (gate)
        {
            if (state.Users.Count >= 5000) throw new InvalidOperationException("Relay user limit reached.");
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var user = new User(Guid.NewGuid(), name, Hash(token), clock.GetUtcNow());
            state.Users.Add(user); Save();
            return new(user.Id, token, name);
        }
    }
    public Guid? Authenticate(string? token)
    {
        if (token is not { Length: 64 }) return null;
        string hash = Hash(token);
        lock (gate) return state.Users.FirstOrDefault(u => u.TokenHash == hash)?.Id;
    }
    public RelayUser[] Users()
    {
        lock (gate)
        {
            Prune();
            return state.Users.Select(u => new RelayUser(u.Id, u.DisplayName, u.LastSeen,
                clock.GetUtcNow() - u.LastSeen < TimeSpan.FromSeconds(45),
                state.Messages.Count(m => m.UserId == u.Id))).OrderByDescending(u => u.Online).ThenBy(u => u.DisplayName).ToArray();
        }
    }
    public FlyMessage? Poll(Guid userId)
    {
        lock (gate)
        {
            int index = state.Users.FindIndex(u => u.Id == userId);
            if (index < 0) throw new ArgumentException("Unknown recipient.");
            state.Users[index] = state.Users[index] with { LastSeen = clock.GetUtcNow() };
            Prune();
            return state.Messages.FirstOrDefault(m => m.UserId == userId)?.Message;
        }
    }
    public Guid Send(Guid userId, string? text)
    {
        string value = text?.Trim() ?? "";
        if (value.Length is < 1 or > 500 || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Messages must contain 1-500 characters.");
        lock (gate)
        {
            Prune();
            if (!state.Users.Any(u => u.Id == userId)) throw new KeyNotFoundException("Recipient no longer exists.");
            if (state.Messages.Count(m => m.UserId == userId) >= 50) throw new InvalidOperationException("Recipient inbox is full.");
            var message = new FlyMessage(Guid.NewGuid(), value, clock.GetUtcNow());
            state.Messages.Add(new(userId, message)); Save();
            return message.Id;
        }
    }
    public bool Acknowledge(Guid userId, Guid messageId)
    {
        lock (gate)
        {
            bool removed = state.Messages.RemoveAll(m => m.UserId == userId && m.Message.Id == messageId) > 0;
            if (removed) Save();
            return removed;
        }
    }
}

internal static class ListExtensions
{
    public static void ReplaceAll<T>(this List<T> values, Func<T, T> transform)
    {
        for (int i = 0; i < values.Count; i++) values[i] = transform(values[i]);
    }
}
