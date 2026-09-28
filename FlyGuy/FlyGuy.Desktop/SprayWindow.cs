using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FlyGuy.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace FlyGuy.Desktop;

internal sealed class SprayWindow : Window
{
    public SprayCloud Cloud { get; }
    private nint handle;
    public SprayWindow(SprayCloud cloud)
    {
        Cloud = cloud;
        Width = Height = cloud.Radius * 2;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowActivated = false;
        ShowInTaskbar = false; IsHitTestVisible = false; ResizeMode = ResizeMode.NoResize;
        Content = new System.Windows.Shapes.Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromArgb(110, 240, 110, 180), Color.FromArgb(0, 240, 110, 180)),
            Stroke = new SolidColorBrush(Color.FromArgb(90, 240, 110, 180)), StrokeThickness = 1
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(handle, -20, Native.GetWindowLong(handle, -20) | 0x20 | 0x80 | 0x08000000);
            Draw();
        };
    }
    public void Draw()
    {
        // Native bounds keep the visible field in physical pixels, like the receptors.
        Native.SetWindowPos(handle, new nint(-1), (int)(Cloud.Position.X - Cloud.Radius),
            (int)(Cloud.Position.Y - Cloud.Radius), (int)(Cloud.Radius * 2), (int)(Cloud.Radius * 2), 0x0010);
        Opacity = Math.Sqrt(Cloud.Concentration);
    }
}
