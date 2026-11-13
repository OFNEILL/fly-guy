using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlyGuy.Messaging;

namespace FlyGuy.Desktop;

internal sealed record RelaySettings(string Address, string DisplayName, string ProtectedToken, bool Enabled, Guid? LastMessageId = null);
internal sealed record LocalRelayKeys(string ProtectedAdminKey);

internal sealed class RelayConnection : IDisposable
{
    public const string RelayAddress = "https://fly-guy-relay.vercel.app/";
    public static string DefaultDisplayName()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address).Where(a => !IPAddress.IsLoopback(a)).ToArray();
            return (addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                ?? IPAddress.Loopback).ToString().Split('%')[0];
        }
        catch (NetworkInformationException) { return IPAddress.Loopback.ToString(); }
    }
    public static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlyGuy");
    private static string SettingsPath => Path.Combine(SettingsDirectory, "messaging.json");
    public static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    public static LocalRelayKeys? LocalKeys() => File.Exists(Path.Combine(SettingsDirectory, "relay-owner.json"))
        ? JsonSerializer.Deserialize<LocalRelayKeys>(File.ReadAllText(Path.Combine(SettingsDirectory, "relay-owner.json"))) : null;
    private CancellationTokenSource? receiving;
    public RelaySettings? Settings { get; private set; }
    public string Status { get; private set; } = "Connecting to Fly messages automatically.";
    public event Action? StatusChanged;
    public event Action<FlyMessage>? MessageReceived;
    public RelayConnection()
    {
        try { if (File.Exists(SettingsPath)) Settings = JsonSerializer.Deserialize<RelaySettings>(File.ReadAllText(SettingsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Status = "Saved messaging settings could not be read. Reconnect to set them up again."; }
        var name = DefaultDisplayName();
        Settings = Settings is { } saved && saved.Address == RelayAddress && saved.DisplayName == name
            ? saved : new(RelayAddress, name, "", Settings?.Enabled ?? true);
        if (!Settings.Enabled) Status = "Fly messages are off.";
    }
    private void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Settings));
        File.Move(temporary, SettingsPath, true);
    }
    private void SetStatus(string message) { Status = message; StatusChanged?.Invoke(); }
    public Task Connect()
    {
        Settings = (Settings ?? new(RelayAddress, DefaultDisplayName(), "", true)) with { Enabled = true };
        Save(); Start();
        return Task.CompletedTask;
    }
    public void Start()
    {
        if (Settings is not { Enabled: true } settings) return;
        receiving?.Cancel();
        var cancellation = receiving = new();
        SetStatus("Connecting to Fly messages automatically.");
        _ = Receive(settings, cancellation);
    }
    private async Task Receive(RelaySettings settings, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                int delaySeconds = 5;
                try
                {
                    if (string.IsNullOrEmpty(settings.ProtectedToken))
                    {
                        using var join = new RelayClient(RelayAddress, "");
                        var identity = await join.Join(DefaultDisplayName(), cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        settings = new(RelayAddress, identity.DisplayName, Protect(identity.Token), true);
                        Settings = settings;
                        Save();
                    }
                    using var client = new RelayClient(RelayAddress, Unprotect(settings.ProtectedToken));
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
                {
                    settings = settings with { ProtectedToken = "", LastMessageId = null };
                    Settings = settings; Save();
                    SetStatus("Refreshing the recipient connection automatically.");
                }
                catch (Exception ex) when (ex is CryptographicException or FormatException)
                {
                    settings = settings with { ProtectedToken = "", LastMessageId = null };
                    Settings = settings; Save();
                    SetStatus("Restoring the recipient connection automatically.");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or RelayException or JsonException)
                { SetStatus("Relay unavailable; reconnecting automatically."); delaySeconds = 15; }
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        { if (!cancellation.IsCancellationRequested) { Settings = null; SetStatus("Messaging settings could not be opened. Click Connect to register again."); } }
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
