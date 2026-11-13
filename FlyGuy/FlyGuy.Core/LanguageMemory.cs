using System.Text.RegularExpressions;

namespace FlyGuy.Core;

public sealed record RememberedPhrase(string Text, int Heard, Dictionary<string, double> Replies);
public sealed record LanguageState(int Version, Dictionary<string, int> Words, RememberedPhrase[] Phrases);

/// <summary>Bounded associative vocabulary and phrase memory; no language-model inference.</summary>
public sealed class LanguageMemory
{
    public const int MaximumPhrases = 512, MaximumWords = 1024;
    private readonly Dictionary<string, int> words = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RememberedPhrase> phrases = new(StringComparer.Ordinal);
    public int WordCount => words.Count;
    public int PhraseCount => phrases.Count;
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 200) throw new ArgumentException("Use a phrase of at most 200 characters.");
        var normalized = string.Join(' ', Regex.Matches(text.ToLowerInvariant(), @"\p{L}+(?:'\p{L}+)?").Select(m => m.Value).Take(16));
        if (normalized.Length == 0) throw new ArgumentException("Say or type some words first.");
        if (normalized.Split(' ').Any(word => word.Length > 24)) throw new ArgumentException("Use words of at most 24 letters.");
        return normalized;
    }
    private static string ValidateReply(string reply)
    {
        var word = Normalize(reply);
        if (word.Contains(' ') || word.Length > 24) throw new ArgumentException("Teach one word, up to 24 letters.");
        return word;
    }
    private void RememberWords(string text)
    {
        foreach (var word in text.Split(' '))
        {
            if (words.TryGetValue(word, out int count)) words[word] = Math.Min(1000000, count + 1);
            else if (words.Count < MaximumWords) words[word] = 1;
        }
    }
    private void Associate(string phrase, string reply, double weight)
    {
        RememberWords(phrase); RememberWords(reply);
        if (!phrases.TryGetValue(phrase, out var memory))
        {
            if (phrases.Count >= MaximumPhrases)
                phrases.Remove(phrases.Values.OrderBy(p => p.Heard).ThenBy(p => p.Text, StringComparer.Ordinal).First().Text);
            memory = new(phrase, 0, new(StringComparer.Ordinal));
        }
        if (!memory.Replies.ContainsKey(reply) && memory.Replies.Count >= 16)
            memory.Replies.Remove(memory.Replies.OrderBy(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key);
        memory.Replies[reply] = Math.Min(1000000, memory.Replies.GetValueOrDefault(reply) + weight);
        phrases[phrase] = memory with { Heard = Math.Min(1000000, memory.Heard + 1) };
    }
    public string Hear(string text, string stateWord)
    {
        string phrase = Normalize(text), state = ValidateReply(stateWord);
        var tokens = phrase.Split(' ');
        string? reply = null;
        // Explicit spoken teaching is intentionally simple: "say apple" or "learn apple".
        if (tokens.Length == 2 && tokens[0] is "say" or "learn")
        {
            reply = ValidateReply(tokens[1]);
            Associate(phrase, reply, 8);
        }
        else
        {
            var candidates = new Dictionary<string, double>(StringComparer.Ordinal);
            var matches = phrases.TryGetValue(phrase, out var exact) ? new[] { exact } : phrases.Values.AsEnumerable();
            foreach (var memory in matches)
            {
                double similarity = Similarity(phrase, memory.Text);
                if (similarity < .4) continue;
                foreach (var association in memory.Replies)
                    candidates[association.Key] = candidates.GetValueOrDefault(association.Key) + similarity * association.Value;
            }
            reply = candidates.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).FirstOrDefault();
            reply ??= tokens.Any(t => t is "hello" or "hi" or "hey") ? "hello" : state;
            Associate(phrase, reply, .25);
        }
        return reply;
    }
    private static double Similarity(string left, string right)
    {
        if (left == right) return 4;
        string[] stop = ["a", "the", "is", "are", "you", "i", "it", "my", "do", "to"];
        var a = left.Split(' ').Except(stop).ToHashSet(StringComparer.Ordinal);
        var b = right.Split(' ').Except(stop).ToHashSet(StringComparer.Ordinal);
        int union = a.Union(b).Count();
        return union == 0 ? 0 : (double)a.Intersect(b).Count() / union;
    }
    public void Teach(string phrase, string reply)
    {
        string normalized = Normalize(phrase), word = ValidateReply(reply);
        double strength = phrases.TryGetValue(normalized, out var memory) ? memory.Replies.Values.Max() + 8 : 8;
        Associate(normalized, word, strength);
    }
    public void Clear() { words.Clear(); phrases.Clear(); }
    public LanguageState Export() => new(1, new(words), phrases.Values.OrderBy(p => p.Text, StringComparer.Ordinal)
        .Select(p => p with { Replies = new(p.Replies) }).ToArray());
    public void Import(LanguageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != 1 || state.Words is null || state.Phrases is null || state.Words.Count > MaximumWords || state.Phrases.Length > MaximumPhrases)
            throw new ArgumentException("Invalid language memory.");
        var validated = new LanguageMemory();
        foreach (var pair in state.Words)
        {
            if (ValidateReply(pair.Key) != pair.Key || pair.Value is < 1 or > 1000000) throw new ArgumentException("Invalid stored word.");
            validated.words.Add(pair.Key, pair.Value);
        }
        foreach (var phrase in state.Phrases)
        {
            if (phrase is null || Normalize(phrase.Text) != phrase.Text || phrase.Heard is < 1 or > 1000000 || phrase.Replies is null || phrase.Replies.Count is < 1 or > 16)
                throw new ArgumentException("Invalid stored phrase.");
            foreach (var reply in phrase.Replies)
                if (ValidateReply(reply.Key) != reply.Key || !double.IsFinite(reply.Value) || reply.Value <= 0 || reply.Value > 1000000)
                    throw new ArgumentException("Invalid stored association.");
            validated.phrases.Add(phrase.Text, phrase with { Replies = new(phrase.Replies) });
        }
        Clear();
        foreach (var pair in validated.words) words.Add(pair.Key, pair.Value);
        foreach (var pair in validated.phrases) phrases.Add(pair.Key, pair.Value);
    }
}
