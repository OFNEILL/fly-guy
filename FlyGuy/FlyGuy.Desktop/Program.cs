using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FlyGuy.Core;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Region = FlyGuy.Core.Region;

namespace FlyGuy.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var inspector = new Inspector();
        app.Run(inspector);
    }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(nint h, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    public static Habitat ReadHabitat() => new(System.Windows.Forms.Screen.AllScreens.Select(s => new Region(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToArray());
}
internal sealed class CreatureView : FrameworkElement
{
    public Creature? Creature { get; set; }
    protected override void OnRender(DrawingContext dc)
    {
        if (Creature is not { } c) return;
        var mid = new Point(48,48);
        dc.PushTransform(new RotateTransform(c.Heading * 180 / Math.PI, 48, 48));
        var leg = new Pen(Brushes.DarkSlateGray, 2.5);
        for (int i = 0; i < 3; i++)
        {
            double x = 37 + i * 10, stride = Math.Sin(c.Gait + i * 2) * 6;
            dc.DrawLine(leg, new(x,43), new(x + stride - 4,29));
            dc.DrawLine(leg, new(x,53), new(x - stride - 4,67));
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(125,180,235,255)), null, new(40,37), 17,7);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(125,180,235,255)), null, new(40,59), 17,7);
        dc.DrawEllipse(Brushes.MediumTurquoise, new Pen(Brushes.DarkSlateGray,2), mid, 19,12);
        dc.DrawEllipse(Brushes.Teal, null, new(62,48), 10,10);
        dc.DrawEllipse(Brushes.White, null, new(66,43), 4,4);
        dc.DrawEllipse(Brushes.White, null, new(66,53), 4,4);
        dc.DrawEllipse(Brushes.Black, null, new(68,43), 2,2);
        dc.DrawEllipse(Brushes.Black, null, new(68,53), 2,2);
        dc.Pop();
    }
}
internal sealed class Overlay : Window
{
    private readonly CreatureView view;
    private nint handle;
    public Overlay(Simulation simulation)
    {
        CaptureExclusion.Register(this);
        Width = Height = 96; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        ShowActivated = false; ResizeMode = ResizeMode.NoResize; IsHitTestVisible = true;
        Content = view = new CreatureView { Creature = simulation.Creature };
        System.Windows.Point? anchor = null;
        MouseLeftButtonDown += (_,e) =>
        {
            var p=e.GetPosition(this);
            if((p-new Point(48,48)).Length>32) return;
            simulation.Touch=1; simulation.Held=true;
            Native.GetCursorPos(out var cursor);
            anchor=new Point(cursor.X-simulation.Creature.Position.X,cursor.Y-simulation.Creature.Position.Y);
            CaptureMouse(); e.Handled=true;
        };
        MouseMove += (_,_) =>
        {
            if(!simulation.Held || anchor is not { } a) return;
            if(Native.GetCursorPos(out var cursor)) simulation.Creature.Position=simulation.Habitat.Constrain(new(cursor.X-a.X,cursor.Y-a.Y));
        };
        MouseLeftButtonUp += (_,_) => { simulation.Held=false; anchor=null; ReleaseMouseCapture(); };
        LostMouseCapture += (_,_) => { simulation.Held=false; anchor=null; };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x80 | 0x08000000);
            HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int msg, nint w, nint l, ref bool handled) =>
            {
                if(msg == 0x84 && !simulation.Held)
                {
                    Native.GetCursorPos(out var cursor);
                    var dpi=VisualTreeHelper.GetDpi(this);
                    if((new Vec(cursor.X,cursor.Y)-simulation.Creature.Position).Length > 32*dpi.DpiScaleX)
                    { handled=true; return new nint(-1); }
                }
                return nint.Zero;
            });
        };
    }
    public void Draw(Creature creature, bool held)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Native.SetWindowPos(handle, new nint(-1), (int)(creature.Position.X - 48 * dpi.DpiScaleX), (int)(creature.Position.Y - 48 * dpi.DpiScaleY), 0, 0, 0x0011);
        Native.GetCursorPos(out var cursor);
        bool interactive=held || (new Vec(cursor.X,cursor.Y)-creature.Position).Length <= 32*dpi.DpiScaleX;
        int style=Native.GetWindowLong(handle,-20);
        int desired=interactive ? style & ~0x20 : style | 0x20;
        if(style!=desired) Native.SetWindowLong(handle,-20,desired);
        view.InvalidateVisual();
    }
}
internal sealed class BrainView(Simulation simulation, WorkMeter? paintMeter = null) : FrameworkElement
{
    private readonly Queue<double[]> history = new();
    public void Sample()
    {
        var b = simulation.Brain;
        history.Enqueue([simulation.Motor.Forward, (simulation.Motor.Turn + 1) / 2, simulation.Motor.Brake, b.Dopamine,
            b.Threat.Aversion, b.Threat.AcuteArousal, b.Threat.Stress, b.Activity[Brain.SprayContact], b.Activity[15],
            b.Plasticity.Weights.Average(), b.Plasticity.ApproachWeights.Average()]);
        if (history.Count > 1200) history.Dequeue();
        InvalidateVisual();
    }
    public void ClearHistory() { history.Clear(); InvalidateVisual(); }
    private static void Text(DrawingContext dc, string text, double x, double y, double size = 12, System.Windows.Media.Brush? brush = null)
        => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.LightGray, 1), new(x,y));
    protected override void OnRender(DrawingContext dc)
    {
        long began=Stopwatch.GetTimestamp();
        var b = simulation.Brain;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(19,25,35)), null, new Rect(0,0,ActualWidth,ActualHeight));
        Text(dc, "SYNTHETIC RATE NETWORK - cyan excitation / coral inhibition", 16,12,14);
        Point Position(int i) => i < 11 ? new(85, 60 + i * 31) : i < 20 ? new(330, 60 + (i-11)*38) : i < 28 ? new(565,60+(i-20)*44) : i < 38 ? new(805,60+(i-28)*34) : i < 47 ? new(1080,60+(i-38)*38) : new(1340,60+(i-47)*42);
        foreach (var e in b.Connections)
        {
            var color = e.Weight > 0 ? Color.FromArgb((byte)(30+b.Activity[e.From]*125),70,210,210) : Color.FromArgb((byte)(30+b.Activity[e.From]*125),255,120,110);
            dc.DrawLine(new Pen(new SolidColorBrush(color), 1 + b.Activity[e.From] * 2), Position(e.From), Position(e.To));
        }
        for (int i = 0; i < b.Names.Length; i++)
        {
            var p = Position(i); double value = b.Activity[i];
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb((byte)(35+value*50),(byte)(65+value*180),(byte)(80+value*150))), new Pen(Brushes.SlateGray,1),p,9+value*4,9+value*4);
            Text(dc, $"{b.Names[i]} {value:F2}",p.X-65,p.Y+13,10);
        }
        Text(dc,"Internal drives (0-1)",18,420,14);
        for (int i = 0; i < 6; i++)
        {
            double x = 18+(i%3)*235, y=452+(i/3)*45;
            Text(dc,$"{Brain.DriveNames[i]}  {b.Drives[i]:F2}",x,y);
            dc.DrawRectangle(Brushes.Teal,null,new(x,y+20,b.Drives[i]*195,5));
        }
        Text(dc,"Threat and reinforcement (0-1)",750,420,14);
        string[] signalNames = ["Reward", "Aversion", "Acute arousal", "Slow stress", "Sensitization", "Spray contact"];
        double[] signals = [b.Dopamine, b.Threat.Aversion, b.Threat.AcuteArousal, b.Threat.Stress, b.Threat.Sensitization, b.Activity[Brain.SprayContact]];
        System.Windows.Media.Brush[] signalColors = [Brushes.MediumPurple, Brushes.HotPink, Brushes.Gold, Brushes.DarkOrange, Brushes.Salmon, Brushes.Cyan];
        for (int i = 0; i < signals.Length; i++)
        {
            double y = 447 + i * 21;
            Text(dc,$"{signalNames[i]} {signals[i]:F3}",750,y,11,signalColors[i]);
            dc.DrawRectangle(signalColors[i],null,new(925,y+5,signals[i]*200,5));
        }
        Text(dc,$"Hunger {b.Hunger:F2}   Energy {b.Energy:F2}   Reward (PAM-like) {b.Dopamine:F3}   Eaten {simulation.Consumed:F2}",18,770,14);
        Text(dc,"Cue weights: reward lowers avoidance; punishment lowers approach. Both start at 1.",18,800,14);
        for(int k=0;k<4;k++) Text(dc,$"{b.Names[28+k]}: avoid={b.Plasticity.Weights[k]:F3}  approach={b.Plasticity.ApproachWeights[k]:F3}  trace={b.Plasticity.Eligibility[k]:F3}",18+k%2*570,830+k/2*28,12);
        Text(dc,$"Motor: force {simulation.Motor.Forward:F2}   turn {simulation.Motor.Turn:F2}   brake {simulation.Motor.Brake:F2}",18,552,14);
        Text(dc,"120s history - thrust cyan / turn yellow (0.5 neutral) / brake coral / reward purple",18,580,12);
        System.Windows.Media.Brush[] colors = [Brushes.Turquoise,Brushes.Gold,Brushes.Salmon,Brushes.MediumPurple];
        var samples = history.ToArray();
        for (int ch=0;ch<4;ch++)
            for(int i=1;i<samples.Length;i++)
                dc.DrawLine(new Pen(colors[ch],1.5),new(18+(i-1)*.92,710-samples[i-1][ch]*100),new(18+i*.92,710-samples[i][ch]*100));
        Text(dc,"Sensory circles show filtered input. Connections flow left - right; recurrent links also shown.",18,731,11);
        Text(dc,$"Aversion {b.Threat.Aversion:F3}   Acute arousal (OA-like) {b.Threat.AcuteArousal:F3}   Stress {b.Threat.Stress:F3}   Sensitization {b.Threat.Sensitization:F3}",18,910,14);
        Text(dc,$"Spray receptors L {b.Activity[Brain.SprayLeft]:F3} / R {b.Activity[Brain.SprayRight]:F3} / contact {b.Activity[Brain.SprayContact]:F3}   Learned threat {b.Activity[Brain.LearnedThreat]:F3}",18,939,14);
        Text(dc,"120s history - aversion magenta / acute gold / stress orange / contact cyan / threat white",18,975,12);
        System.Windows.Media.Brush[] stressColors = [Brushes.HotPink, Brushes.Gold, Brushes.DarkOrange, Brushes.Cyan, Brushes.White];
        for (int ch = 4; ch <= 8; ch++)
            for (int i = 1; i < samples.Length; i++)
                dc.DrawLine(new Pen(stressColors[ch-4],1.5),new(18+(i-1)*.92,1120-samples[i-1][ch]*110),new(18+i*.92,1120-samples[i][ch]*110));
        Text(dc,"Mean plastic weights over 120s - avoidance teal / approach pink (per-cue values above)",18,1145,12);
        for (int ch = 9; ch <= 10; ch++)
            for (int i = 1; i < samples.Length; i++)
                dc.DrawLine(new Pen(ch == 9 ? Brushes.Turquoise : Brushes.HotPink,1.5),new(18+(i-1)*.92,1280-samples[i-1][ch]*100),new(18+i*.92,1280-samples[i][ch]*100));
        paintMeter?.Record(Stopwatch.GetElapsedTime(began).TotalSeconds);
    }
}
internal sealed class Inspector : Window
{
    private readonly Simulation simulation = new(Native.ReadHabitat());
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Overlay overlay;
    private readonly BrainView graph;
    private readonly VisionRuntime vision;
    private FlyVisionWindow? visionWindow;
    private readonly System.Windows.Threading.DispatcherTimer updateTimer = new(System.Windows.Threading.DispatcherPriority.Normal) { Interval=TimeSpan.FromMilliseconds(4) };
    private readonly TextBlock status = new() { Margin = new Thickness(10), Foreground = Brushes.LightGray };
    private double last, accumulator, sampled, topology, rateTime, lastRender;
    private int frames, ticks;
    private bool paused, sugarKey, sprayKey, toolKey;
    private double dropAt = double.PositiveInfinity;
    private int pendingTool;
    private readonly System.Windows.Controls.ComboBox tool = new()
    {
        Width = 95, Margin = new Thickness(8,0,0,0), SelectedIndex = 0,
        Items = { new ComboBoxItem { Content = "Sugar" }, new ComboBoxItem { Content = "Fly Spray" } }
    };
    private readonly List<SprayWindow> sprayWindows = [];
    private readonly List<SugarWindow> sugarWindows = [];
    private Vec lastHeldPosition;
    public Inspector()
    {
        CaptureExclusion.Register(this);
        vision = new(clock);
        Title = "Fly Guy - neural laboratory";
        Width = Math.Min(1280, SystemParameters.WorkArea.Width);
        Height = Math.Min(1000, SystemParameters.WorkArea.Height);
        Background = new SolidColorBrush(Color.FromRgb(19,25,35));
        var root = new DockPanel(); Content = root;
        graph = new BrainView(simulation,vision.BrainPaintMeter) { Width=1480,Height=1310 };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin=new Thickness(10) };
        var pause = new System.Windows.Controls.Button { Content="Pause / resume", Padding=new Thickness(12,5,12,5) };
        pause.Click += (_,_) => paused=!paused;
        var exit = new System.Windows.Controls.Button { Content="Exit creature", Margin=new Thickness(10,0,0,0), Padding=new Thickness(12,5,12,5) };
        exit.Click += (_,_) => Close(); buttons.Children.Add(pause); buttons.Children.Add(exit);
        buttons.Children.Add(tool);
        var drop=new System.Windows.Controls.Button { Content="Apply at cursor in 3s", Margin=new Thickness(8,0,0,0) };
        drop.Click+=(_,_)=> { pendingTool=tool.SelectedIndex; dropAt=clock.Elapsed.TotalSeconds+3; }; buttons.Children.Add(drop);
        var reset=new System.Windows.Controls.Button { Content="Reset brain", Margin=new Thickness(8,0,0,0) };
        reset.Click+=(_,_)=> { simulation.ResetBrain(); graph.ClearHistory(); }; buttons.Children.Add(reset);
        var flyVision = new System.Windows.Controls.Button { Content="Fly Vision", Margin=new Thickness(8,0,0,0), Padding=new Thickness(8,3,8,3) };
        flyVision.Click += (_,_) =>
        {
            if (visionWindow is null)
            {
                visionWindow = new(vision,simulation);
                visionWindow.Closed += (_,_) => visionWindow=null;
                visionWindow.Show();
            }
            else { if (visionWindow.WindowState==WindowState.Minimized) visionWindow.WindowState=WindowState.Normal; visionWindow.Activate(); }
        };
        buttons.Children.Add(flyVision);
        var help=new TextBlock { Text="Select Sugar or Fly Spray, then apply and move the cursor within 3 seconds. Ctrl+Shift+Space applies the selected tool immediately; Ctrl+Shift+S = sugar, Ctrl+Shift+F = spray. Escape cancels a countdown. Tools pause with the simulation. Grab the fly with left mouse.", Foreground=Brushes.LightGray, Margin=new Thickness(10), TextWrapping=TextWrapping.Wrap };
        DockPanel.SetDock(help,Dock.Top); root.Children.Add(help);
        DockPanel.SetDock(buttons,Dock.Top); root.Children.Add(buttons);
        var settingsPanel = new WrapPanel { Margin = new Thickness(10,0,10,6) };
        var settingsMessage = new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(6) };
        System.Windows.Controls.TextBox DecayInput(string label, double value)
        {
            settingsPanel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, Margin = new Thickness(5) });
            var input = new System.Windows.Controls.TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 45, Margin = new Thickness(2) };
            settingsPanel.Children.Add(input); return input;
        }
        var acute = DecayInput("Decay seconds: acute", 2);
        var stress = DecayInput("stress", 35);
        var aversion = DecayInput("aversion", 1.2);
        var sensitization = DecayInput("sensitization", 180);
        var apply = new System.Windows.Controls.Button { Content = "Apply rates", Margin = new Thickness(6,0,0,0) };
        apply.Click += (_,_) =>
        {
            var inputs = new[] { acute, stress, aversion, sensitization };
            double[] values = new double[4];
            for (int i = 0; i < inputs.Length; i++)
                if (!double.TryParse(inputs[i].Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]) || values[i] < .05 || values[i] > 3600)
                { settingsMessage.Text = "Use 0.05-3600 seconds."; return; }
            simulation.Brain.ThreatSettings = new() { AcuteDecaySeconds = values[0], StressDecaySeconds = values[1], AversionDecaySeconds = values[2], SensitizationDecaySeconds = values[3] };
            settingsMessage.Text = "Rates applied.";
        };
        settingsPanel.Children.Add(apply); settingsPanel.Children.Add(settingsMessage);
        DockPanel.SetDock(settingsPanel,Dock.Top); root.Children.Add(settingsPanel);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        root.Children.Add(new ScrollViewer { Content=graph, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        overlay = new Overlay(simulation);
        updateTimer.Tick += Update;
        Loaded += (_,_) => { overlay.Show(); last=clock.Elapsed.TotalSeconds; updateTimer.Start(); CompositionTarget.Rendering += Render; };
        Closed += (_,_) => { updateTimer.Stop(); vision.Dispose(); CompositionTarget.Rendering -= Render; visionWindow?.Close(); overlay.Close(); foreach(var window in sugarWindows) window.Close(); foreach(var window in sprayWindows) window.Close(); };
    }
    private void Update(object? sender, EventArgs args)
    {
        double now=clock.Elapsed.TotalSeconds, elapsed=now-last; last=now;
        if(now-topology>2) { simulation.Habitat=Native.ReadHabitat(); simulation.Creature.Position=simulation.Habitat.Constrain(simulation.Creature.Position); topology=now; }
        if(Native.GetCursorPos(out var inputCursor))
        {
            bool modifiers=Native.GetAsyncKeyState(0x11)<0 && Native.GetAsyncKeyState(0x10)<0;
            bool key=modifiers && Native.GetAsyncKeyState(0x53)<0;
            bool spray=modifiers && Native.GetAsyncKeyState(0x46)<0;
            bool selected=modifiers && Native.GetAsyncKeyState(0x20)<0;
            if (Native.GetAsyncKeyState(0x1B)<0 || paused) dropAt=double.PositiveInfinity;
            if (!paused)
            {
                Vec position = new(inputCursor.X,inputCursor.Y);
                if (key && !sugarKey) simulation.DropSugar(position);
                if (spray && !sprayKey) simulation.SprayAt(position);
                if ((selected && !toolKey) || now>=dropAt)
                {
                    int chosen = now>=dropAt ? pendingTool : tool.SelectedIndex;
                    if (chosen == 1) simulation.SprayAt(position); else simulation.DropSugar(position);
                    dropAt=double.PositiveInfinity;
                }
            }
            sugarKey=key; sprayKey=spray; toolKey=selected;
        }
        if(simulation.Held)
        {
            simulation.Creature.Velocity=(simulation.Creature.Position-lastHeldPosition)*(1/Math.Max(.001,elapsed));
            if(simulation.Creature.Velocity.Length>1500) simulation.Creature.Velocity*=1500/simulation.Creature.Velocity.Length;
        }
        lastHeldPosition=simulation.Creature.Position;
        vision.Advance(now,simulation,paused);
        if(!paused)
        {
            accumulator+=Math.Min(elapsed,.1);
            double dt=1/vision.Eyes.Settings.BrainHz;
            while(accumulator>=dt)
            {
                long began=Stopwatch.GetTimestamp();
                simulation.Step(vision.Eyes.Signals,dt);
                vision.BrainMeter.Record(Stopwatch.GetElapsedTime(began).TotalSeconds);
                accumulator-=dt; ticks++;
            }
        }
        else accumulator=0;
        if(now-sampled>=.1) { if(!paused) graph.Sample(); sampled=now; }
        if(now-rateTime>=1)
        {
            status.Text=$"{(paused ? "PAUSED" : "LIVE")}   {frames/(now-rateTime):F0} render FPS   {ticks/(now-rateTime):F0} ticks/s   speed {simulation.Creature.Velocity.Length:F1} px/s   {(double.IsFinite(dropAt) ? $"{(pendingTool == 1 ? "SPRAY" : "SUGAR")} in {Math.Max(0,dropAt-now):F1}s" : $"Spray clouds: {simulation.Spray.Count}")}\n{vision.Status}";
            frames=ticks=0; rateTime=now;
        }
    }
    private void Render(object? sender, EventArgs args)
    {
        double now=clock.Elapsed.TotalSeconds;
        if (now<lastRender) return;
        double period=1/vision.Eyes.Settings.RenderHz;
        lastRender=now-lastRender>=period ? now+period : lastRender+period;
        long began=Stopwatch.GetTimestamp();
        foreach(var food in simulation.Sugar)
            if(!sugarWindows.Any(w=>ReferenceEquals(w.Food,food))) { var window=new SugarWindow(food); sugarWindows.Add(window); window.Show(); }
        foreach(var window in sugarWindows.ToArray())
            if(!simulation.Sugar.Contains(window.Food)) { window.Close(); sugarWindows.Remove(window); }
            else window.Opacity=.3+.7*window.Food.Amount;
        foreach (var cloud in simulation.Spray)
            if (!sprayWindows.Any(w=>ReferenceEquals(w.Cloud,cloud))) { var window=new SprayWindow(cloud); sprayWindows.Add(window); window.Show(); }
        foreach (var window in sprayWindows.ToArray())
            if (!simulation.Spray.Contains(window.Cloud)) { window.Close(); sprayWindows.Remove(window); }
            else window.Draw();
        overlay.Draw(simulation.Creature,simulation.Held); frames++;
        visionWindow?.Refresh(now);
        vision.RenderMeter.Record(Stopwatch.GetElapsedTime(began).TotalSeconds);
    }
}


