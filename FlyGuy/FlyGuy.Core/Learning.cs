namespace FlyGuy.Core;

public sealed class Sugar(Vec position)
{
    public Vec Position { get; } = position;
    public double Amount { get; set; } = 1;
}
public interface IPlasticity
{
    void Tick(double[] features, double dopamine, double dt);
    void Tick(double[] features, double reward, double aversion, double stress, double dt);
    double[] Weights { get; }
    double[] ApproachWeights { get; }
    double[] Eligibility { get; }
}
/// <summary>Separate reward and punishment compartments depress opposing KC-like readouts.</summary>
public sealed class RewardPlasticity : IPlasticity
{
    public double[] Weights { get; } = [1, 1, 1, 1];
    public double[] ApproachWeights { get; } = [1, 1, 1, 1];
    public double[] Eligibility { get; } = new double[4];
    public void Tick(double[] features, double dopamine, double dt)
        => Tick(features, dopamine, 0, 0, dt);

    public void Tick(double[] features, double reward, double aversion, double stress, double dt)
    {
        for (int i = 0; i < 4; i++)
        {
            Eligibility[i] = Eligibility[i] * Math.Exp(-dt / 2) + features[i] * (1 - Math.Exp(-dt / .15));
            Eligibility[i] = Math.Min(1, Eligibility[i]);
            Weights[i] = Math.Clamp(Weights[i] * Math.Exp(-.22 * reward * Eligibility[i] * dt), .05, 1);
            ApproachWeights[i] = Math.Clamp(ApproachWeights[i] * Math.Exp(-.65 * aversion * (1 + .5 * stress) * Eligibility[i] * dt), .05, 1);
        }
    }
}
public sealed record LearnedState(int Version, double[] Weights, double[]? ApproachWeights = null);
