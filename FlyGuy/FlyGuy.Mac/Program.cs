using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using FlyGuy.Core;

namespace FlyGuy.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            Console.Error.WriteLine("Use FlyGuy.Desktop on Windows. This frontend requires macOS 14 or later.");
            return 1;
        }
        if (args.Contains("--smoke-test"))
        {
            // No permission prompt or screenshots in the noninteractive packaging check.
            var world = Native.ReadHabitat();
            var simulation = new Simulation(world);
            for (int i = 0; i < 120; i++) simulation.Step();
            if (!world.Contains(simulation.Creature.Position)) return 2;
            Console.WriteLine($"Native bridge loaded; {world.Regions.Length} display(s); simulation OK.");
            return 0;
        }
        return AppBuilder.Configure<MacApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class MacApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new Inspector();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class Inspector : Window
{
    private readonly Simulation simulation = new(Native.ReadHabitat());
    private readonly BinocularVision eyes = new();
    private readonly DesktopCapture capture = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.0 / 60) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock metrics = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock populations = new() { FontFamily = new FontFamily("Menlo"), FontSize = 12 };
    private readonly TextBlock countdown = new();
    private readonly Overlay overlay;
    private readonly RetinaView retina;
    private bool paused, visionEnabled;
    private double last, accumulator, nextVision, lastVision, nextReport, nextHabitat;
    private double toolAt;
    private bool sprayPending;

    public Inspector()
    {
        Title = "Fly Guy — macOS"; Width = 720; Height = 780;
        MinWidth = 560; MinHeight = 480;
        retina = new RetinaView(eyes) { Height = 160 };
        overlay = new Overlay(simulation);
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "FLY GUY", FontSize = 26, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = "Synthetic neural simulation • drag the fly to reposition it", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        Button Add(string label, Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => action(); buttons.Children.Add(button); return button;
        }
        Button? pause = null;
        pause = Add("Pause", () => { paused = !paused; pause!.Content = paused ? "Resume" : "Pause"; toolAt = 0; });
        Add("Sugar in 3s", () => Schedule(false));
        Add("Spray in 3s", () => Schedule(true));
        Add("Cancel", () => toolAt = 0);
        Add("Reset brain", simulation.ResetBrain);
        Add("Bring fly here", () => simulation.Creature.Position = simulation.Habitat.Constrain(Native.Cursor));
        Add("Exit", Close);
        panel.Children.Add(buttons);
        panel.Children.Add(countdown);
        var vision = new CheckBox { Content = "Enable desktop vision (requires Screen Recording permission)" };
        vision.IsCheckedChanged += (_, _) =>
        {
            visionEnabled = vision.IsChecked == true;
            if (visionEnabled) Native.fg_permission(1);
            else eyes.Clear();
        };
        panel.Children.Add(vision);
        panel.Children.Add(status);
        panel.Children.Add(new TextBlock { Text = "Left / right retina — processed neural activity", FontWeight = FontWeight.Bold });
        panel.Children.Add(retina);
        panel.Children.Add(metrics);
        panel.Children.Add(new TextBlock { Text = "Brain populations", FontWeight = FontWeight.Bold });
        panel.Children.Add(populations);
        Content = new ScrollViewer { Content = panel };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) toolAt = 0; };
        Opened += (_, _) => { overlay.Show(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); capture.Dispose(); overlay.Close(); };
        timer.Tick += (_, _) => Tick();
    }
    private void Schedule(bool spray)
    {
        if (paused) return;
        sprayPending = spray; toolAt = clock.Elapsed.TotalSeconds + 3;
    }
    private void Tick()
    {
        double now = clock.Elapsed.TotalSeconds, elapsed = Math.Min(.1, now - last); last = now;
        if (now >= nextHabitat)
        {
            simulation.Habitat = Native.ReadHabitat(); nextHabitat = now + 2;
            simulation.Creature.Position = simulation.Habitat.Constrain(simulation.Creature.Position);
        }
        capture.Request(simulation.Creature.Position, simulation.Habitat.Regions, visionEnabled && !paused);
        if (!paused && visionEnabled && now >= nextVision)
        {
            double stamp = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (capture.Latest is { } frame && stamp - frame.Timestamp < .5)
            {
                double dt = lastVision == 0 ? 1.0 / 30 : Math.Min(.25, now - lastVision);
                eyes.Sample(frame, simulation.Creature.Position, simulation.Creature.Heading, dt);
                eyes.Process(dt);
            }
            else eyes.Clear();
            lastVision = now;
            nextVision = now + 1.0 / 30;
        }
        if (toolAt > 0 && now >= toolAt)
        {
            if (sprayPending) simulation.SprayAt(Native.Cursor); else simulation.DropSugar(Native.Cursor);
            toolAt = 0;
        }
        countdown.Text = toolAt > 0 ? $"Move the cursor: {(sprayPending ? "spray" : "sugar")} in {Math.Ceiling(toolAt - now)}…" : "";
        if (!paused)
        {
            accumulator += elapsed;
            while (accumulator >= Simulation.Dt) { simulation.Step(eyes.Signals); accumulator -= Simulation.Dt; }
        }
        else accumulator = 0;
        overlay.Draw();
        if (now < nextReport) return;
        nextReport = now + .1;
        status.Text = visionEnabled ? capture.Status : "Desktop vision off. No screen pixels are captured.";
        var b = simulation.Brain;
        metrics.Text = $"Hunger {b.Hunger:F2}   Energy {b.Energy:F2}   Reward {b.Dopamine:F3}   Eaten {simulation.Consumed:F2}\n" +
            $"Aversion {b.Threat.Aversion:F3}   Arousal {b.Threat.AcuteArousal:F3}   Stress {b.Threat.Stress:F3}\n" +
            $"Thrust {simulation.Motor.Forward:F2}   Turn {simulation.Motor.Turn:F2}   Brake {simulation.Motor.Brake:F2}\n" +
            $"Sugar sources {simulation.Sugar.Count}   Spray clouds {simulation.Spray.Count}\n" +
            $"Avoidance weights: {string.Join("  ", b.Plasticity.Weights.Select(w => w.ToString("F3")))}\n" +
            $"Approach weights: {string.Join("  ", b.Plasticity.ApproachWeights.Select(w => w.ToString("F3")))}";
        populations.Text = string.Join("\n", b.Names.Select((name, i) => $"{name,-22} {b.Activity[i]:F3}"));
        retina.InvalidateVisual();
    }
}

