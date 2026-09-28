using FlyGuy.Core;
using System.Diagnostics;

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var habitat = new Habitat([new(-1280,0,1280,1024),new(0,0,1920,1080)]);
var a = new Simulation(habitat); var b = new Simulation(habitat);
var timer=Stopwatch.StartNew(); double travelled=0, turnRange=0;
for(int i=0;i<120*60*10;i++)
{
    Vec cursor=new(600+500*Math.Sin(i/1300.0),400+300*Math.Cos(i/1700.0));
    var prior=a.Creature.Position; a.Step(cursor); b.Step(cursor);
    Require(a.Creature.Position==b.Creature.Position,"Seeded replay diverged");
    Require(habitat.Contains(a.Creature.Position),"Escaped monitor union");
    Require(a.Brain.Activity.All(x=>double.IsFinite(x)&&x>=0&&x<=1),"Invalid neural activity");
    Require(double.IsFinite(a.Creature.Velocity.Length),"Invalid velocity");
    travelled+=(a.Creature.Position-prior).Length; turnRange=Math.Max(turnRange,Math.Abs(a.Motor.Turn));
}
Require(travelled>1000 && turnRange>.1,"Controller is inactive");
var left=new Brain(); var right=new Brain(); var ls=new double[11]; var rs=new double[11]; ls[0]=1; rs[1]=1;
MotorActivity lm=default,rm=default;
for(int i=0;i<240;i++) {lm=left.Step(ls,Simulation.Dt);rm=right.Step(rs,Simulation.Dt);}
Require(lm.Turn<rm.Turn-.1,"Cursor laterality does not influence motor neurons");
left=new Brain();right=new Brain(); ls=new double[11];rs=new double[11];ls[2]=1;rs[3]=1;
for(int i=0;i<240;i++) {lm=left.Step(ls,Simulation.Dt);rm=right.Step(rs,Simulation.Dt);}
Require(lm.Turn>rm.Turn+.1,"Edge signals do not produce opponent steering");
var c=new Creature {Position=new(300,300)};
for(int i=0;i<1200;i++) MotorSystem.Step(c,default,habitat,Simulation.Dt);
Require(c.Position==new Vec(300,300),"Movement exists without motor activity");
Require(habitat.Edge(new(-1,500),0)==0,"Shared monitor seam treated as obstacle");
var gap=new Habitat([new(-300,0,100,100),new(0,0,100,100)]);
Require(!gap.Contains(new(-100,50)) && gap.Contains(gap.Constrain(new(-100,50))),"Monitor gap handling failed");
Console.WriteLine($"PASS: 10 simulated minutes; deterministic replay, finite activity, monitor containment, cursor/edge responses, motor ablation. Travel {travelled:F0}px, peak turn {turnRange:F2}. Elapsed {timer.Elapsed.TotalSeconds:F2}s.");
