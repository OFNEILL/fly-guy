using FlyGuy.Core;
using System.Diagnostics;

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var habitat = new Habitat([new(-1280,0,1280,1024),new(0,0,1920,1080)]);
var a = new Simulation(habitat); var b = new Simulation(habitat);
var timer=Stopwatch.StartNew(); double travelled=0, turnRange=0;
for(int i=0;i<120*60*10;i++)
{
    var prior=a.Creature.Position; a.Step(); b.Step();
    Require(a.Creature.Position==b.Creature.Position,"Seeded replay diverged");
    Require(habitat.Contains(a.Creature.Position),"Escaped monitor union");
    Require(a.Brain.Activity.All(x=>double.IsFinite(x)&&x>=0&&x<=1),"Invalid neural activity");
    Require(double.IsFinite(a.Creature.Velocity.Length),"Invalid velocity");
    travelled+=(a.Creature.Position-prior).Length; turnRange=Math.Max(turnRange,Math.Abs(a.Motor.Turn));
}
Require(travelled>1000 && turnRange>.1,"Controller is inactive");
var left=new Brain(); var right=new Brain(); var ls=new double[Sensors.InputCount]; var rs=new double[Sensors.InputCount]; ls[0]=1; rs[1]=1;
MotorActivity lm=default,rm=default;
for(int i=0;i<240;i++) {lm=left.Step(ls,Simulation.Dt);rm=right.Step(rs,Simulation.Dt);}
Require(lm.Turn<rm.Turn-.1,"Cursor laterality does not influence motor neurons");
left=new Brain();right=new Brain(); ls=new double[Sensors.InputCount];rs=new double[Sensors.InputCount];ls[2]=1;rs[3]=1;
for(int i=0;i<240;i++) {lm=left.Step(ls,Simulation.Dt);rm=right.Step(rs,Simulation.Dt);}
Require(lm.Turn>rm.Turn+.1,"Edge signals do not produce opponent steering");
var c=new Creature {Position=new(300,300)};
for(int i=0;i<1200;i++) MotorSystem.Step(c,default,habitat,Simulation.Dt);
Require(c.Position==new Vec(300,300),"Movement exists without motor activity");
Require(habitat.Edge(new(-1,500),0)==0,"Shared monitor seam treated as obstacle");
var gap=new Habitat([new(-300,0,100,100),new(0,0,100,100)]);
Require(!gap.Contains(new(-100,50)) && gap.Contains(gap.Constrain(new(-100,50))),"Monitor gap handling failed");
var feeding = new Simulation(habitat) { Held = true };
feeding.DropSugar(feeding.Creature.Position);
double initialHunger = feeding.Brain.Hunger, initialEnergy = feeding.Brain.Energy;
for (int i = 0; i < 120 * 5; i++) feeding.Step();
Require(feeding.Consumed > .9 && feeding.Consumed <= 1, "Feeding failed to conserve food mass");
Require(feeding.Sugar.Count == 0, "Depleted sugar remained in the habitat");
Require(feeding.Brain.Hunger < initialHunger && feeding.Brain.Energy > initialEnergy, "Ingestion did not replenish metabolism");
Require(feeding.Brain.Plasticity.Weights.Any(w => w < .99), "Ingestion produced no learning");
var paired = new RewardPlasticity(); var unrewarded = new RewardPlasticity();
for (int i = 0; i < 120 * 10; i++)
{
    paired.Tick([1, 0, 0, 0], 1, Simulation.Dt);
    unrewarded.Tick([1, 0, 0, 0], 0, Simulation.Dt);
}
Require(paired.Weights[0] < .5 && paired.Weights.Skip(1).All(w => w == 1), "Reward learning was not feature selective");
Require(unrewarded.Weights.All(w => w == 1), "Weights changed without reward");
var saved = feeding.Brain.ExportLearning();
feeding.ResetBrain();
Require(feeding.Brain.Plasticity.Weights.All(w => w == 1), "Brain reset retained learned weights");
feeding.Brain.ImportLearning(saved);
Require(feeding.Brain.Plasticity.Weights.SequenceEqual(saved.Weights), "Learning round trip failed");
saved.Weights[2] = 1;
Require(feeding.Brain.Plasticity.Weights[2] < .99, "Imported weights alias the saved state");
Console.WriteLine($"PASS: 10 simulated minutes; deterministic replay, finite activity, monitor containment, cursor/edge responses, motor ablation, feeding, metabolism, selective reward learning, reset and learned-state round trip. Travel {travelled:F0}px, peak turn {turnRange:F2}. Elapsed {timer.Elapsed.TotalSeconds:F2}s.");
SprayChecks.RunAll();
VisionChecks.RunAll();
