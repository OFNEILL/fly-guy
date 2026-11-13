using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace FlyGuy.Desktop;

internal sealed class AppUpdates : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly CancellationTokenSource stopping = new();
    private bool checking;
    public event Action<string>? Changed;

    public AppUpdates() => timer.Tick += async (_,_) => await CheckAsync();

    public void Start()
    {
        timer.Start();
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (checking || stopping.IsCancellationRequested) return;
        checking = true;
        try
        {
            var manager = new UpdateManager(new GithubSource("https://github.com/OFNEILL/fly-guy", null, false));
            if (!manager.IsInstalled)
            {
                timer.Stop();
                Changed?.Invoke("Install using Fly Guy Setup to enable automatic updates.");
                return;
            }
            if (manager.UpdatePendingRestart is not null)
            { Changed?.Invoke("Update ready. Close and reopen Fly Guy to install it."); return; }
            var update = await manager.CheckForUpdatesAsync();
            if (stopping.IsCancellationRequested) return;
            if (update is null) { Changed?.Invoke("Fly Guy is up to date."); return; }
            Changed?.Invoke("Downloading a Fly Guy update...");
            await manager.DownloadUpdatesAsync(update, cancelToken: stopping.Token);
            if (!stopping.IsCancellationRequested)
                Changed?.Invoke("Update ready. Close and reopen Fly Guy to install it.");
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // An unavailable feed, offline machine or failed download must never stop the creature.
            System.Diagnostics.Debug.WriteLine($"Fly Guy update check: {ex}");
            if (!stopping.IsCancellationRequested)
                Changed?.Invoke("Could not check for updates. Fly Guy will try again later.");
        }
        finally { checking = false; }
    }

    public void Dispose() { timer.Stop(); stopping.Cancel(); }
}
