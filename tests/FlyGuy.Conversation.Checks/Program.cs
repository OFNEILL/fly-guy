using System.IO;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Windows.Threading;
using FlyGuy.Core;
using FlyGuy.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        var actionTime = DateTime.UtcNow;
        var executed = new List<DesktopAction>();
        var actions = new DesktopActions(action => { executed.Add(action); return "done"; }, () => actionTime);
        Require(DesktopActions.Parse("open notepad & del file") is null, "Accepted arbitrary command.");
        actions.TryRun("open notepad", true, out _);
        Require(executed.Count == 0, "Disabled action executed.");
        actions.Enabled = true;
        actions.TryRun("open notepad", false, out _);
        Require(executed.Count == 0, "Voice executed desktop action.");
        Require(actions.AutonomousEnabled, "Autonomous actions should be enabled by default.");
        actions.TickAutonomous(true, false, 1, 0, false);
        Require(executed.Count == 0, "Autonomy skipped startup delay.");
        actionTime = actionTime.AddSeconds(31);
        actions.TickAutonomous(false, false, 1, 0, false);
        actions.TickAutonomous(true, true, 1, 0, false);
        Require(executed.Count == 0, "Paused or held fly acted.");
        actions.TickAutonomous(true, false, 1, 0, false);
        Require(executed.SequenceEqual(new[] { DesktopAction.OpenNotepad }), "Curious fly did not open Notepad.");
        actions.TickAutonomous(true, false, 0, 1, true);
        Require(executed.Count == 1, "Autonomy skipped cooldown.");
        actionTime = actionTime.AddSeconds(61);
        actions.TickAutonomous(true, false, 1, 0, false);
        Require(executed.Count == 1, "Opened Notepad more than once.");
        actions.TickAutonomous(true, false, 0, 1, false);
        Require(executed.Count == 1, "Moved distant cursor.");
        actions.TickAutonomous(true, false, 0, 1, true);
        Require(executed.Last() == DesktopAction.MoveCursor, "Alarmed fly did not move nearby cursor.");
        actionTime = actionTime.AddSeconds(61);
        actions.AutonomousEnabled = false;
        actions.TickAutonomous(true, false, 0, 1, true);
        Require(executed.Count == 2, "Disabled autonomy executed.");
        Console.WriteLine("PASS: desktop action allowlist, typed input gate, autonomy delay, pause/held gates, cooldown, single launch and cursor proximity.");
        if (Environment.GetCommandLineArgs().Contains("--actions-only")) return 0;
        string directory = Path.Combine(Path.GetTempPath(), "FlyGuy-language-check-" + Guid.NewGuid());
        string path = Path.Combine(directory, "memory.dat");
        Directory.CreateDirectory(directory);
        try
        {
            var brain = new Brain();
            using (var first = new FlyConversation(Dispatcher.CurrentDispatcher, () => brain, _ => { }, () => true, path))
            {
                first.Teach("red apple", "snack");
                Require(!File.ReadAllText(path).Contains("apple", StringComparison.Ordinal), "Language memory is not encrypted.");
            }
            string? reply = null;
            using (var restored = new FlyConversation(Dispatcher.CurrentDispatcher, () => brain, word => reply = word, () => true, path))
            {
                restored.Hear("red apple");
                Require(reply == "snack", "Taught response did not survive process-style reload.");
                restored.Clear();
            }
            using (var cleared = new FlyConversation(Dispatcher.CurrentDispatcher, () => brain, _ => { }, () => false, path))
            {
                Require(cleared.Memory.PhraseCount == 0, "Cleared memory was restored.");
                cleared.Hear("hello fly");
                Require(cleared.Memory.PhraseCount == 0, "Paused conversation learned speech.");
            }
            Console.WriteLine("PASS: DPAPI persistence, taught response after reload, clearing and pause gating.");

            using var microphone = new FlyMicrophone(Dispatcher.CurrentDispatcher);
            string status = "";
            microphone.StatusChanged += message => status = message;
            microphone.Start();
            bool opened = microphone.IsListening;
            Console.WriteLine(opened ? "PASS: default microphone opened successfully." : "MICROPHONE UNAVAILABLE: " + status);
            microphone.Stop();
            Require(!microphone.IsListening, "Microphone did not stop.");

            var info = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en");
            if (info is null) { Console.WriteLine("RECOGNITION UNAVAILABLE: install an English Windows speech-recognition language."); return 0; }
            using var wave = new MemoryStream();
            using (var synth = new SpeechSynthesizer())
            {
                Console.WriteLine($"Recognition culture: {info.Culture}; voice: {synth.Voice.Name} ({synth.Voice.Culture}).");
                synth.SetOutputToWaveStream(wave);
                synth.Speak("hello how are you today");
            }
            wave.Position = 0;
            using var recognizer = new SpeechRecognitionEngine(info);
            recognizer.LoadGrammar(new DictationGrammar());
            recognizer.SetInputToWaveStream(wave);
            var result = recognizer.Recognize(TimeSpan.FromSeconds(8));
            if (result is null || string.IsNullOrWhiteSpace(result.Text)) throw new Exception("Synthesized speech was not recognized.");
            Console.WriteLine($"PASS: local audio-to-text recognition: {result.Text} (confidence {result.Confidence:F2}).");
            Require(result.Confidence >= FlyMicrophone.MinimumConfidence, "Recognized audio did not meet the app's confidence threshold.");
            using (var endToEnd = new FlyConversation(Dispatcher.CurrentDispatcher, () => brain, word => reply = word, () => true, path))
            {
                endToEnd.Teach(result.Text, "friend");
                endToEnd.Hear(result.Text);
                Require(reply == "friend", "Recognized speech did not produce a learned reply.");
                using var output = new MemoryStream();
                using var speaker = new SpeechSynthesizer();
                speaker.SetOutputToWaveStream(output);
                speaker.Speak(reply);
                Require(output.Length > 44, "Response did not produce speech audio.");
            }
            Console.WriteLine("PASS: audio recognition → learned response → synthesized speech audio.");
            return 0;
        }
        finally { Directory.Delete(directory, true); }
    }
}
