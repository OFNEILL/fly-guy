using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using FlyGuy.Core;
using Brushes = System.Windows.Media.Brushes;
namespace FlyGuy.Desktop;

internal sealed class CigaretteWindow : Window
{
    public Cigarette Cigarette { get; }
    private readonly System.Windows.Controls.Border body;
    public CigaretteWindow(Cigarette cigarette)
    {
        Title = "Fly Guy cigarette";
        Cigarette=cigarette; Width=44; Height=22; WindowStyle=WindowStyle.None; AllowsTransparency=true;
        Background=Brushes.Transparent; Topmost=true; ShowActivated=false; ShowInTaskbar=false;
        IsHitTestVisible=false;
        Content=body=new System.Windows.Controls.Border { Width=36, Height=9, Background=Brushes.WhiteSmoke, BorderBrush=Brushes.SandyBrown, BorderThickness=new Thickness(0,0,9,0), CornerRadius=new CornerRadius(2) };
        SourceInitialized+=(_,_)=>
        {
            var h=new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(h,-20,Native.GetWindowLong(h,-20)|0x20|0x80|0x08000000);
            var dpi=VisualTreeHelper.GetDpi(this);
            Native.SetWindowPos(h,new nint(-1),(int)(cigarette.Position.X-22*dpi.DpiScaleX),(int)(cigarette.Position.Y-11*dpi.DpiScaleY),0,0,0x0011);
        };
    }
    public void Draw()
    {
        body.Width = 9 + 27 * Cigarette.RemainingFraction;
        body.BorderBrush = Cigarette.IsLit ? Brushes.OrangeRed : Brushes.SandyBrown;
    }
}

