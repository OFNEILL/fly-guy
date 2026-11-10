using FlyGuy.Core;

internal static class CigaretteChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Cigarette: " + message);
    }

    public static void RunAll()
    {
        var habitat = new Habitat([new(0, 0, 1200, 900)]);
        var sim = new Simulation(new Habitat([new(0, 0, 30, 30)]));
        sim.DropCigarette(sim.Creature.Position);
        Check(sim.SmokeCigarette(), "manual puff failed at contact");
        var n = sim.Brain.Nicotine;
        Check(n.Cortisol == 20 && n.Epinephrine == 24 && n.Norepinephrine == 24,
            "first puff must produce extremely large fictional hormone spikes");
        Check(sim.Cigarettes.Count == 0 && sim.SmokingCigarette is { PuffsRemaining: 7 },
            "first puff did not remove ground cigarette and transfer it to the fly");
        Check(!sim.SmokeCigarette() && sim.SmokingCigarette is { PuffsRemaining: 7 }, "cooldown consumed extra tobacco");
        for (int i = 0; i < 120 * 60; i++)
        {
            sim.Step();
            Check(sim.Brain.Activity.All(x => double.IsFinite(x) && x >= 0 && x <= 1), "chemistry produced invalid neural activity");
        }
        Check(n.Puffs > 2 && n.Dependence > .75 && n.Tolerance > .2, "autonomous repeated use did not build dependence/tolerance");
        sim.Cigarettes.Clear();
        for (int i = 0; i < 120 * 90; i++) sim.Step();
        Check(n.Withdrawal > .7 && n.Craving > .95, "abstinence did not produce withdrawal/craving");
        double dependence = n.Dependence;
        n.Tick(3600);
        Check(n.Dependence < dependence * .2 && n.Cortisol < .01, "abstinence did not permit recovery");
        sim.ResetBrain();
        Check(sim.Brain.Nicotine.Puffs == 0 && sim.Brain.Nicotine.Dependence == 0 && sim.Brain.Nicotine.Cortisol == 0,
            "reset retained addiction or chemistry");

        var autonomous = new Simulation(habitat);
        autonomous.DropCigarette(autonomous.Creature.Position);
        for (int i = 0; i < 120; i++) autonomous.Step();
        Check(autonomous.Brain.Nicotine.Puffs > 0, "free fly never tried a cigarette without user action");
        var disabled = new Simulation(habitat) { AutonomousSmoking = false, Held = true };
        disabled.DropCigarette(disabled.Creature.Position);
        for (int i = 0; i < 120 * 4; i++) disabled.Step();
        Check(disabled.Brain.Nicotine.Puffs == 0, "disabled autonomous use still smoked");
        disabled.AutonomousSmoking = true;
        for (int i = 0; i < 120 * 4; i++) disabled.Step();
        Check(disabled.Brain.Nicotine.Puffs == 0, "held fly smoked autonomously");
        Check(disabled.SmokeCigarette(), "manual use should work while held");

        var finite = new Simulation(habitat) { AutonomousSmoking = false };
        finite.DropCigarette(finite.Creature.Position);
        for (int i = 0; i < 8; i++)
        {
            finite.Brain.Nicotine.Tick(9);
            Check(finite.SmokeCigarette(), "finite cigarette depleted early");
        }
        Check(finite.Cigarettes.Count == 0 && finite.SmokingCigarette is null && !finite.SmokeCigarette(), "depleted cigarette remained usable");
        var burnout = new Simulation(habitat) { AutonomousSmoking = false, Held = true };
        burnout.DropCigarette(burnout.Creature.Position);
        var burning = burnout.Cigarettes[0];
        for (int i = 0; i < 120 * 65; i++) burnout.Step();
        Check(burnout.Cigarettes.Count == 1 && burning.RemainingFraction == 1 && !burning.IsLit,
            "unlit cigarette burned before use");
        Check(burnout.SmokeCigarette() && burnout.Cigarettes.Count == 0 && ReferenceEquals(burnout.SmokingCigarette, burning) && burning.IsLit && burning.RemainingFraction < 1,
            "smoking did not light and shorten cigarette");
        burnout.Creature.Position = new(50, 50);
        double beforeBurn = burning.RemainingFraction;
        for (int i = 0; i < 120 * 30; i++) burnout.Step();
        Check(burning.RemainingFraction < beforeBurn && burnout.Cigarettes.Count == 0 && ReferenceEquals(burnout.SmokingCigarette, burning),
            "abandoned lit cigarette did not burn down");
        for (int i = 0; i < 120 * 31; i++) burnout.Step();
        Check(burnout.Cigarettes.Count == 0 && burnout.SmokingCigarette is null && burning.PuffsRemaining == 0 && !burnout.SmokeCigarette(),
            "burned-out cigarette remained on desktop or usable");
        finite.DropCigarette(new(double.NaN, 0));
        finite.DropCigarette(new(-50, -50));
        Check(finite.Cigarettes.Count == 0, "invalid placements accepted");

        var left = new Brain(); var right = new Brain();
        var ls = new double[Sensors.InputCount]; var rs = new double[Sensors.InputCount];
        ls[30] = 1; rs[31] = 1;
        MotorActivity lm = default, rm = default;
        for (int i = 0; i < 240; i++) { lm = left.Step(ls, Simulation.Dt); rm = right.Step(rs, Simulation.Dt); }
        Check(lm.Turn < rm.Turn - .1, "tobacco odor did not drive directional neural steering");
        Console.WriteLine("PASS: cigarette spikes, autonomous initiation/repeated use, dependence, tolerance, withdrawal, recovery, reset, toggle, held behavior, finite supply and neural seeking.");
    }
}
