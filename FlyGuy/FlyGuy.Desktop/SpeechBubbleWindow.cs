using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FlyGuy.Core;
using Brushes = System.Windows.Media.Brushes;

namespace FlyGuy.Desktop;

internal sealed class SpeechBubbleWindow : Window
{
    private readonly TextBlock words = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16, Foreground = Brushes.DarkSlateGray, MaxHeight = 300 };
    private readonly System.Diagnostics.Stopwatch visibleFor = new();
    private nint handle;
    private double durationSeconds = 12;
    public SpeechBubbleWindow()
    {
        Title = "Fly Guy speech bubble";
        Width = 300; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Background = Brushes.FloralWhite, BorderBrush = Brushes.Teal, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(16,12,16,12), Child = words
        });
        stack.Children.Add(new System.Windows.Shapes.Polygon
        {
            Points = new PointCollection { new(135,0), new(155,0), new(145,14) },
            Fill = Brushes.Teal, Height = 14, HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        });
        Content = stack;
        CaptureExclusion.Register(this);
        SourceInitialized += (_,_) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x20 | 0x80 | 0x08000000);
        };
    }
    public void Speak(string text, Creature fly, Habitat habitat, double durationSeconds = 12)
    {
        this.durationSeconds = durationSeconds;
        words.Text = text;
        visibleFor.Restart();
        // Measure while hidden, position at the fly, then reveal without taking focus.
        Opacity = 0; Show(); UpdateLayout(); Follow(fly, habitat); Opacity = 1;
    }
    public void Follow(Creature fly, Habitat habitat)
    {
        if (!IsVisible) return;
        if (visibleFor.Elapsed.TotalSeconds >= durationSeconds) { Hide(); return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = ActualWidth * dpi.DpiScaleX, height = ActualHeight * dpi.DpiScaleY;
        var region = habitat.Regions.FirstOrDefault(r => r.Contains(fly.Position));
        if (region.Width <= 0) region = habitat.Regions[0];
        double x = Math.Clamp(fly.Position.X - width / 2, region.X, Math.Max(region.X, region.X + region.Width - width));
        double y = fly.Position.Y - height - 28;
        if (y < region.Y) y = fly.Position.Y + 30;
        y = Math.Clamp(y, region.Y, Math.Max(region.Y, region.Y + region.Height - height));
        Native.SetWindowPos(handle, new nint(-1), (int)x, (int)y, 0, 0, 0x0011);
    }
}
