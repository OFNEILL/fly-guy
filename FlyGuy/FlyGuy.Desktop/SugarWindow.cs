using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using FlyGuy.Core;
using Brushes = System.Windows.Media.Brushes;
namespace FlyGuy.Desktop;

internal sealed class SugarWindow : Window
{
    public Sugar Food { get; }
    public SugarWindow(Sugar food)
    {
        Food=food; Width=Height=22; WindowStyle=WindowStyle.None; AllowsTransparency=true;
        Background=Brushes.Transparent; Topmost=true; ShowActivated=false; ShowInTaskbar=false;
        IsHitTestVisible=false;
        Content=new System.Windows.Shapes.Ellipse { Width=14, Height=14, Fill=Brushes.PaleGoldenrod, Stroke=Brushes.Goldenrod, StrokeThickness=2 };
        SourceInitialized+=(_,_)=>
        {
            var h=new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h,-20,Native.GetWindowLong(h,-20)|0x20|0x80|0x08000000);
            var dpi=VisualTreeHelper.GetDpi(this);
            Native.SetWindowPos(h,new nint(-1),(int)(food.Position.X-11*dpi.DpiScaleX),(int)(food.Position.Y-11*dpi.DpiScaleY),0,0,0x0011);
        };
    }
}
