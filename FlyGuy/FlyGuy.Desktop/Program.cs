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
    public Overlay(Creature creature)
    {
        Width = Height = 96; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        ShowActivated = false; ResizeMode = ResizeMode.NoResize; IsHitTestVisible = false;
        Content = view = new CreatureView { Creature = creature };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x20 | 0x80 | 0x08000000);
        };
    }
    public void Draw(Creature creature)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Native.SetWindowPos(handle, new nint(-1), (int)(creature.Position.X - 48 * dpi.DpiScaleX), (int)(creature.Position.Y - 48 * dpi.DpiScaleY), 0, 0, 0x0011);
        view.InvalidateVisual();
    }
}
internal sealed class BrainView(Simulation simulation) : FrameworkElement
{
    private readonly Queue<double[]> history = new();
    public void Sample()
    {
        history.Enqueue([simulation.Motor.Forward, (simulation.Motor.Turn + 1) / 2, simulation.Motor.Brake, simulation.Brain.Activity[11]]);
        if (history.Count > 240) history.Dequeue();
        InvalidateVisual();
    }
    private static void Text(DrawingContext dc, string text, double x, double y, double size = 12, System.Windows.Media.Brush? brush = null)
        => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.LightGray, 1), new(x,y));
    protected override void OnRender(DrawingContext dc)
    {
        var b = simulation.Brain;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(19,25,35)), null, new Rect(0,0,ActualWidth,ActualHeight));
        Text(dc, "SYNTHETIC RATE NETWORK • cyan excitation / coral inhibition", 16,12,14);
        Point Position(int i) => i < 11 ? new(110, 60 + i * 31) : i < 16 ? new(360, 70 + (i-11)*66) : new(620,90+(i-16)*80);
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
        Text(dc,"Internal drives (0–1)",18,420,14);
        for (int i = 0; i < 6; i++)
        {
            double x = 18+(i%3)*235, y=452+(i/3)*45;
            Text(dc,$"{Brain.DriveNames[i]}  {b.Drives[i]:F2}",x,y);
            dc.DrawRectangle(Brushes.Teal,null,new(x,y+20,b.Drives[i]*195,5));
        }
        Text(dc,$"Motor: force {simulation.Motor.Forward:F2}   turn {simulation.Motor.Turn:F2}   brake {simulation.Motor.Brake:F2}",18,552,14);
        Text(dc,"24s history • thrust cyan / turn yellow (0.5 neutral) / brake coral / Explore L purple",18,580,12);
        System.Windows.Media.Brush[] colors = [Brushes.Turquoise,Brushes.Gold,Brushes.Salmon,Brushes.MediumPurple];
        var samples = history.ToArray();
        for (int ch=0;ch<4;ch++)
            for(int i=1;i<samples.Length;i++)
                dc.DrawLine(new Pen(colors[ch],1.5),new(18+(i-1)*2.8,710-samples[i-1][ch]*100),new(18+i*2.8,710-samples[i][ch]*100));
        Text(dc,"Sensory circles show filtered input. Connections flow left → right; recurrent links also shown.",18,731,11);
    }
}
internal sealed class Inspector : Window
{
    private readonly Simulation simulation = new(Native.ReadHabitat());
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Overlay overlay;
    private readonly BrainView graph;
    private readonly TextBlock status = new() { Margin = new Thickness(10), Foreground = Brushes.LightGray };
    private double last, accumulator, sampled, topology, rateTime;
    private int frames, ticks;
    private bool paused;
    public Inspector()
    {
        Title = "Fly Guy — neural laboratory"; Width=770; Height=900;
        Background = new SolidColorBrush(Color.FromRgb(19,25,35));
        var root = new DockPanel(); Content = root;
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin=new Thickness(10) };
        var pause = new System.Windows.Controls.Button { Content="Pause / resume", Padding=new Thickness(12,5,12,5) };
        pause.Click += (_,_) => paused=!paused;
        var exit = new System.Windows.Controls.Button { Content="Exit creature", Margin=new Thickness(10,0,0,0), Padding=new Thickness(12,5,12,5) };
        exit.Click += (_,_) => Close(); buttons.Children.Add(pause); buttons.Children.Add(exit);
        buttons.Children.Add(new TextBlock { Text="Minimize this inspector to watch your desktop.", Foreground=Brushes.LightGray, Margin=new Thickness(8) });
        DockPanel.SetDock(buttons,Dock.Top); root.Children.Add(buttons);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        graph = new BrainView(simulation) { Width=720,Height=760 };
        root.Children.Add(new ScrollViewer { Content=graph, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        overlay = new Overlay(simulation.Creature);
        Loaded += (_,_) => { overlay.Show(); last=clock.Elapsed.TotalSeconds; CompositionTarget.Rendering += Render; };
        Closed += (_,_) => { CompositionTarget.Rendering -= Render; overlay.Close(); };
    }
    private void Render(object? sender, EventArgs args)
    {
        double now=clock.Elapsed.TotalSeconds, elapsed=now-last; last=now;
        if(now-topology>2) { simulation.Habitat=Native.ReadHabitat(); simulation.Creature.Position=simulation.Habitat.Constrain(simulation.Creature.Position); topology=now; }
        if(!paused)
        {
            accumulator+=Math.Min(elapsed,.1);
            if(Native.GetCursorPos(out var cursor))
                while(accumulator>=Simulation.Dt) { simulation.Step(new(cursor.X,cursor.Y)); accumulator-=Simulation.Dt; ticks++; }
            else accumulator=0;
        }
        else accumulator=0;
        overlay.Draw(simulation.Creature); frames++;
        if(now-sampled>=.1) { if(!paused) graph.Sample(); sampled=now; }
        if(now-rateTime>=1)
        {
            status.Text=$"{(paused ? "PAUSED" : "LIVE")}   {frames/(now-rateTime):F0} render FPS   {ticks/(now-rateTime):F0} ticks/s (target 120)   speed {simulation.Creature.Velocity.Length:F1} px/s   seed 7";
            frames=ticks=0; rateTime=now;
        }
    }
}
