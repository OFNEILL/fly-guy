using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlyGuy.Core;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace FlyGuy.Desktop;

internal sealed class FlyVisionWindow : Window
{
    private readonly VisionRuntime vision;
    private readonly VisionView view;
    private readonly TextBlock status = new() { Foreground = Brushes.LightGray, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { Foreground = Brushes.Gold, Margin = new Thickness(8) };
    private readonly ComboBox modes = new() { Width=170, Margin=new Thickness(8), SelectedIndex=1,
        Items = { new ComboBoxItem { Content="Raw" }, new ComboBoxItem { Content="Receptors" }, new ComboBoxItem { Content="Contrast / edges" }, new ComboBoxItem { Content="Motion" }, new ComboBoxItem { Content="Neural" } } };
    private double refreshed;
    public FlyVisionWindow(VisionRuntime vision, Simulation simulation)
    {
        this.vision = vision;
        Title = "Fly Vision - two eyes";
        Width = Math.Min(1180,SystemParameters.WorkArea.Width); Height = Math.Min(990,SystemParameters.WorkArea.Height);
        Background = new SolidColorBrush(Color.FromRgb(19,25,35));
        CaptureExclusion.Register(this);
        var root = new DockPanel(); Content = root;
        var controls = new WrapPanel();
        controls.Children.Add(new TextBlock { Text="Fly Vision", Foreground=Brushes.White, FontSize=18, Margin=new Thickness(10) });
        controls.Children.Add(modes);
        var map = new CheckBox { Content="Show sampling map", IsChecked=true, Foreground=Brushes.LightGray, Margin=new Thickness(10) };
        controls.Children.Add(map);
        var explanation = new TextBlock { Text="Grayscale eyes; no object recognition. Top = far, bottom = near. Pink hatch = unavailable.", Foreground=Brushes.LightGray, Margin=new Thickness(10), TextWrapping=TextWrapping.Wrap };
        DockPanel.SetDock(controls,Dock.Top); root.Children.Add(controls);
        DockPanel.SetDock(explanation,Dock.Top); root.Children.Add(explanation);

        var fields = new Dictionary<string,TextBox>();
        WrapPanel FieldRow(params (string Name, double Value)[] values)
        {
            var row = new WrapPanel { Margin = new Thickness(6,0,6,4) };
            foreach (var (name,value) in values)
            {
                row.Children.Add(new TextBlock { Text=name, Foreground=Brushes.LightGray, Margin=new Thickness(5) });
                var input = new TextBox { Text=value.ToString(CultureInfo.InvariantCulture), Width=45, Margin=new Thickness(2) };
                System.Windows.Automation.AutomationProperties.SetName(input,name);
                System.Windows.Automation.AutomationProperties.SetAutomationId(input,name.Replace(" ",""));
                fields.Add(name,input); row.Children.Add(input);
            }
            return row;
        }
        var settings = vision.Eyes.Settings;
        var geometry = FieldRow(("Columns",settings.Columns),("Rows",settings.Rows),("Left FOV",settings.Left.FieldOfViewDegrees),
            ("Right FOV",settings.Right.FieldOfViewDegrees),("Left yaw",settings.Left.YawDegrees),("Right yaw",settings.Right.YawDegrees),("Range px",settings.FarDistance));
        DockPanel.SetDock(geometry,Dock.Top); root.Children.Add(geometry);
        var rates = FieldRow(("Capture Hz",settings.CaptureHz),("Receptors Hz",settings.ReceptorHz),("Processing Hz",settings.ProcessingHz),("Brain Hz",settings.BrainHz),("Render Hz",settings.RenderHz));
        var apply = new Button { Content="Apply vision settings", Margin=new Thickness(6,0,6,0) };
        rates.Children.Add(apply); DockPanel.SetDock(rates,Dock.Top); root.Children.Add(rates);
        DockPanel.SetDock(message,Dock.Top); root.Children.Add(message);
        apply.Click += (_,_) =>
        {
            try
            {
                double Read(string key) => double.Parse(fields[key].Text,CultureInfo.InvariantCulture);
                double columns = Read("Columns"), rows = Read("Rows");
                if (columns != Math.Truncate(columns) || rows != Math.Truncate(rows) || columns < 4 || columns > 64 || rows < 2 || rows > 32)
                    throw new ArgumentException("Columns/rows must be integers: 4-64 and 2-32.");
                var next = vision.Eyes.Settings with
                {
                    Columns = (int)columns, Rows = (int)rows, FarDistance = Read("Range px"),
                    Left = settings.Left with { FieldOfViewDegrees=Read("Left FOV"), YawDegrees=Read("Left yaw") },
                    Right = settings.Right with { FieldOfViewDegrees=Read("Right FOV"), YawDegrees=Read("Right yaw") },
                    CaptureHz=Read("Capture Hz"), ReceptorHz=Read("Receptors Hz"), ProcessingHz=Read("Processing Hz"), BrainHz=Read("Brain Hz"), RenderHz=Read("Render Hz")
                };
                vision.Configure(next); message.Text="Vision settings applied; temporal history reset.";
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException) { message.Text=ex.Message; }
        };
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        view = new VisionView(vision,simulation) { Width=1090, Height=770 };
        modes.SelectionChanged += (_,_) => { view.Mode = modes.SelectedIndex; view.InvalidateVisual(); };
        map.Checked += (_,_) => { view.ShowMap=true; view.InvalidateVisual(); };
        map.Unchecked += (_,_) => { view.ShowMap=false; view.InvalidateVisual(); };
        root.Children.Add(new ScrollViewer { Content=view, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
    }
    public void Refresh(double now)
    {
        if (!IsVisible || now-refreshed < 1.0/15) return;
        refreshed=now;
        var frame=vision.SampledFrame;
        status.Text=$"{vision.Status} | Capture frames: {vision.CaptureMeter.Count}\n{vision.Rates(now)}\nSample age {(frame is null ? "unavailable" : $"{Math.Max(0,now-frame.Timestamp)*1000:F0} ms")}; receptor update {vision.SampledAt:F2}s; processed {vision.ProcessedAt:F2}s. Viewer capped at 15 Hz.";
        view.Mode=modes.SelectedIndex; view.InvalidateVisual();
    }
}

internal sealed class VisionView(VisionRuntime vision, Simulation simulation) : FrameworkElement
{
    public int Mode { get; set; } = 1;
    public bool ShowMap { get; set; } = true;
    private static void Text(DrawingContext dc, string value, double x, double y, double size=12, System.Windows.Media.Brush? brush=null)
        => dc.DrawText(new FormattedText(value,CultureInfo.InvariantCulture,System.Windows.FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush??Brushes.LightGray,1),new(x,y));
    private static BitmapSource Image(int width,int height,byte[] pixels) => BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
    protected override void OnRender(DrawingContext dc)
    {
        long began=System.Diagnostics.Stopwatch.GetTimestamp();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(19,25,35)),null,new(0,0,ActualWidth,ActualHeight));
        var eyes=vision.Eyes;
        DrawEye(dc,eyes.Left,20,"LEFT EYE",0); DrawEye(dc,eyes.Right,565,"RIGHT EYE",1);
        string legend=Mode switch
        {
            0 => "Raw: warped desktop pixel taps BEFORE pooling/filtering; each cell uses a 4x4 footprint by default.",
            1 => "Receptors: exact filtered luminance [0,1]; each enlarged square is one receptor. Colour is not sent to the brain.",
            2 => "Contrast: local center-surround; cyan = brighter, coral = darker, black = zero. Range [-1,1].",
            3 => "Motion: gold background = temporal change [0,1]; arrows = delayed-neighbor motion (CW/right, CCW/left; radial up/down).",
            _ => "Neural: per-receptor visual rate [0,1], pooled separately into each eye's brain input; numbers below are the actual inputs."
        };
        Text(dc,legend,20,310,12);
        Text(dc,vision.Eyes.Available ? "PIXEL INPUT ACTIVE" : "NO CURRENT PIXEL INPUT - visual signals are zero",20,335,14,vision.Eyes.Available?Brushes.Turquoise:Brushes.Salmon);
        Text(dc,"Other senses (explicit, NOT inferred from pixels)",ShowMap?410:20,385,15);
        var senses=simulation.Sensors.Values;
        double sx=ShowMap?410:20;
        Text(dc,$"Food odor L {senses[11]:F3} / R {senses[12]:F3}; taste contact {senses[13]:F3}",sx,418);
        Text(dc,$"Spray chemical L {senses[19]:F3} / R {senses[20]:F3}; contact {senses[21]:F3}",sx,443);
        Text(dc,$"Boundary feelers L {senses[2]:F2} / R {senses[3]:F2} / front {senses[4]:F2} (12 px)",sx,468);
        Text(dc,$"Touch {senses[14]:F3}; held {senses[15]:F0}; speed {senses[5]:F3}; moving {senses[6]:F3}",sx,493);
        Text(dc,$"Heading sin/cos {senses[9]:F3}/{senses[10]:F3}; ingestion {senses[17]:F3}",sx,518);
        Text(dc,$"Visual change {senses[7]:F3}; motion {senses[16]:F3}; quiet {senses[8]:F3}; looming disabled",sx,543);
        Text(dc,$"Motor neurons: thrust {simulation.Motor.Forward:F3}; turn {simulation.Motor.Turn:F3}; brake {simulation.Motor.Brake:F3}",sx,578,13);
        Text(dc,"Debug windows and the creature are excluded from capture. Sugar and spray remain visible.",20,730,12);
        Text(dc,"No OCR, object labels, cursor coordinates, or semantic desktop data reach the brain.",20,750,12);
        if (ShowMap) DrawMap(dc);
        vision.EyePaintMeter.Record(System.Diagnostics.Stopwatch.GetElapsedTime(began).TotalSeconds);
    }
    private void DrawEye(DrawingContext dc, EyeVision eye, double x, string title, int side)
    {
        Text(dc,title,x,10,20,side==0?Brushes.Cyan:Brushes.HotPink);
        var validity=Mode>=2?eye.ProcessedValid:eye.Valid;
        Text(dc,$"{eye.Settings.Columns} x {eye.Settings.Rows} | FOV {eye.Geometry.FieldOfViewDegrees:G} deg | yaw {eye.Geometry.YawDegrees:G} deg | valid {validity.Count(v=>v)}/{validity.Length}",x,40);
        const double width=500,height=200;
        if (Mode == 0)
        {
            RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.NearestNeighbor);
            dc.DrawImage(Image(eye.RawWidth,eye.RawHeight,eye.RawBgra),new Rect(x,65,width,height));
        }
        else
        {
            double w=width/eye.Settings.Columns,h=height/eye.Settings.Rows;
            for (int i=0;i<eye.Receptors.Length;i++)
            {
                var cell=new Rect(x+i%eye.Settings.Columns*w,65+i/eye.Settings.Columns*h,w-.5,h-.5);
                if (!validity[i])
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45,22,45)),null,cell);
                    dc.DrawLine(new Pen(Brushes.MediumVioletRed,.5),cell.TopLeft,cell.BottomRight); continue;
                }
                Color color;
                if (Mode==2)
                {
                    double value=eye.Contrast[i]; byte strength=(byte)(Math.Abs(value)*255);
                    color=value>=0?Color.FromRgb(0,strength,strength):Color.FromRgb(strength,(byte)(strength*.35),(byte)(strength*.3));
                }
                else if (Mode==3) { byte strength=(byte)(eye.Change[i]*220); color=Color.FromRgb(strength,(byte)(strength*.7),15); }
                else { byte value=(byte)(Math.Clamp(Mode==4?eye.Neurons[i]:eye.Receptors[i],0,1)*255); color=Color.FromRgb(value,value,value); }
                dc.DrawRectangle(new SolidColorBrush(color),null,cell);
                if (Mode==3)
                {
                    double dx=eye.MotionX[i]*w*.45,dy=eye.MotionY[i]*h*.45;
                    if (Math.Abs(dx)+Math.Abs(dy)>.2)
                    {
                        var from=new Point(cell.X+w*.5,cell.Y+h*.5); var to=new Point(from.X+dx,from.Y+dy);
                        dc.DrawLine(new Pen(Brushes.Cyan,1.5),from,to); dc.DrawEllipse(Brushes.White,null,to,1.2,1.2);
                    }
                }
            }
        }
        var s=eye.Signals;
        Text(dc,$"To brain: activity {s.Activity:F3} | light {s.Luminance:F3} | contrast {s.Contrast:F3} | change {s.Change:F3}",x,271,11);
        Text(dc,$"Motion CW {s.Clockwise:F3} / CCW {s.CounterClockwise:F3} | brain visual population {simulation.Brain.Activity[side]:F3}",x,288,11);
    }
    private void DrawMap(DrawingContext dc)
    {
        Text(dc,"Sampling map: cyan L / pink R; white heading",20,385,13);
        var frame=vision.SampledFrame;
        if (frame is null) { Text(dc,"Waiting for a valid frame",20,420); return; }
        const double size=300;
        var bounds=new Rect(20,416,size,size);
        dc.PushClip(new RectangleGeometry(bounds));
        dc.DrawImage(Image(frame.Width,frame.Height,frame.Bgra),bounds);
        Point Map(Vec p)=>new(bounds.X+(p.X-frame.X)*size/frame.Width,bounds.Y+(p.Y-frame.Y)*size/frame.Height);
        foreach (var eye in new[] { vision.Eyes.Left,vision.Eyes.Right })
        {
            var color=ReferenceEquals(eye,vision.Eyes.Left)?Brushes.Cyan:Brushes.HotPink;
            var origin=Map(eye.Pose.Position);
            dc.DrawEllipse(color,null,origin,3,3);
            dc.DrawLine(new Pen(color,1),origin,Map(eye.Project(0,0)));
            dc.DrawLine(new Pen(color,1),origin,Map(eye.Project(eye.Settings.Columns,0)));
            dc.DrawLine(new Pen(color,1.5),origin,Map(eye.Pose.Position+Vec.Direction(eye.Pose.Heading)*70));
            foreach (var position in eye.SamplePositions) dc.DrawEllipse(color,null,Map(position),.8,.8);
        }
        var body=simulation.Creature;
        dc.DrawLine(new Pen(Brushes.White,2),Map(body.Position),Map(body.Position+Vec.Direction(body.Heading)*45));
        dc.Pop();
    }
}
