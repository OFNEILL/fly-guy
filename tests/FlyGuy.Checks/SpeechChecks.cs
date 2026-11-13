using FlyGuy.Core;

internal static class SpeechChecks
{
    public static void RunAll()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        static List<(double Time, string Word)> Advance(PrimitiveSpeech speech, Brain brain, double seconds)
        {
            var result = new List<(double, string)>();
            for (int i = 0; i < seconds * 120; i++)
            {
                speech.Tick(brain, Simulation.Dt);
                if (speech.TakeWord() is { } word) result.Add((i * Simulation.Dt, word));
            }
            return result;
        }
        var brain = new Brain();
        var speech = new PrimitiveSpeech();
        var words = Advance(speech, brain, 100);
        Require(words.Count == 3 && words.All(w => w.Word == "hungry"), "Persistent hunger should speak occasionally, not spam.");
        Require(words.Zip(words.Skip(1)).All(pair => pair.Second.Time - pair.First.Time >= 45 - Simulation.Dt), "Repeated word cooldown failed.");
        Require(speech.TakeWord() is null, "Speech was delivered more than once.");

        brain.Activity[Brain.SprayContact] = 1;
        words = Advance(speech, brain, 15);
        Require(words.Any(w => w.Word == "ouch"), "Strong aversive activity did not produce ouch.");
        Require(words.All(w => w.Word.All(char.IsAsciiLetterLower)), "Speech must contain exactly one primitive word.");
        speech.Enabled = false;
        Require(Advance(speech, brain, 60).Count == 0, "Disabled speech produced words.");
        speech.Enabled = true;
        Require(Advance(speech, brain, 3).Any(w => w.Word == "ouch"), "Re-enabled speech did not resume.");

        var world = new Habitat([new(0, 0, 800, 600)]);
        var simulation = new Simulation(world);
        var replay = new Simulation(world);
        var heard = new List<string>();
        for (int i = 0; i < 120 * 20; i++)
        {
            simulation.Step(); replay.Step();
            var actual = simulation.Brain.Speech.TakeWord();
            Require(actual == replay.Brain.Speech.TakeWord(), "Brain speech replay is not deterministic.");
            if (actual is not null) heard.Add(actual);
        }
        Require(heard.Count > 0, "The live brain never spoke.");
        simulation.Brain.Speech.Enabled = false;
        simulation.ResetBrain();
        Require(!simulation.Brain.Speech.Enabled && simulation.Brain.Speech.LastWord is null, "Reset did not preserve mute or clear speech state.");
        Console.WriteLine("PASS: primitive neural speech, sustained signals, one-word output, cooldowns, mute, reset and deterministic live-brain replay.");
    }
}
