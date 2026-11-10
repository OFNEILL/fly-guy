using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyGuy.Messaging;

namespace FlyGuy.Desktop;

internal sealed record RelaySettings(string Address, string DisplayName, string ProtectedToken, bool Enabled, Guid? LastMessageId = null);
internal sealed record LocalRelayKeys(string ProtectedAdminKey, string ProtectedInviteKey);

internal sealed class RelayConnection : IDisposable
{
    public static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlyGuy");
    private static string SettingsPath => Path.Combine(SettingsDirectory, "messaging.json");
    public static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    public static LocalRelayKeys? LocalKeys() => File.Exists(Path.Combine(SettingsDirectory, "relay-owner.json"))
        ? JsonSerializer.Deserialize<LocalRelayKeys>(File.ReadAllText(Path.Combine(SettingsDirectory, "relay-owner.json"))) : null;
    private CancellationTokenSource? receiving;
    public RelaySettings? Settings { get; private set; }
    public string Status { get; private set; } = "Fly messages are off. Connect to receive messages from the fly.";
    public event Action? StatusChanged;
    public event Action<FlyMessage>? MessageReceived;
    public RelayConnection()
    {
        try { if (File.Exists(SettingsPath)) Settings = JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(SettingsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = "Saved messaging settings could not be read. Reconnect to set them up again."; }
    }
    private void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Settings));
        File.Move(temporary, SettingsPath, true);
    }
    private void SetStatus(string message) { Status = message; StatusChanged?.Invoke(); }
    public async Task Connect(string address, string displayName, string invitation)
    {
        string normalized = RelayClient.ValidateAddress(address).AbsoluteUri;
        if (invitation.Length > 0)
        {
            using var join = new RelayClient(normalized, invitation);
            var identity = await join.Join(displayName);
            Settings = new(normalized, identity.DisplayName, Protect(identity.Token), true);
        }
        else if (Settings is { } saved && saved.Address == normalized)
            Settings = saved with { Enabled = true };
        else throw new ArgumentException("Enter an invitation key to join this relay.");
        Save(); Start();
    }
    public void Start()
    {
        if (Settings is not { Enabled: true } settings) return;
        receiving?.Cancel();
        var cancellation = receiving = new();
        _ = Receive(settings, cancellation);
    }
    private async Task Receive(RelaySettings settings, CancellationTokenSource cancellation)
    {
        try
        {
            using var client = new RelayClient(settings.Address, Unprotect(settings.ProtectedToken));
            while (!cancellation.IsCancellationRequested)
            {
                int delaySeconds = 5;
                try
                {
                    var message = await client.Poll(cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    SetStatus($"Connected as {settings.DisplayName}. Waiting for fly messages.");
                    if (message is not null)
                    {
                        if (Settings?.LastMessageId != message.Id)
                        {
                            MessageReceived?.Invoke(message);
                            Settings = Settings! with { LastMessageId = message.Id };
                            Save();
                        }
                        await client.Acknowledge(message.Id, cancellation.Token);
                        delaySeconds = 12;
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch (RelayException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                { SetStatus("Recipient key was rejected. Enter an invitation to reconnect."); break; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RelayException or JsonException)
                { SetStatus("Relay unavailable; reconnecting automatically."); delaySeconds = 15; }
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        { if (!cancellation.IsCancellationRequested) SetStatus("Messaging settings could not be opened. Reconnect with your invitation."); }
        finally { cancellation.Dispose(); if (ReferenceEquals(receiving, cancellation)) receiving = null; }
    }
    public void Disconnect()
    {
        receiving?.Cancel();
        if (Settings is not null) { Settings = Settings with { Enabled = false }; Save(); }
        SetStatus("Fly messages are off.");
    }
    public void Dispose() { receiving?.Cancel(); }
}
