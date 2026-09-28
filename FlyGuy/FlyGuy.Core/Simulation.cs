namespace FlyGuy.Core;

public readonly record struct Vec(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double b) => new(a.X * b, a.Y * b);
    public static Vec Direction(double angle) => new(Math.Cos(angle), Math.Sin(angle));
}
public readonly record struct Region(double X, double Y, double Width, double Height)
{
    public bool Contains(Vec p) => p.X >= X && p.Y >= Y && p.X <= X + Width && p.Y <= Y + Height;
    public Vec Clamp(Vec p) => new(Math.Clamp(p.X, X, X + Width), Math.Clamp(p.Y, Y, Y + Height));
}
public sealed class Habitat(Region[] regions)
{
    public Region[] Regions { get; } = regions.Length > 0 ? regions : throw new ArgumentException("A monitor is required");
    public bool Contains(Vec p) => Regions.Any(r => r.Contains(p));
    public Vec Constrain(Vec p) => Contains(p) ? p : Regions.Select(r => r.Clamp(p)).MinBy(q => (q - p).Length);
    public double Edge(Vec p, double angle)
    {
        for (var d = 8; d <= 160; d += 8)
            if (!Contains(p + Vec.Direction(angle) * d)) return 1 - (d - 8) / 160.0;
        return 0;
    }
}
public sealed class Creature
{
    public Vec Position { get; set; }
    public Vec Velocity { get; set; }
    public double Heading { get; set; }
    public double Gait { get; set; }
}
public sealed class Sensors
{
    public static readonly string[] Names = ["Cursor left", "Cursor right", "Edge left", "Edge right", "Edge front", "Speed", "Moving", "Novelty", "Quiet time", "Heading sin", "Heading cos"];
    public double[] Values { get; } = new double[Names.Length];
    private Vec? previousCursor;
    private double quiet;
    public void Sample(Creature c, Habitat world, Vec cursor, double dt)
    {
        var offset = cursor - c.Position;
        var relative = Math.Atan2(offset.Y, offset.X) - c.Heading;
        var proximity = Math.Exp(-offset.Length / 280);
        var novelty = previousCursor is { } last ? Math.Clamp((cursor - last).Length / (dt * 1000), 0, 1) * proximity : 0;
        previousCursor = cursor;
        quiet = novelty > .08 ? 0 : quiet + dt;
        Values[0] = proximity * (.5 - .5 * Math.Sin(relative));
        Values[1] = proximity * (.5 + .5 * Math.Sin(relative));
        Values[2] = world.Edge(c.Position, c.Heading - .65);
        Values[3] = world.Edge(c.Position, c.Heading + .65);
        Values[4] = world.Edge(c.Position, c.Heading);
        Values[5] = Math.Clamp(c.Velocity.Length / 150, 0, 1);
        Values[6] = 1 - Math.Exp(-c.Velocity.Length / 15);
        Values[7] = novelty;
        Values[8] = 1 - Math.Exp(-quiet / 12);
        Values[9] = .5 + .5 * Math.Sin(c.Heading);
        Values[10] = .5 + .5 * Math.Cos(c.Heading);
    }
}
public readonly record struct Synapse(int From, int To, double Weight);
public readonly record struct MotorActivity(double Forward, double Turn, double Brake);

