namespace FlyGuy.Core;

public sealed record EyeGeometry(double ForwardOffset, double SideOffset, double YawDegrees, double FieldOfViewDegrees);

public sealed record VisionSettings
{
    public int Columns { get; init; } = 24;
    public int Rows { get; init; } = 10;
    public int SamplesPerAxis { get; init; } = 4;
    public double NearDistance { get; init; } = 12;
    public double FarDistance { get; init; } = 240;
    public EyeGeometry Left { get; init; } = new(18, -6, -40, 140);
    public EyeGeometry Right { get; init; } = new(18, 6, 40, 140);
    public double ReceptorTimeConstant { get; init; } = .025;
    public double MotionDelaySeconds { get; init; } = .06;
    public double NeuralTimeConstant { get; init; } = .06;
    public double CaptureHz { get; init; } = 20;
    public double ReceptorHz { get; init; } = 30;
    public double ProcessingHz { get; init; } = 30;
    public double BrainHz { get; init; } = 120;
    public double RenderHz { get; init; } = 60;
    public void Validate()
    {
        if (Columns is < 4 or > 64 || Rows is < 2 or > 32 || SamplesPerAxis is < 1 or > 6)
            throw new ArgumentException("Use 4-64 columns, 2-32 rows, 1-6 samples per axis.");
        if (!double.IsFinite(NearDistance) || !double.IsFinite(FarDistance) || NearDistance < 0 || FarDistance < NearDistance + 10 || FarDistance > 500)
            throw new ArgumentException("Eye range must be finite, at least 10 pixels deep, and at most 500 pixels.");
        foreach (var eye in new[] { Left, Right })
            if (eye is null || !double.IsFinite(eye.YawDegrees) || !double.IsFinite(eye.ForwardOffset) || !double.IsFinite(eye.SideOffset)
                || Math.Abs(eye.ForwardOffset) > 40 || Math.Abs(eye.SideOffset) > 40 || !double.IsFinite(eye.FieldOfViewDegrees) || eye.FieldOfViewDegrees is < 20 or > 180)
                throw new ArgumentException("Invalid eye geometry (FOV 20-180 degrees; offsets within 40 pixels).");
        foreach (double tau in new[] { ReceptorTimeConstant, MotionDelaySeconds, NeuralTimeConstant })
            if (!double.IsFinite(tau) || tau is < .001 or > 2) throw new ArgumentException("Visual time constants must be 0.001-2 seconds.");
        foreach (double rate in new[] { CaptureHz, ReceptorHz, ProcessingHz, RenderHz })
            if (!double.IsFinite(rate) || rate is < 1 or > 120) throw new ArgumentException("Visual/render rates must be 1-120 Hz.");
        if (!double.IsFinite(BrainHz) || BrainHz is < 60 or > 240) throw new ArgumentException("Brain rate must be 60-240 Hz.");
    }
}

public readonly record struct EyePose(Vec Position, double Heading);
public readonly record struct EyeSignals(double Activity, double Luminance, double Contrast, double Change, double Clockwise, double CounterClockwise);
public readonly record struct VisualSignals(EyeSignals Left, EyeSignals Right, bool Available);

/// <summary>Immutable-by-ownership BGRA frame in physical desktop coordinates. Pixels never reach Brain.</summary>
public sealed class PixelFrame(int x, int y, int width, int height, byte[] bgra, Region[] validRegions, double timestamp)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = bgra.Length == checked(width * height * 4) ? bgra : throw new ArgumentException("Invalid pixel buffer");
    public double Timestamp { get; } = timestamp;
    public bool TrySample(Vec position, out double luminance, out int pixel)
    {
        int sx = (int)Math.Floor(position.X), sy = (int)Math.Floor(position.Y);
        if (sx < X || sy < Y || sx >= X + Width || sy >= Y + Height || !validRegions.Any(r => sx >= r.X && sy >= r.Y && sx < r.X + r.Width && sy < r.Y + r.Height))
        { luminance = 0; pixel = -1; return false; }
        pixel = ((sy - Y) * Width + sx - X) * 4;
        // Deliberately grayscale; these are display-luminance coefficients, not fly spectral sensitivities.
        luminance = (.0722 * Bgra[pixel] + .7152 * Bgra[pixel+1] + .2126 * Bgra[pixel+2]) / 255;
        return true;
    }
}

