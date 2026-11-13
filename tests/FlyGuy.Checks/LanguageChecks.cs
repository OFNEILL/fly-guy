using FlyGuy.Core;
using System.Text.Json;

internal static class LanguageChecks
{
    public static void RunAll()
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var memory = new LanguageMemory();
        Require(LanguageMemory.Normalize(" Hello, LITTLE fly! ") == "hello little fly", "Phrase normalization failed.");
        Require(memory.Hear("Hello fly", "hungry") == "hello", "Greeting failed.");
        Require(memory.Hear("Hello fly", "scared") == "hello", "Greeting memory did not persist.");
        Require(memory.Hear("say apple", "sleep") == "apple", "Spoken teaching failed.");
        Require(memory.Hear("apple", "hungry") == "apple", "Learned word did not generalize.");
        memory.Teach("red apple", "fruit");
        Require(memory.Hear("big red apple", "scared") == "fruit", "Phrase association did not generalize.");
        memory.Teach("red apple", "snack");
        Require(memory.Hear("red apple", "hungry") == "snack", "Correcting a taught reply failed.");
        Require(memory.Hear("what now", "ouch") == "ouch", "Unknown speech did not use the brain state.");
        Require(memory.Hear("what now", "sleep") == "ouch", "Context association was not remembered.");
        var replay = new LanguageMemory();
        replay.Import(JsonSerializer.Deserialize<LanguageState>(JsonSerializer.Serialize(memory.Export()))!);
        Require(replay.Hear("red apple", "hungry") == "snack", "Saved phrase did not survive JSON round trip.");
        var exported = replay.Export();
        exported.Words.Clear(); exported.Phrases[0].Replies.Clear();
        Require(replay.WordCount > 0 && replay.Hear("red apple", "hungry") == "snack", "Exported state aliases live memory.");
        int count = replay.PhraseCount;
        try { replay.Import(new(99, [], [])); throw new Exception("Invalid version accepted."); }
        catch (ArgumentException) { }
        Require(replay.PhraseCount == count, "Invalid import damaged existing memory.");
        try { replay.Teach("test", "two words"); throw new Exception("Multiword reply accepted."); }
        catch (ArgumentException) { }
        for (int i = 0; i < 1300; i++)
        {
            string word = "word" + (char)('a' + i % 26) + (char)('a' + i / 26 % 26) + (char)('a' + i / 676);
            replay.Teach("cue " + word, word);
        }
        Require(replay.PhraseCount == LanguageMemory.MaximumPhrases && replay.WordCount <= LanguageMemory.MaximumWords, "Language memory grew without bounds.");
        var bounded = new LanguageMemory(); bounded.Import(replay.Export());
        Require(bounded.PhraseCount == replay.PhraseCount, "Bounded memory cannot be restored.");
        replay.Clear();
        Require(replay.WordCount == 0 && replay.PhraseCount == 0, "Clear retained language memory.");
        Console.WriteLine("PASS: learned words, phrase association, grounded replies, spoken teaching, correction, persistence, isolation, bounds and clearing.");
    }
}
