using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using FlyGuy.Core;

namespace FlyGuy.Desktop;

internal sealed class FlyConversation : IDisposable
{
    private readonly FlyMicrophone microphone;
    private readonly Func<Brain> brain;
    private readonly Action<string> respond;
    private readonly Func<bool> canRespond;
    private readonly string memoryPath;
    private DateTime nextReply;
    public LanguageMemory Memory { get; } = new();
    public DesktopActions Actions { get; } = new();
    public bool IsListening => microphone.IsListening;
    public string Status { get; private set; } = "Microphone off. Start listening or type a phrase.";
    public string LastHeard { get; private set; } = "";
    public string LastReply { get; private set; } = "";
    public event Action? Changed;

    public FlyConversation(Dispatcher dispatcher, Func<Brain> brain, Action<string> respond, Func<bool> canRespond, string? memoryPath = null)
    {
        this.brain = brain; this.respond = respond; this.canRespond = canRespond;
        this.memoryPath = memoryPath ?? Path.Combine(RelayConnection.SettingsDirectory, "language-memory.dat");
        microphone = new(dispatcher, () =>
        {
            var state = Memory.Export();
            return state.Phrases.Select(p => p.Text).Concat(state.Words.Keys).ToArray();
        });
        microphone.Heard += text => Hear(text, false);
        microphone.StatusChanged += message => { Status = message; Changed?.Invoke(); };
        try
        {
            if (File.Exists(this.memoryPath))
            {
                if (new FileInfo(this.memoryPath).Length > 2000000) throw new ArgumentException("Language memory is too large.");
                var json = RelayConnection.Unprotect(File.ReadAllText(this.memoryPath));
                Memory.Import(JsonSerializer.Deserialize<LanguageState>(json) ?? throw new ArgumentException("Empty memory."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or FormatException or JsonException or ArgumentException)
        { Status = "Saved language memory could not be opened. You can teach new words or clear memory."; }
    }
    private string Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(memoryPath)!);
            string temporary = memoryPath + ".tmp";
            File.WriteAllText(temporary, RelayConnection.Protect(JsonSerializer.Serialize(Memory.Export())));
            File.Move(temporary, memoryPath, true);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { return " Memory changed this session but could not be saved to disk."; }
    }
    public void StartListening() => microphone.Start();
    public void StopListening() => microphone.Stop();
    public void SuppressVoice() => microphone.SuppressVoice();
    public void TickActions(bool held, bool cursorNearby)
    {
        var b = brain();
        var result = Actions.TickAutonomous(canRespond(), held, b.Drives[0], b.Threat.AcuteArousal, cursorNearby);
        if (result is null) return;
        Status = result; Changed?.Invoke();
    }
    public void Hear(string text) => Hear(text, true);
    private void Hear(string text, bool typed)
    {
        if (!canRespond() || !brain().Speech.Enabled)
        { Status = "Resume the fly and enable Primitive speech to talk with it. Owner messages take priority."; Changed?.Invoke(); return; }
        if (Actions.TryRun(text, typed, out var actionStatus))
        {
            LastHeard = text; LastReply = ""; Status = actionStatus;
            Changed?.Invoke(); return;
        }
        if (DateTime.UtcNow < nextReply) return;
        try
        {
            LastHeard = LanguageMemory.Normalize(text);
            LastReply = Memory.Hear(LastHeard, PrimitiveSpeech.Describe(brain()));
            nextReply = DateTime.UtcNow.AddSeconds(4);
            string warning = Save();
            respond(LastReply);
            Status = $"Heard: {LastHeard}  →  Fly: {LastReply}." + warning;
        }
        catch (ArgumentException ex) { Status = ex.Message; }
        Changed?.Invoke();
    }
    public void Teach(string phrase, string reply)
    {
        try
        {
            Memory.Teach(phrase, reply);
            string warning = Save();
            if (microphone.IsListening) { microphone.Stop(); microphone.Start(); }
            Status = "Learned. Say that phrase again to hear its new one-word reply." + warning;
        }
        catch (ArgumentException ex) { Status = ex.Message; }
        Changed?.Invoke();
    }
    public void Clear()
    {
        bool listening = microphone.IsListening;
        if (listening) microphone.Stop();
        Memory.Clear(); LastHeard = LastReply = "";
        string warning = Save();
        if (listening) microphone.Start();
        Status = "Word and phrase memory cleared." + warning;
        Changed?.Invoke();
    }
    public void Dispose() => microphone.Dispose();
}