/// <summary>One independent retina and its local early visual processing.</summary>
public sealed class EyeVision
{
    public VisionSettings Settings { get; }
    public EyeGeometry Geometry { get; }
    public EyePose Pose { get; private set; }
    public double[] Receptors { get; }
    public double[] Contrast { get; }
    public double[] Change { get; }
    public double[] MotionX { get; }
    public double[] MotionY { get; }
    public double[] Neurons { get; }
    public bool[] Valid { get; }
    public bool[] ProcessedValid { get; }
    public Vec[] SamplePositions { get; }
    public byte[] RawBgra { get; }
    public int RawWidth => Settings.Columns * Settings.SamplesPerAxis;
    public int RawHeight => Settings.Rows * Settings.SamplesPerAxis;
    public EyeSignals Signals { get; private set; }
    private readonly double[] previous, delayedOn, delayedOff, on, off;
    private readonly bool[] initialized, processed;
    public EyeVision(VisionSettings settings, EyeGeometry geometry)
    {
        Settings = settings; Geometry = geometry;
        int size = settings.Columns * settings.Rows;
        Receptors = new double[size]; Contrast = new double[size]; Change = new double[size];
        MotionX = new double[size]; MotionY = new double[size]; Neurons = new double[size];
        Valid = new bool[size]; ProcessedValid = new bool[size]; SamplePositions = new Vec[size];
        previous = new double[size]; delayedOn = new double[size]; delayedOff = new double[size];
        on = new double[size]; off = new double[size]; initialized = new bool[size]; processed = new bool[size];
        RawBgra = new byte[RawWidth * RawHeight * 4];
    }
    public EyePose GetPose(Vec body, double heading) => new(body + Vec.Direction(heading) * Geometry.ForwardOffset + Vec.Direction(heading + Math.PI / 2) * Geometry.SideOffset,
        heading + Geometry.YawDegrees * Math.PI / 180);
    public Vec Project(double column, double row)
    {
        double angle = Pose.Heading + (column / Settings.Columns - .5) * Geometry.FieldOfViewDegrees * Math.PI / 180;
        // Top row is distant; bottom row is near. This is a flat desktop fan, not a 3-D camera.
        double distance = Settings.FarDistance - row / Settings.Rows * (Settings.FarDistance - Settings.NearDistance);
        return Pose.Position + Vec.Direction(angle) * distance;
    }
    public void Sample(PixelFrame frame, Vec position, double heading, double dt)
    {
        Pose = GetPose(position, heading);
        double alpha = 1 - Math.Exp(-dt / Settings.ReceptorTimeConstant);
        int sub = Settings.SamplesPerAxis;
        for (int row = 0; row < Settings.Rows; row++)
            for (int col = 0; col < Settings.Columns; col++)
            {
                int i = row * Settings.Columns + col, count = 0;
                double sum = 0;
                SamplePositions[i] = Project(col + .5, row + .5);
                for (int v = 0; v < sub; v++)
                    for (int u = 0; u < sub; u++)
                    {
                        int raw = ((row * sub + v) * RawWidth + col * sub + u) * 4;
                        if (frame.TrySample(Project(col + (u + .5) / sub, row + (v + .5) / sub), out double light, out int pixel))
                        {
                            sum += light; count++;
                            RawBgra[raw] = frame.Bgra[pixel]; RawBgra[raw+1] = frame.Bgra[pixel+1]; RawBgra[raw+2] = frame.Bgra[pixel+2]; RawBgra[raw+3] = 255;
                        }
                        else { RawBgra[raw] = 35; RawBgra[raw+1] = 15; RawBgra[raw+2] = 35; RawBgra[raw+3] = 255; }
                    }
                // Partial/offscreen cells are explicitly blind, not artificial black edges.
                Valid[i] = count == sub * sub;
                if (!Valid[i]) { Receptors[i] = 0; initialized[i] = false; processed[i] = false; continue; }
                double target = sum / count;
                if (!initialized[i]) processed[i] = false;
                Receptors[i] = initialized[i] ? Receptors[i] + (target - Receptors[i]) * alpha : target;
                initialized[i] = true;
            }
    }
    public void Process(double dt)
    {
        double delayAlpha = 1 - Math.Exp(-dt / Settings.MotionDelaySeconds);
        double neuralAlpha = 1 - Math.Exp(-dt / Settings.NeuralTimeConstant);
        for (int i = 0; i < Receptors.Length; i++)
        {
            if (!Valid[i])
            {
                Contrast[i] = Change[i] = MotionX[i] = MotionY[i] = Neurons[i] = 0;
                on[i] = off[i] = delayedOn[i] = delayedOff[i] = 0; processed[i] = false; continue;
            }
            int row = i / Settings.Columns, col = i % Settings.Columns;
            double surround = 0; int count = 0;
            for (int y = Math.Max(0,row-1); y <= Math.Min(Settings.Rows-1,row+1); y++)
                for (int x = Math.Max(0,col-1); x <= Math.Min(Settings.Columns-1,col+1); x++)
                {
                    int j = y * Settings.Columns + x;
                    if (Valid[j]) { surround += Receptors[j]; count++; }
                }
            double mean = surround / Math.Max(1,count);
            Contrast[i] = Math.Clamp((Receptors[i] - mean) / (.15 + mean), -1, 1);
            Change[i] = processed[i] ? Math.Clamp(Math.Abs(Receptors[i] - previous[i]) / Math.Max(.01,dt) * .15, 0, 1) : 0;
            previous[i] = Receptors[i];
            on[i] = Math.Max(0, Contrast[i]); off[i] = Math.Max(0, -Contrast[i]);
            if (!processed[i]) { delayedOn[i] = on[i]; delayedOff[i] = off[i]; }
        }
        double luminance = 0, contrast = 0, change = 0, activity = 0, clockwise = 0, counter = 0;
        for (int i = 0; i < Receptors.Length; i++)
        {
            if (!Valid[i]) continue;
            double Correlate(int neighbor) => neighbor >= 0 && neighbor < Valid.Length && Valid[neighbor] && processed[i] && processed[neighbor]
                ? 8 * (delayedOn[neighbor] * on[i] - on[neighbor] * delayedOn[i] + delayedOff[neighbor] * off[i] - off[neighbor] * delayedOff[i]) : 0;
            MotionX[i] = Math.Clamp(i % Settings.Columns > 0 ? Correlate(i-1) : 0, -1, 1);
            MotionY[i] = Math.Clamp(i >= Settings.Columns ? Correlate(i-Settings.Columns) : 0, -1, 1);
            double target = Math.Clamp(.35 * Math.Abs(Contrast[i]) + .8 * Change[i] + .5 * (Math.Abs(MotionX[i]) + Math.Abs(MotionY[i])), 0, 1);
            Neurons[i] += (target - Neurons[i]) * neuralAlpha;
            luminance += Receptors[i]; contrast += Math.Abs(Contrast[i]); change += Change[i] * Change[i]; activity += Neurons[i] * Neurons[i];
            clockwise += Math.Pow(Math.Max(0,MotionX[i]),2); counter += Math.Pow(Math.Max(0,-MotionX[i]),2);
        }
        // Update delays only after every correlator has read the previous state.
        for (int i = 0; i < Receptors.Length; i++)
        {
            if (!Valid[i]) continue;
            delayedOn[i] += (on[i] - delayedOn[i]) * delayAlpha;
            delayedOff[i] += (off[i] - delayedOff[i]) * delayAlpha;
            processed[i] = true;
        }
        int size = Receptors.Length;
        Array.Copy(Valid,ProcessedValid,size);
        double Pool(double squared) => Math.Clamp(3 * Math.Sqrt(squared / size), 0, 1);
        Signals = new(Pool(activity), luminance / size, contrast / size, Pool(change), Pool(clockwise), Pool(counter));
    }
    public void Clear()
    {
        foreach (var array in new[] { Receptors, Contrast, Change, MotionX, MotionY, Neurons, previous, delayedOn, delayedOff, on, off }) Array.Clear(array);
        Array.Clear(Valid); Array.Clear(ProcessedValid); Array.Clear(initialized); Array.Clear(processed); Array.Clear(RawBgra);
        Signals = default;
    }
}

public sealed class BinocularVision
{
    public VisionSettings Settings { get; }
    public EyeVision Left { get; }
    public EyeVision Right { get; }
    public bool Available { get; private set; }
    public VisualSignals Signals => new(Left.Signals, Right.Signals, Available);
    public BinocularVision(VisionSettings? settings = null)
    {
        Settings = settings ?? new(); Settings.Validate();
        Left = new(Settings, Settings.Left); Right = new(Settings, Settings.Right);
    }
    public void Sample(PixelFrame frame, Vec position, double heading, double dt)
    {
        Left.Sample(frame,position,heading,dt); Right.Sample(frame,position,heading,dt); Available = true;
    }
    public void Process(double dt) { Left.Process(dt); Right.Process(dt); }
    public void Clear() { Left.Clear(); Right.Clear(); Available = false; }
}
