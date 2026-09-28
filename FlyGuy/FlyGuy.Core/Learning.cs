namespace FlyGuy.Core;

public sealed class Sugar(Vec position)
{
    public Vec Position { get; } = position;
    public double Amount { get; set; } = 1;
}
public interface IPlasticity
{
    void Tick(double[] features, double dopamine, double dt);
    double[] Weights { get; }
    double[] Eligibility { get; }
}
/// <summary>Reward depresses active feature-to-avoidance synapses.</summary>
public sealed class RewardPlasticity : IPlasticity
{
    public double[] Weights { get; } = [1, 1, 1, 1];
    public double[] Eligibility { get; } = new double[4];
    public void Tick(double[] features, double dopamine, double dt)
    {
        for (int i = 0; i < 4; i++)
        {
            Eligibility[i] = Eligibility[i] * Math.Exp(-dt / 2) + features[i] * (1 - Math.Exp(-dt / .15));
            Eligibility[i] = Math.Min(1, Eligibility[i]);
            Weights[i] = Math.Clamp(Weights[i] - .22 * dopamine * Eligibility[i] * Weights[i] * dt, .05, 1);
        }
    }
}
public sealed record LearnedState(int Version, double[] Weights);
