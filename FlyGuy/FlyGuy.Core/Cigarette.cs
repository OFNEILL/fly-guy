namespace FlyGuy.Core;

public sealed class Cigarette(Vec position)
{
    public const double BurnSeconds = 60;
    public Vec Position { get; } = position;
    public int PuffsRemaining { get; internal set; } = 8;
    public bool IsLit { get; private set; }
    public double BurnSecondsRemaining { get; private set; } = BurnSeconds;
    public double RemainingFraction => Math.Min(PuffsRemaining / 8.0, BurnSecondsRemaining / BurnSeconds);

    internal void TakePuff()
    {
        IsLit = true;
        PuffsRemaining--;
    }

    internal void Tick(double dt)
    {
        if (!IsLit) return;
        BurnSecondsRemaining = Math.Max(0, BurnSecondsRemaining - dt);
        if (BurnSecondsRemaining <= 0) PuffsRemaining = 0;
    }
}

/// <summary>Fictional gameplay chemistry in relative units, not biological fly chemistry.</summary>
public sealed class NicotineState
{
    public double Nicotine { get; private set; }
    public double Dependence { get; private set; }
    public double Tolerance { get; private set; }
    public double Cortisol { get; private set; }
    public double Epinephrine { get; private set; }
    public double Norepinephrine { get; private set; }
    public double Reward { get; private set; }
    public int Puffs { get; private set; }
    public double Cooldown { get; private set; }
    public double Withdrawal => Dependence * (1 - Nicotine);
    public double Craving => Math.Clamp(.25 + Withdrawal * 1.2 + Dependence * .15 - Nicotine * .5, 0, 1);

    public bool TryPuff()
    {
        if (Cooldown > 0) return false;
        Cooldown = 8;
        Puffs++;
        double response = 1 / (1 + Tolerance);
        Nicotine = Math.Min(1, Nicotine + .8);
        Cortisol = Math.Min(40, Cortisol + 20 * response);
        Epinephrine = Math.Min(40, Epinephrine + 24 * response);
        Norepinephrine = Math.Min(40, Norepinephrine + 24 * response);
        Reward = Math.Min(1, Reward + response);
        Dependence += (1 - Dependence) * .35;
        Tolerance += (1 - Tolerance) * .12;
        return true;
    }

    public void Tick(double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        Cooldown = Math.Max(0, Cooldown - dt);
        Nicotine *= Math.Exp(-dt / 10);
        Cortisol *= Math.Exp(-dt / 35);
        Epinephrine *= Math.Exp(-dt / 5);
        Norepinephrine *= Math.Exp(-dt / 9);
        Reward *= Math.Exp(-dt / 3);
        Dependence *= Math.Exp(-dt / 1800);
        Tolerance *= Math.Exp(-dt / 300);
    }
}