/// <summary>Synthetic population rate model. No measured connectome weights.</summary>
public sealed class Brain
{
    public string[] Names { get; } = [.. Sensors.Names, "Explore L", "Explore R", "Orient L", "Orient R", "Threat", "Forward", "Turn L", "Turn R", "Brake"];
    public double[] Activity { get; } = new double[20];
    public double[] Drives { get; } = [.6, 0, .3, 0, .5, 0];
    public static readonly string[] DriveNames = ["Curiosity", "Fatigue", "Arousal", "Startle", "Attraction", "Habituation"];
    public List<Synapse> Connections { get; } = [];
    private readonly double[] adaptation = new double[20], next = new double[20], noise = new double[20];
    private readonly Random random;
    public Brain(int seed = 7)
    {
        random = new(seed);
        void Link(int a, int b, double w) => Connections.Add(new(a, b, w));
        Link(0,13,3); Link(1,14,3); Link(13,17,3); Link(14,18,3);
        Link(2,18,6); Link(3,17,6); Link(4,15,3); Link(7,15,3);
        Link(15,19,3); Link(15,16,-1); Link(5,19,.6); Link(6,16,.4);
        Link(8,11,1); Link(8,12,1); Link(9,11,.15); Link(10,12,.15);
        Link(11,11,2.5); Link(12,12,2.5); Link(11,12,-3); Link(12,11,-3);
        Link(11,17,2.5); Link(12,18,2.5); Link(11,16,1.5); Link(12,16,1.5);
        Link(17,18,-2); Link(18,17,-2); Link(19,16,-3);
        Activity[11] = .15; Activity[12] = .08;
    }
    public MotorActivity Step(double[] senses, double dt)
    {
        double Ease(double old, double target, double tau) => old + (target - old) * (1 - Math.Exp(-dt / tau));
        Drives[0] = Ease(Drives[0], .3 + .7 * senses[8], 10);
        Drives[1] = Ease(Drives[1], senses[5], 35);
        Drives[2] = Ease(Drives[2], .2 + .8 * senses[7], 2);
        Drives[3] = Ease(Drives[3], Math.Max(senses[7], senses[4]), .6);
        Drives[4] = Ease(Drives[4], 1 - Drives[5], 8);
        Drives[5] = Ease(Drives[5], Math.Clamp(senses[0] + senses[1], 0, 1), 12);
        for (int i = 0; i < 11; i++) next[i] = Ease(Activity[i], senses[i], .07);
        for (int i = 11; i < 20; i++)
        {
            noise[i] = Ease(noise[i], random.NextDouble() * 2 - 1, .2);
            double current = -1.8 + noise[i] * .7 - adaptation[i] * (i is 11 or 12 ? 3.8 : .3);
            foreach (var edge in Connections) if (edge.To == i) current += Activity[edge.From] * edge.Weight;
            if (i is 11 or 12) current += Drives[0] * 2 + Drives[2];
            if (i is 13 or 14) current += Drives[4] - Drives[5] * 2;
            if (i == 15) current += Drives[3];
            if (i == 16) current += 2 + Drives[0] - Drives[1] * 3;
            if (i == 19) current += Drives[1] * 2;
            next[i] = Ease(Activity[i], 1 / (1 + Math.Exp(-current)), .12);
        }
        Array.Copy(next, Activity, 20);
        for (int i = 11; i < 20; i++) adaptation[i] = Ease(adaptation[i], Activity[i], 2.5);
        return new(Activity[16], Activity[18] - Activity[17], Activity[19]);
    }
}
public static class MotorSystem
{
    public static void Step(Creature c, MotorActivity motor, Habitat habitat, double dt)
    {
        c.Heading = Math.IEEERemainder(c.Heading + motor.Turn * 5 * dt, Math.Tau);
        var acceleration = Vec.Direction(c.Heading) * (motor.Forward * 320);
        c.Velocity = (c.Velocity + acceleration * dt) * Math.Exp(-(2 + motor.Brake * 9) * dt);
        var proposed = c.Position + c.Velocity * dt;
        c.Position = habitat.Constrain(proposed);
        if ((proposed - c.Position).Length > .001) c.Velocity = new(0, 0);
        c.Gait += c.Velocity.Length * dt / 12;
    }
}
public sealed class Simulation(Habitat habitat, int seed = 7)
{
    public const double Dt = 1.0 / 120;
    public Habitat Habitat { get; set; } = habitat;
    public Creature Creature { get; } = new() { Position = habitat.Regions[0].Clamp(new(habitat.Regions[0].X + habitat.Regions[0].Width / 2, habitat.Regions[0].Y + habitat.Regions[0].Height / 2)) };
    public Sensors Sensors { get; } = new();
    public Brain Brain { get; } = new(seed);
    public MotorActivity Motor { get; private set; }
    public void Step(Vec cursor)
    {
        Sensors.Sample(Creature, Habitat, cursor, Dt);
        Motor = Brain.Step(Sensors.Values, Dt);
        MotorSystem.Step(Creature, Motor, Habitat, Dt);
    }
}
