using FlyGuy.Core;

internal static class SprayChecks
{
    private static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }

    private static double[] Input(double cue = 0, double spray = 0, double reward = 0)
    {
        var senses = new double[Sensors.InputCount];
        senses[0] = cue; senses[Sensors.SprayContact] = spray; senses[17] = reward;
        return senses;
    }

    private static void Run(Brain brain, double seconds, double[] input)
    {
        for (int i = 0; i < seconds / Simulation.Dt; i++) brain.Step(input, Simulation.Dt);
    }

    private static Brain Condition(int seed, bool paired)
    {
        var brain = new Brain(seed);
        for (int trial = 0; trial < 8; trial++)
            for (int i = 0; i < 120 * 20; i++)
            {
                double seconds = i * Simulation.Dt;
                double onset = paired ? 1 : 10;
                brain.Step(Input(seconds < 1 ? 1 : 0, seconds >= onset && seconds < onset + 1 ? 1 : 0), Simulation.Dt);
            }
        // Test retained memory in a fresh brain, excluding residual stress and adaptation.
        var probe = new Brain(seed);
        probe.ImportLearning(brain.ExportLearning());
        return probe;
    }

    public static void RunAll()
    {
        var habitat = new Habitat([new(0,0,1920,1080)]);
        var local = new Simulation(habitat) { Held = true };
        var far = new Simulation(habitat) { Held = true };
        Vec start = local.Creature.Position;
        local.SprayAt(start); far.SprayAt(new(20,20));
        for (int i = 0; i < 120; i++) { local.Step(); far.Step(); }
        Check(local.Sensors.Values[Sensors.SprayContact] > .4 && far.Sensors.Values[Sensors.SprayContact] == 0,
            "Spray is a local concentration field");
        Check(local.Brain.Threat.AcuteArousal > .7 && local.Brain.Threat.Stress > .1 && local.Brain.Threat.Aversion > .7,
            "Contact receptors drive acute, stress and aversive signals");
        Check(local.Creature.Position == start && local.Creature.Velocity == default,
            "Spray does not bypass held-body physics");
        Check(far.Brain.Threat.Aversion == 0 && far.Brain.Threat.Stress == 0, "Distant spray produces no punishment");
        for (int i = 0; i < 120 * 10; i++) local.Step();
        Check(local.Spray.Count == 0 && local.Sensors.Values[Sensors.SprayContact] == 0, "Clouds decay and expire");

        var brain = new Brain();
        Run(brain, 2, Input(spray: 1));
        double firstStress = brain.Threat.Stress;
        Run(brain, 8, Input());
        Check(brain.Threat.Stress > .2 && brain.Threat.AcuteArousal < .05 && brain.Threat.Aversion < .02,
            "Slow stress outlasts acute arousal and punishment");
        Run(brain, 2, Input(spray: 1));
        Check(brain.Threat.Stress > firstStress + .1, "Exposure before recovery accumulates stress");
        Run(brain, 8, Input());
        var naive = new Brain();
        Run(naive, .2, Input(spray: .15)); Run(brain, .2, Input(spray: .15));
        Check(brain.Activity[Brain.SprayContact] > naive.Activity[Brain.SprayContact] + .005,
            "Prior aversion sensitizes responses to a weak irritant");
        Run(brain, 1200, Input());
        Check(brain.Threat.Stress < .001 && brain.Threat.Sensitization < .001 && brain.Threat.AcuteArousal < .001,
            "Stress and sensitization recover without exposure");

        var fast = new ThreatModulation(); var slow = new ThreatModulation();
        var defaults = new ThreatSettings();
        var altered = defaults with { AcuteDecaySeconds = 8, AversionDecaySeconds = 7, StressDecaySeconds = 90 };
        for (int i=0;i<120;i++) { fast.Tick(1,0,defaults,Simulation.Dt); slow.Tick(1,0,altered,Simulation.Dt); }
        for (int i=0;i<120*8;i++) { fast.Tick(0,0,defaults,Simulation.Dt); slow.Tick(0,0,altered,Simulation.Dt); }
        Check(slow.AcuteArousal > fast.AcuteArousal * 5 && slow.Aversion > fast.Aversion * 5 && slow.Stress > fast.Stress,
            "Independent decay settings affect recovery");
        try { brain.ThreatSettings = defaults with { StressDecaySeconds = 0 }; throw new Exception("Invalid rate accepted"); }
        catch (ArgumentOutOfRangeException) { }

        for (int seed = 1; seed <= 5; seed++)
        {
            var paired = Condition(seed, true); var unpaired = Condition(seed, false);
            Check(paired.Plasticity.ApproachWeights[0] < unpaired.Plasticity.ApproachWeights[0] - .2 && paired.Plasticity.ApproachWeights[1] == 1,
                $"Seed {seed}: preceding cue learns selective aversion more than delayed controls");
            MotorActivity pairedMotor = default, unpairedMotor = default;
            for (int i = 0; i < 120; i++)
            {
                pairedMotor = paired.Step(Input(1), Simulation.Dt);
                unpairedMotor = unpaired.Step(Input(1), Simulation.Dt);
            }
            Check(paired.Activity[Brain.LearnedThreat] > unpaired.Activity[Brain.LearnedThreat] + .1 && paired.Threat.Aversion == 0,
                $"Seed {seed}: cue alone recalls threat without inventing punishment");
            Check(pairedMotor.Turn > unpairedMotor.Turn + .05,
                $"Seed {seed}: learned aversion changes the motor response away from a left cue");
            Console.WriteLine($"  Approach weights paired/delayed {paired.Plasticity.ApproachWeights[0]:F3}/{unpaired.Plasticity.ApproachWeights[0]:F3}; turn {pairedMotor.Turn:F3}/{unpairedMotor.Turn:F3}");
        }

        var rewarded = new Brain(); var punished = new Brain(); var conflict = new Brain();
        Run(rewarded, 8, Input(1, 0, 1)); Run(punished, 8, Input(1, 1)); Run(conflict, 8, Input(1, 1, 1));
        Check(conflict.Dopamine > .9 && conflict.Threat.Aversion > .8 && conflict.Plasticity.Weights[0] < .5 && conflict.Plasticity.ApproachWeights[0] < .5,
            "Simultaneous reward and punishment independently modify opposing readouts");
        double Probe(Brain trained)
        {
            var fresh = new Brain(); fresh.ImportLearning(trained.ExportLearning());
            Run(fresh, 1, Input(1));
            return fresh.Activity[18] - fresh.Activity[17];
        }
        double toward = Probe(rewarded), away = Probe(punished), mixed = Probe(conflict);
        Check(toward < mixed && mixed < away, "Conflicting learned signals yield a competing motor response");
        Console.WriteLine($"  Reward/mixed/aversion turn {toward:F3}/{mixed:F3}/{away:F3}");

        var blocked = new Brain(); var blockedControl = new Brain();
        blocked.Connections.RemoveAll(e => e.From >= Brain.SprayLeft && e.From <= Brain.SprayContact);
        blockedControl.Connections.RemoveAll(e => e.From >= Brain.SprayLeft && e.From <= Brain.SprayContact);
        Run(blocked, 3, Input(spray: 1)); Run(blockedControl, 3, Input());
        Check(blocked.Threat.Aversion == 0 && blocked.Threat.AcuteArousal == 0 && blocked.Activity[16] == blockedControl.Activity[16] && blocked.Activity[17] == blockedControl.Activity[17],
            "Ablating spray sensory projections removes modulation and motor effects");

        var persisted = new Brain(); var saved = conflict.ExportLearning(); persisted.ImportLearning(saved);
        Check(persisted.Plasticity.ApproachWeights.SequenceEqual(conflict.Plasticity.ApproachWeights), "Both plasticity compartments survive a learned-state round trip");
        saved.ApproachWeights![0] = 1;
        Check(persisted.Plasticity.ApproachWeights[0] < .5, "Learned state does not alias approach weights");
        persisted.ImportLearning(new(1, [1,1,1,1]));
        Check(persisted.Plasticity.ApproachWeights.All(w=>w==1), "Version 1 learning imports with neutral aversive memory");
        local.ResetBrain();
        Check(local.Brain.Threat.Stress == 0 && local.Brain.Threat.Sensitization == 0 && local.Brain.Plasticity.ApproachWeights.All(w=>w==1), "Reset clears stress and both memories");

        for (int seed = 1; seed <= 3; seed++)
        {
            var sim = new Simulation(habitat, seed); var replay = new Simulation(habitat, seed);
            for (int i=0;i<120*120;i++)
            {
                if (i % 360 == 0)
                {
                    sim.SprayAt(sim.Creature.Position); replay.SprayAt(replay.Creature.Position);
                    sim.DropSugar(sim.Creature.Position); replay.DropSugar(replay.Creature.Position);
                }
                sim.Step(); replay.Step();
                CheckFinite(sim);
                if (sim.Creature.Position != replay.Creature.Position) throw new Exception("Spray replay diverged");
            }
            Check(true, $"Seed {seed}: repeated sugar/spray remains bounded, contained and deterministic");
        }
    }

    private static void CheckFinite(Simulation sim)
    {
        if (!sim.Brain.Activity.All(v => double.IsFinite(v) && v >= 0 && v <= 1) || !sim.Habitat.Contains(sim.Creature.Position)
            || !sim.Brain.Plasticity.ApproachWeights.Concat(sim.Brain.Plasticity.Weights).All(v=>double.IsFinite(v) && v>=.05 && v<=1))
            throw new Exception("Invalid simulation under repeated spray");
    }
}
