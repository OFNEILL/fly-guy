using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FlyGuy.Core;
using Region = FlyGuy.Core.Region;

namespace FlyGuy.Desktop;

internal sealed record CaptureRequest(Vec Position, Region[] Monitors, VisionSettings Settings, bool Enabled);

internal static class CaptureExclusion
{
    private static readonly Dictionary<nint,string> failures = [];
    private static readonly object gate = new();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    public static string? Error { get { lock (gate) return failures.Values.FirstOrDefault(); } }
    public static void Register(Window window)
    {
        nint handle = 0;
        window.SourceInitialized += (_,_) =>
        {
            handle = new WindowInteropHelper(window).Handle;
            if (!SetWindowDisplayAffinity(handle, 0x11))
                lock (gate) failures[handle] = "Cannot exclude debugger/self from capture: " + new Win32Exception(Marshal.GetLastWin32Error()).Message;
        };
        window.Closed += (_,_) => { lock (gate) failures.Remove(handle); };
    }
}

internal sealed class WorkMeter
{
    private readonly object gate = new();
    private long count, previousCount;
    private double totalSeconds, previousSeconds, previousTime, rate, milliseconds;
    public long Count { get { lock(gate) return count; } }
    public void Record(double seconds) { lock(gate) { count++; totalSeconds += seconds; } }
    public string Read(double now, double target)
    {
        lock(gate)
        {
            if (now - previousTime >= 1)
            {
                rate = (count - previousCount) / (now - previousTime);
                milliseconds = count == previousCount ? 0 : 1000 * (totalSeconds - previousSeconds) / (count - previousCount);
                previousCount = count; previousSeconds = totalSeconds; previousTime = now;
            }
            return $"{rate:F1}/{target:G} Hz, {milliseconds:F2} ms/update";
        }
    }
}

/// <summary>Single bounded background capture worker. No files, OCR, window titles or object metadata.</summary>
internal sealed class DesktopCapture : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public nint Handle; public Native.CursorPoint Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [DllImport("user32.dll", SetLastError=true)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll", SetLastError=true)] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool BitBlt(nint target,int x,int y,int width,int height,nint source,int sourceX,int sourceY,uint operation);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch clock;
    private CaptureRequest? request;
    private PixelFrame? latest;
    private string status = "Waiting for desktop capture";
    private readonly Task worker;
    public WorkMeter Meter { get; } = new();
    public PixelFrame? Latest => Volatile.Read(ref latest);
    public string Status => Volatile.Read(ref status);
    public DesktopCapture(Stopwatch clock)
    {
        this.clock = clock;
        worker = Task.Run(CaptureLoop);
    }
    public void Request(CaptureRequest value) => Volatile.Write(ref request,value);
    private async Task CaptureLoop()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var current = Volatile.Read(ref request);
                double began = clock.Elapsed.TotalSeconds;
                double period = 1 / (current?.Settings.CaptureHz ?? 20);
                if (current is { Enabled: true })
                {
                    try
                    {
                        if (CaptureExclusion.Error is { } error) throw new InvalidOperationException(error);
                        var frame = Capture(current, began);
                        Volatile.Write(ref latest,frame);
                        Volatile.Write(ref status,"Live desktop pixels (memory only)");
                        Meter.Record(clock.Elapsed.TotalSeconds - began);
                    }
                    catch (Exception ex) when (ex is Win32Exception or ExternalException or InvalidOperationException or ArgumentException)
                    {
                        Volatile.Write(ref latest,null);
                        Volatile.Write(ref status,"VISION UNAVAILABLE: " + ex.Message);
                    }
                }
                else { Volatile.Write(ref latest,null); Volatile.Write(ref status,"Vision paused"); }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(.001,period - (clock.Elapsed.TotalSeconds-began))),cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally { Volatile.Write(ref latest,null); cancellation.Dispose(); }
    }
    private static PixelFrame Capture(CaptureRequest request, double timestamp)
    {
        int radius = (int)Math.Ceiling(request.Settings.FarDistance + 64);
        int x = (int)Math.Floor(request.Position.X) - radius, y = (int)Math.Floor(request.Position.Y) - radius, size = radius * 2;
        using var bitmap = new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Black);
            var bounds = new Rectangle(x,y,size,size);
            nint screen = GetDC(0);
            if (screen == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                nint target = graphics.GetHdc();
                try
                {
                    foreach (var region in request.Monitors)
                    {
                        var part = Rectangle.Intersect(bounds,new((int)region.X,(int)region.Y,(int)region.Width,(int)region.Height));
                        // SRCCOPY | CAPTUREBLT includes layered sugar/spray windows. The managed enum rejects combined flags.
                        if (part.Width > 0 && part.Height > 0 && !BitBlt(target,part.X-x,part.Y-y,part.Width,part.Height,screen,part.X,part.Y,0x40CC0020))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally { graphics.ReleaseHdc(target); }
            }
            finally { ReleaseDC(0,screen); }
            // GDI screen copies omit the hardware cursor. Composite its actual icon, not a semantic location signal.
            var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
            if (GetCursorInfo(ref cursor) && (cursor.Flags & 1) != 0)
            {
                nint icon = CopyIcon(cursor.Handle);
                if (icon != 0)
                {
                    try
                    {
                        if (GetIconInfo(icon,out var info))
                        {
                            try
                            {
                                nint dc = graphics.GetHdc();
                                try
                                {
                                    if (!DrawIconEx(dc,cursor.Position.X-(int)info.HotspotX-x,cursor.Position.Y-(int)info.HotspotY-y,icon,0,0,0,0,3))
                                        throw new Win32Exception(Marshal.GetLastWin32Error());
                                }
                                finally { graphics.ReleaseHdc(dc); }
                            }
                            finally { if (info.Mask != 0) DeleteObject(info.Mask); if (info.Color != 0) DeleteObject(info.Color); }
                        }
                    }
                    finally { DestroyIcon(icon); }
                }
            }
        }
        byte[] pixels = new byte[size*size*4];
        var data = bitmap.LockBits(new(0,0,size,size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            for (int row = 0; row < size; row++) Marshal.Copy(data.Scan0 + row*data.Stride,pixels,row*size*4,size*4);
        }
        finally { bitmap.UnlockBits(data); }
        // GDI does not promise alpha bytes; desktop pixels are opaque in the diagnostic source map.
        for (int i=3;i<pixels.Length;i+=4) pixels[i]=255;
        return new(x,y,size,size,pixels,request.Monitors,timestamp);
    }
    public void Dispose()
    {
        if (!worker.IsCompleted) cancellation.Cancel();
    }
}

