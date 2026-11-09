using FlyGuy.Core;
using System.Diagnostics;
static void Check(bool pass,string name) { if(!pass) throw new Exception(name); Console.WriteLine("PASS " + name); }
var world=new Habitat([new(0,0,1920,1080)]);
var sim=new Simulation(world);
var timer=Stopwatch.StartNew();
var initial=sim.Creature.Position;
for(int i=0;i<120*300;i++) sim.Step();
Check(world.Contains(sim.Creature.Position) && sim.Brain.Activity.All(v=>double.IsFinite(v)&&v>=0&&v<=1),"Five-minute closed-loop stability");
Check((initial-sim.Creature.Position).Length>10,"Neural motor output moves body");
Console.WriteLine($"300 simulated seconds in {timer.Elapsed.TotalSeconds:F3}s; working set {Environment.WorkingSet/1048576} MB");
sim.Held=true; sim.DropSugar(sim.Creature.Position);
for(int i=0;i<120;i++) sim.Step();
Check(sim.Consumed>0 && sim.Brain.Dopamine>.01,"Contact feeding evokes reward while held");
var position=sim.Creature.Position;
for(int i=0;i<120;i++) sim.Step();
Check(sim.Creature.Position==position,"Grab constrains physics while brain runs");
var paired=new Brain(); var unpaired=new Brain();
for(int trial=0;trial<12;trial++)
 for(int i=0;i<120*8;i++)
 {
    var p=new double[Sensors.InputCount]; var u=new double[Sensors.InputCount];
    p[0]=u[0]=i<120 ? 1 : 0;
    p[17]=i>=120 && i<240 ? 1 : 0;
    u[17]=i>=720 && i<840 ? 1 : 0;
    paired.Step(p,Simulation.Dt); unpaired.Step(u,Simulation.Dt);
 }
Check(paired.Plasticity.Weights[0]<unpaired.Plasticity.Weights[0]-.05,"Paired reward depresses cue weight more than delayed reward");
Check(paired.Plasticity.Weights[1]==1,"Inactive cue remains unchanged");
Console.WriteLine($"Paired {paired.Plasticity.Weights[0]:F4}, delayed {unpaired.Plasticity.Weights[0]:F4}");
var probe=new double[Sensors.InputCount]; probe[0]=1;
MotorActivity pm=default, um=default;
for(int i=0;i<240;i++) { pm=paired.Step(probe,Simulation.Dt); um=unpaired.Step(probe,Simulation.Dt); }
Check(Math.Abs(pm.Turn-um.Turn)>.005,"Learned weights alter motor response to cue");
var restored=new Brain(); restored.ImportLearning(paired.ExportLearning());
Check(restored.Plasticity.Weights.SequenceEqual(paired.Plasticity.Weights),"Versioned learned-state round trip");
sim.ResetBrain(); Check(sim.Brain.Plasticity.Weights.All(w=>w==1) && sim.Brain.Dopamine==0,"Brain reset");
var sense=new Sensors(); var creature=new Creature {Position=new(500,500)};
sense.Sample(creature,world,Simulation.Dt,[new(new(1000,1000))],false,0,0);
Check(sense.Values[11]==0 && sense.Values[12]==0 && sense.Values[13]==0,"Distant sugar is not sensed");

var habituated=new Brain(); var cue=new double[Sensors.InputCount]; cue[0]=1;
for(int i=0;i<120*60;i++) habituated.Step(cue,Simulation.Dt);
double high=habituated.Drives[5];
for(int i=0;i<120*60;i++) habituated.Step(new double[Sensors.InputCount],Simulation.Dt);
Check(high>.9 && habituated.Drives[5]<.02,"Habituation accumulates and recovers");
for(int seed=1;seed<=5;seed++)
{
    var experiment=new Simulation(world,seed);
    for(int i=0;i<120*180;i++)
    {
        if(i%(120*10)==0) experiment.DropSugar(experiment.Creature.Position+new Vec(30,10));
        experiment.Step();
    }
    Check(experiment.Brain.Activity.All(double.IsFinite) && world.Contains(experiment.Creature.Position),$"Food/cursor stability seed {seed}");
}
