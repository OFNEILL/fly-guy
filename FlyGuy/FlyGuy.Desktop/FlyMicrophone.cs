using System.Speech.Recognition;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace FlyGuy.Desktop;

internal sealed class FlyMicrophone(Dispatcher dispatcher, Func<string[]>? knownPhrases = null) : IDisposable
{
    public const float MinimumConfidence = .4f;
    private SpeechRecognitionEngine? engine;
    private DateTime suppressUntil;
    private int generation;
    public bool IsListening => engine is not null;
    public event Action<string>? Heard;
    public event Action<string>? StatusChanged;
    public void SuppressVoice() => suppressUntil = DateTime.UtcNow.AddMilliseconds(900);

    public void Start()
    {
        if (engine is not null) return;
        SpeechRecognitionEngine? candidate = null;
        int current = ++generation;
        try
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            var recognizer = installed.FirstOrDefault(r => r.Culture.Name == System.Globalization.CultureInfo.CurrentUICulture.Name)
                ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                ?? installed.FirstOrDefault();
            if (recognizer is null) throw new InvalidOperationException("Install a Windows speech-recognition language in Settings > Time & language > Speech, then try again.");
            candidate = new(recognizer);
            candidate.LoadGrammar(new DictationGrammar());
            var known = knownPhrases?.Invoke().Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().Take(1536).ToArray();
            if (known is { Length: > 0 })
            {
                var grammar = new GrammarBuilder(new Choices(known)) { Culture = recognizer.Culture };
                candidate.LoadGrammar(new Grammar(grammar));
            }
            candidate.SpeechRecognized += (_, args) =>
            {
                string text = args.Result.Text;
                float confidence = args.Result.Confidence;
                DateTime began = args.Result.Audio?.StartTime.ToUniversalTime() ?? DateTime.UtcNow;
                dispatcher.BeginInvoke(() =>
                {
                    if (current != generation || engine is null || DateTime.UtcNow < suppressUntil || began <= suppressUntil) return;
                    if (confidence < MinimumConfidence) { StatusChanged?.Invoke("Not sure what you said. Try again clearly."); return; }
                    Heard?.Invoke(text);
                });
            };
            candidate.RecognizeCompleted += (_, args) => dispatcher.BeginInvoke(() =>
            {
                if (current != generation || engine is null) return;
                Stop();
                StatusChanged?.Invoke(args.Error is null ? "Microphone stopped." : "Microphone disconnected or recognition stopped. Check your input device and start listening again.");
            });
            candidate.SetInputToDefaultAudioDevice();
            engine = candidate;
            candidate.RecognizeAsync(RecognizeMode.Multiple);
            StatusChanged?.Invoke($"Listening locally ({recognizer.Culture.DisplayName}). Speak a short phrase.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or UnauthorizedAccessException or ArgumentException)
        {
            engine = null; generation++;
            candidate?.Dispose();
            StatusChanged?.Invoke("Cannot listen: " + ex.Message + " Check Windows microphone permissions and your default input device.");
        }
    }
    public void Stop()
    {
        generation++;
        var previous = engine; engine = null;
        if (previous is not null)
        {
            try { previous.RecognizeAsyncCancel(); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
            previous.Dispose();
        }
        StatusChanged?.Invoke("Microphone off.");
    }
    public void Dispose() => Stop();
}