internal sealed class VisionRuntime : IDisposable
{
    private readonly DesktopCapture capture;
    private double sampled, processed, nextSample, nextProcess;
    public BinocularVision Eyes { get; private set; } = new();
    public PixelFrame? SampledFrame { get; private set; }
    public WorkMeter ReceptorMeter { get; } = new();
    public WorkMeter ProcessingMeter { get; } = new();
    public WorkMeter BrainMeter { get; } = new();
    public WorkMeter RenderMeter { get; } = new();
    public WorkMeter EyePaintMeter { get; } = new();
    public WorkMeter BrainPaintMeter { get; } = new();
    public WorkMeter CaptureMeter => capture.Meter;
    public string Status => capture.Status;
    public double SampledAt => sampled;
    public double ProcessedAt => processed;
    public VisionRuntime(Stopwatch clock) => capture = new(clock);
    public void Configure(VisionSettings settings) { Eyes = new(settings); SampledFrame = null; sampled = processed = nextSample = nextProcess = 0; }
    public void Advance(double now, Simulation simulation, bool paused)
    {
        capture.Request(new(simulation.Creature.Position,simulation.Habitat.Regions,Eyes.Settings,!paused));
        if (paused) return;
        var frame = capture.Latest;
        if (frame is null || now - frame.Timestamp > Math.Max(.5,2 / Eyes.Settings.CaptureHz))
        {
            if (Eyes.Available) Eyes.Clear();
            SampledFrame = null; return;
        }
        if (now >= nextSample)
        {
            long began = Stopwatch.GetTimestamp();
            Eyes.Sample(frame,simulation.Creature.Position,simulation.Creature.Heading,sampled == 0 ? 1/Eyes.Settings.ReceptorHz : Math.Min(.25,now-sampled));
            SampledFrame = frame; sampled = now;
            double period=1/Eyes.Settings.ReceptorHz;
            nextSample = now-nextSample>=period ? now+period : nextSample+period;
            ReceptorMeter.Record(Stopwatch.GetElapsedTime(began).TotalSeconds);
        }
        if (now >= nextProcess)
        {
            long began = Stopwatch.GetTimestamp();
            Eyes.Process(processed == 0 ? 1/Eyes.Settings.ProcessingHz : Math.Min(.25,now-processed));
            processed = now;
            double period=1/Eyes.Settings.ProcessingHz;
            nextProcess = now-nextProcess>=period ? now+period : nextProcess+period;
            ProcessingMeter.Record(Stopwatch.GetElapsedTime(began).TotalSeconds);
        }
    }
    public string Rates(double now) => $"Capture {CaptureMeter.Read(now,Eyes.Settings.CaptureHz)} | receptors {ReceptorMeter.Read(now,Eyes.Settings.ReceptorHz)} | processing {ProcessingMeter.Read(now,Eyes.Settings.ProcessingHz)}\nBrain {BrainMeter.Read(now,Eyes.Settings.BrainHz)} | render callbacks {RenderMeter.Read(now,Eyes.Settings.RenderHz)}\nEye paint {EyePaintMeter.Read(now,Math.Min(15,Eyes.Settings.RenderHz))} | brain graph paint {BrainPaintMeter.Read(now,10)} (elapsed work times; excludes deferred GPU work)";
    public void Dispose() => capture.Dispose();
}
