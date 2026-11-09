using System.Runtime.InteropServices;
using FlyGuy.Core;

namespace FlyGuy.Mac;

// Quartz coordinates are desktop points (including on Retina displays). The entire
// Mac simulation and Avalonia's macOS window positions use this same space.
internal static class Native
{
    private const string Library = "FlyGuyCapture";
    [DllImport(Library)] private static extern int fg_display(int index, out double x, out double y, out double width, out double height);
    [DllImport(Library)] private static extern void fg_cursor(out double x, out double y);
    [DllImport(Library)] internal static extern int fg_permission(int request);
    [DllImport(Library)] internal static extern int fg_capture(int x, int y, int size, [Out] byte[] pixels);

    public static Vec Cursor { get { fg_cursor(out var x, out var y); return new(x, y); } }
    public static Habitat ReadHabitat()
    {
        var regions = new List<Region>();
        for (int i = 0; fg_display(i, out var x, out var y, out var w, out var h) != 0; i++)
            regions.Add(new(x, y, w, h));
        return new(regions.ToArray());
    }
}

internal sealed class DesktopCapture : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly object gate = new();
    private Vec position;
    private Region[] regions = [];
    private bool enabled;
    private PixelFrame? latest;
    private string status = "Enable desktop vision to request Screen Recording permission.";
    public PixelFrame? Latest => Volatile.Read(ref latest);
    public string Status => Volatile.Read(ref status);

    public DesktopCapture() => _ = Task.Run(Run);
    public void Request(Vec center, Region[] habitat, bool active)
    {
        lock (gate) { position = center; regions = habitat; enabled = active; }
    }
    private async Task Run()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                Vec center; Region[] habitat; bool active;
                lock (gate) { center = position; habitat = regions; active = enabled; }
                if (active)
                {
                    const int size = 600;
                    int x = (int)Math.Floor(center.X) - size / 2, y = (int)Math.Floor(center.Y) - size / 2;
                    var pixels = new byte[size * size * 4];
                    int result = Native.fg_capture(x, y, size, pixels);
                    if (result == 1)
                    {
                        Volatile.Write(ref latest, new PixelFrame(x, y, size, size, pixels, habitat,
                            System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency));
                        Volatile.Write(ref status, "Desktop vision active (screen pixels stay in memory).");
                    }
                    else
                    {
                        Volatile.Write(ref latest, null);
                        Volatile.Write(ref status, result == -2
                            ? "Allow Fly Guy in System Settings > Privacy & Security > Screen Recording, then restart."
                            : "Desktop capture unavailable; visual input is disabled. Try enabling vision again.");
                    }
                }
                else Volatile.Write(ref latest, null);
                await Task.Delay(active ? 50 : 200, cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Volatile.Write(ref latest, null); Volatile.Write(ref status, $"Capture stopped: {ex.Message}"); }
    }
    public void Dispose() => cancellation.Cancel();
}
