namespace FlyGuy.Core;

/// <summary>A fictional, nonlethal irritant field, not an insecticide toxicity model.</summary>
public sealed class SprayCloud(Vec position, double radius = 110)
{
    public Vec Position { get; } = position;
    public double Radius { get; } = double.IsFinite(radius) && radius > 0 ? radius : throw new ArgumentOutOfRangeException(nameof(radius));
    public double Concentration { get; private set; } = 1;
    public double Sample(Vec point)
    {
        double distance = (point - Position).Length / Radius;
        return distance >= 1 ? 0 : Concentration * Math.Pow(1 - distance * distance, 2);
    }
    public void Tick(double dt) => Concentration *= Math.Exp(-dt / 1.5);
}

/// <summary>Model parameters in seconds; chosen for interactive experiments, not measured fly kinetics.</summary>
public sealed record ThreatSettings
{
    public double AcuteDecaySeconds { get; init; } = 2;
    public double StressDecaySeconds { get; init; } = 35;
    public double AversionDecaySeconds { get; init; } = 1.2;
    public double SensitizationDecaySeconds { get; init; } = 180;

    internal void Validate()
    {
        foreach (double value in new[] { AcuteDecaySeconds, StressDecaySeconds, AversionDecaySeconds, SensitizationDecaySeconds })
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Decay times must be finite and positive.");
    }
}

public sealed class ThreatModulation
{
    public double AcuteArousal { get; private set; }
    public double Stress { get; private set; }
    public double Aversion { get; private set; }
    public double Sensitization { get; private set; }

    // Exact bounded integration of dx/dt = input * rise * (1-x) - x / decay.
    private static double Integrate(double value, double input, double rise, double decay, double dt)
    {
        double rate = input * rise + 1 / decay;
        double target = input * rise / rate;
        return target + (value - target) * Math.Exp(-rate * dt);
    }

    public void Tick(double punishment, double learnedThreat, ThreatSettings settings, double dt)
    {
        Aversion = Integrate(Aversion, punishment, 8, settings.AversionDecaySeconds, dt);
        AcuteArousal = Integrate(AcuteArousal, Math.Max(punishment, learnedThreat), 10, settings.AcuteDecaySeconds, dt);
        Stress = Integrate(Stress, punishment, .35, settings.StressDecaySeconds, dt);
        Sensitization = Integrate(Sensitization, punishment, .06, settings.SensitizationDecaySeconds, dt);
    }
}
