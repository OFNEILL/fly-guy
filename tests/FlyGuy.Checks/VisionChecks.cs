using FlyGuy.Core;

internal static class VisionChecks
{
    private static readonly Vec Center = new(320,320);
    private const int Size = 640;
    private static readonly Region[] Monitors = [new(0,0,Size,Size)];
    private const double Dt = 1.0/30;
    private static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        Console.WriteLine("PASS: vision - " + name);
    }
    private static PixelFrame Frame(Func<int,int,double> value, double time=0, Region[]? monitors=null)
    {
        var pixels=new byte[Size*Size*4];
        for (int y=0;y<Size;y++)
            for (int x=0;x<Size;x++)
            {
                int i=(y*Size+x)*4; byte light=(byte)Math.Clamp(value(x,y)*255,0,255);
                pixels[i]=pixels[i+1]=pixels[i+2]=light; pixels[i+3]=255;
            }
        return new(0,0,Size,Size,pixels,monitors??Monitors,time);
    }
    private static void Tick(BinocularVision eyes,PixelFrame frame,double heading=0)
    {
        eyes.Sample(frame,Center,heading,Dt); eyes.Process(Dt);
    }
    public static void RunAll()
    {
        var eyes=new BinocularVision();
        var uniform=Frame((_,_)=>.5);
        for (int i=0;i<60;i++) Tick(eyes,uniform);
        Check(eyes.Left.Pose.Position!=eyes.Right.Pose.Position && eyes.Left.Pose.Heading!=eyes.Right.Pose.Heading,
            "eyes have distinct positions and viewing directions");
        Check(eyes.Left.SamplePositions.Where((p,i)=>p!=eyes.Right.SamplePositions[i]).Count()==eyes.Left.Receptors.Length,
            "retinas do not receive identical sampling geometry");
        double overlap=Math.Min(eyes.Settings.Left.YawDegrees+eyes.Settings.Left.FieldOfViewDegrees/2,eyes.Settings.Right.YawDegrees+eyes.Settings.Right.FieldOfViewDegrees/2)
            - Math.Max(eyes.Settings.Left.YawDegrees-eyes.Settings.Left.FieldOfViewDegrees/2,eyes.Settings.Right.YawDegrees-eyes.Settings.Right.FieldOfViewDegrees/2);
        Check(overlap>0 && overlap<eyes.Settings.Left.FieldOfViewDegrees,"fields overlap without coinciding");
        Check(eyes.Left.Contrast.All(v=>Math.Abs(v)<1e-10) && eyes.Left.Change.All(v=>v==0) && eyes.Left.MotionX.All(v=>v==0),
            "uniform stationary images produce neither edges nor motion");
        var before=eyes.Left.SamplePositions[0]-Center;
        Tick(eyes,uniform,Math.PI/2);
        var after=eyes.Left.SamplePositions[0]-Center;
        Check((after-new Vec(-before.Y,before.X)).Length<1e-8,"body turning rotates both the eye position and its rays");

        // One pixel object confined to the left-only field. There are no object/cursor labels in the input.
        var stimulus=Center+Vec.Direction(-85*Math.PI/180)*120;
        PixelFrame Pointer(double offset=0,bool mirror=false) => Frame((x,y)=>Math.Abs(x-(stimulus.X+offset))<10 && Math.Abs(y-(mirror?Size-stimulus.Y:stimulus.Y))<13 ? 1 : .15);
        eyes=new();
        for (int i=0;i<60;i++) Tick(eyes,Pointer());
        double stillChange=eyes.Left.Signals.Change;
        Check(eyes.Left.Signals.Activity>eyes.Right.Signals.Activity+.01,"a left-field pixel stimulus causes asymmetric visual activity");
        double movingChange=0,movingNeural=0;
        for (int i=0;i<30;i++)
        {
            Tick(eyes,Pointer(30*Math.Sin(i*.2)));
            movingChange+=eyes.Left.Signals.Change; movingNeural+=eyes.Left.Signals.Activity;
        }
        Check(movingChange/30>stillChange+.015,"a moving pointer-shaped patch differs from a stationary patch");
        Console.WriteLine($"  Stationary/moving temporal signal {stillChange:F4}/{movingChange/30:F4}; moving neural {movingNeural/30:F4}");
        for (int i=0;i<90;i++) Tick(eyes,Pointer(),Math.PI/2);
        Check(eyes.Left.Signals.Activity<.001 && eyes.Right.Signals.Activity<.001,"turning away removes an out-of-view stimulus");

        var contract=new BinocularVision(); Tick(contract,Pointer());
        int sub=contract.Settings.SamplesPerAxis;
        for (int i=0;i<contract.Left.Receptors.Length;i++)
        {
            if (!contract.Left.Valid[i]) continue;
            double expected=0;
            for (int y=0;y<sub;y++) for (int x=0;x<sub;x++)
            {
                int raw=((i/contract.Settings.Columns*sub+y)*contract.Left.RawWidth+i%contract.Settings.Columns*sub+x)*4;
                expected+=contract.Left.RawBgra[raw]/255.0;
            }
            if (Math.Abs(expected/(sub*sub)-contract.Left.Receptors[i])>1e-10) throw new Exception("Raw/receptor mismatch");
        }
        double pooled=Math.Clamp(3*Math.Sqrt(contract.Left.Neurons.Select(v=>v*v).Average()),0,1);
        Check(Math.Abs(pooled-contract.Signals.Left.Activity)<1e-10,"debugger arrays reproduce the exact pooled brain input");

        double Motion(int direction)
        {
            var vision=new BinocularVision(new VisionSettings { Left=new(0,0,0,140),Right=new(0,6,40,140) });
            double result=0;
            for (int i=0;i<75;i++)
            {
                double phase=direction*i*.15;
                var frame=Frame((x,y)=>.5+.45*Math.Sin(12*Math.Atan2(y-Center.Y,x-Center.X)-phase));
                Tick(vision,frame);
                if (i>15) result+=vision.Left.MotionX.Average();
            }
            return result/59;
        }
        double cw=Motion(1),ccw=Motion(-1);
        Check(cw>.001 && ccw<-.001,"delayed neighboring ON/OFF channels distinguish opposite motion directions");
        Console.WriteLine($"  Grating direction signals CW {cw:F4}, CCW {ccw:F4}");
        var flash=new BinocularVision(); Tick(flash,uniform); Tick(flash,Frame((_,_)=>.9));
        Check(flash.Left.Signals.Change>.1 && flash.Left.MotionX.All(v=>Math.Abs(v)<1e-10),"uniform flicker is temporal change, not directional motion");

        var blind=new BinocularVision();
        var gapFrame=Frame((_,_)=>.5,monitors:[new(0,0,300,Size),new(340,0,300,Size)]);
        Tick(blind,gapFrame);
        Check(blind.Left.Valid.Any(v=>!v) && blind.Left.Contrast.All(v=>Math.Abs(v)<1e-10),"monitor gaps are blind cells, not fabricated black edges");
        blind.Clear();
        Check(!blind.Signals.Available && blind.Left.Neurons.All(v=>v==0) && blind.Right.Receptors.All(v=>v==0),"capture loss clears all visual state and input");

        for (int seed=1;seed<=3;seed++)
        {
            var leftEyes=new BinocularVision(); var rightEyes=new BinocularVision();
            var leftSim=new Simulation(new(Monitors),seed) { Held=true };
            var rightSim=new Simulation(new(Monitors),seed) { Held=true };
            double leftTurn=0,rightTurn=0;
            for (int i=0;i<90;i++)
            {
                double offset=25*Math.Sin(i*.2);
                Tick(leftEyes,Pointer(offset)); Tick(rightEyes,Pointer(offset,true));
                for (int j=0;j<4;j++) { leftSim.Step(leftEyes.Signals); rightSim.Step(rightEyes.Signals); }
                if (i>=30) { leftTurn+=leftSim.Motor.Turn; rightTurn+=rightSim.Motor.Turn; }
            }
            Check(leftTurn/60<rightTurn/60-.01,$"seed {seed}: pixels -> separate eyes -> neural circuit -> asymmetric motor output");
            Console.WriteLine($"  Mean left/right stimulus turn {leftTurn/60:F4}/{rightTurn/60:F4}");
        }
        var noPixels=new Simulation(new(Monitors)) { Held=true };
        noPixels.DropSugar(noPixels.Creature.Position);
        noPixels.Step();
        Check(noPixels.Sensors.Values[0]==0 && noPixels.Sensors.Values[1]==0 && noPixels.Sensors.Values[7]==0
            && noPixels.Sensors.Values.Skip(22).All(v=>v==0) && noPixels.Sensors.Values[11]>0,
            "food smell is explicit and cannot secretly become a visual object signal");
        foreach (double hz in new[] {60.0,120,240})
        {
            var sim=new Simulation(new(Monitors));
            for (int i=0;i<hz*10;i++) sim.Step(contract.Signals,1/hz);
            Check(sim.Brain.Activity.All(v=>double.IsFinite(v)&&v>=0&&v<=1),$"brain integration remains bounded at {hz} Hz");
        }
        var configured=new BinocularVision(new VisionSettings { Columns=12,Rows=6,Left=new(14,-8,-30,100),Right=new(20,9,45,120) });
        Tick(configured,uniform);
        Check(configured.Left.Receptors.Length==72 && configured.Left.RawBgra.Length==12*6*16*4,"receptor count and per-eye geometry are configurable");
        try { _=new BinocularVision(new VisionSettings { Columns=0 }); throw new Exception("Invalid geometry accepted"); }
        catch (ArgumentException) { }
    }
}
