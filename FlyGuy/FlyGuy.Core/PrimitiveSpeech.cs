namespace FlyGuy.Core;

/// <summary>Fictional one-word readout of sustained neural and internal state, not a language model.</summary>
public sealed class PrimitiveSpeech
{
    private static readonly string[] vocabulary = ["ouch", "scared", "yum", "food", "hungry", "sleep", "stop", "go", "buzz"];
    private readonly double[] activation = new double[vocabulary.Length];
    private readonly double[] lastSpoken = Enumerable.Repeat(double.NegativeInfinity, vocabulary.Length).ToArray();
    private double time, nextSpeech = 2, stableFor;
    private int candidate = -1;
    private string? pending;
    public bool Enabled { get; set; } = true;
    public string? LastWord { get; private set; }
    private static double Signal(Brain brain, int index) => index switch
    {
        0 => Math.Max(brain.Activity[Brain.SprayContact], brain.Threat.Aversion) * 1.4,
        1 => Math.Max(brain.Threat.AcuteArousal, brain.Activity[15] - .25) * 1.1,
        2 => Math.Max(brain.Activity[27], brain.Dopamine) * 1.2,
        3 => Math.Max(brain.Activity[20], Math.Max(brain.Activity[21], brain.Activity[22])) * .95,
        4 => brain.Hunger * .85,
        5 => Math.Max(brain.Drives[1], 1 - brain.Energy),
        6 => brain.Activity[24] * .95,
        7 => brain.Activity[16] * .7,
        _ => brain.Drives[0] * .7
    };
    public static string Describe(Brain brain) => vocabulary[Enumerable.Range(0, vocabulary.Length).MaxBy(i => Signal(brain, i))];

    public string? TakeWord()
    {
        var word = pending;
        pending = null;
        return word;
    }

    public void Tick(Brain brain, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        time += dt;
        if (!Enabled) { pending = null; stableFor = 0; candidate = -1; return; }
        double smoothing = 1 - Math.Exp(-dt / .3);
        int strongest = 0;
        for (int i = 0; i < vocabulary.Length; i++)
        {
            activation[i] += (Math.Clamp(Signal(brain, i), 0, 1.4) - activation[i]) * smoothing;
            if (activation[i] > activation[strongest]) strongest = i;
        }
        if (strongest != candidate) { candidate = strongest; stableFor = 0; }
        stableFor += dt;
        if (activation[strongest] < .35 || stableFor < .6 || time < nextSpeech || time - lastSpoken[strongest] < 45) return;
        pending = LastWord = vocabulary[strongest];
        lastSpoken[strongest] = time;
        nextSpeech = time + 14;
    }
}