internal sealed class Overlay : Window
{
    private readonly Simulation simulation;
    private readonly FlyView view;
    private Vec anchor;
    public Overlay(Simulation simulation)
    {
        this.simulation = simulation;
        Width = Height = 96; CanResize = false; SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Content = view = new FlyView(simulation.Creature);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            if (Math.Pow(p.X - 48, 2) + Math.Pow(p.Y - 48, 2) > 32 * 32) return;
            anchor = Native.Cursor - simulation.Creature.Position;
            simulation.Held = true; simulation.Touch = 1; e.Pointer.Capture(view);
        };
        PointerMoved += (_, _) =>
        {
            if (simulation.Held) simulation.Creature.Position = simulation.Habitat.Constrain(Native.Cursor - anchor);
        };
        PointerReleased += (_, e) => { simulation.Held = false; e.Pointer.Capture(null); };
        PointerCaptureLost += (_, _) => simulation.Held = false;
    }
    public void Draw()
    {
        // Avalonia's macOS backend uses desktop points for Window.Position; its
        // Screen bounds use the same global origin, independent of backing scale.
        Position = new PixelPoint((int)Math.Round(simulation.Creature.Position.X - 48),
            (int)Math.Round(simulation.Creature.Position.Y - 48));
        view.InvalidateVisual();
    }
}

internal sealed class FlyView(Creature creature) : Control
{
    public override void Render(DrawingContext dc)
    {
        using var transform = dc.PushTransform(Matrix.CreateTranslation(-48, -48) *
            Matrix.CreateRotation(creature.Heading) * Matrix.CreateTranslation(48, 48));
        var leg = new Pen(Brushes.DarkSlateGray, 2.5);
        for (int i = 0; i < 3; i++)
        {
            double x = 37 + i * 10, stride = Math.Sin(creature.Gait + i * 2) * 6;
            dc.DrawLine(leg, new(x, 43), new(x + stride - 4, 29));
            dc.DrawLine(leg, new(x, 53), new(x - stride - 4, 67));
        }
        var wing = new SolidColorBrush(Color.FromArgb(125, 180, 235, 255));
        dc.DrawEllipse(wing, null, new(40, 37), 17, 7);
        dc.DrawEllipse(wing, null, new(40, 59), 17, 7);
        dc.DrawEllipse(Brushes.MediumTurquoise, new Pen(Brushes.DarkSlateGray, 2), new(48, 48), 19, 12);
        dc.DrawEllipse(Brushes.Teal, null, new(62, 48), 10, 10);
        foreach (int y in new[] { 43, 53 })
        {
            dc.DrawEllipse(Brushes.White, null, new(66, y), 4, 4);
            dc.DrawEllipse(Brushes.Black, null, new(68, y), 2, 2);
        }
    }
}

internal sealed class RetinaView(BinocularVision eyes) : Control
{
    public override void Render(DrawingContext dc)
    {
        int columns = eyes.Settings.Columns, rows = eyes.Settings.Rows;
        double cell = Math.Min((Bounds.Width - 16) / (columns * 2), Bounds.Height / rows);
        if (cell <= 0) return;
        var retinas = new[] { eyes.Left, eyes.Right };
        for (int eye = 0; eye < 2; eye++)
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    byte value = (byte)Math.Clamp(retinas[eye].Neurons[i] * 255, 0, 255);
                    var color = eyes.Available && retinas[eye].ProcessedValid[i]
                        ? Color.FromRgb(0, value, value) : Color.FromRgb(40, 25, 40);
                    dc.DrawRectangle(new SolidColorBrush(color), null,
                        new Rect(eye * (columns * cell + 16) + col * cell, row * cell, cell - 1, cell - 1));
                }
    }
}
