using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FlyGuy.Desktop;

internal enum DesktopAction { OpenNotepad, MoveCursor }

internal sealed class DesktopActions
{
    public bool Enabled { get; set; }
    private DateTime nextAction;
    private DateTime nextAutonomous;
    private bool openedNotepad;
    private bool enabled;
    public bool AutonomousEnabled
    {
        get => enabled;
        set { if (value && !enabled) nextAutonomous = now().AddSeconds(30); enabled = value; }
    }
    private readonly Func<DesktopAction, string> execute;
    private readonly Func<DateTime> now;

    public DesktopActions(Func<DesktopAction, string>? execute = null, Func<DateTime>? now = null)
    {
        this.execute = execute ?? Execute;
        this.now = now ?? (() => DateTime.UtcNow);
        AutonomousEnabled = true;
    }

    public static DesktopAction? Parse(string text) => text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant() switch
    {
        "open notepad" => DesktopAction.OpenNotepad,
        "move cursor away" or "drag cursor away" or "move the cursor away" => DesktopAction.MoveCursor,
        _ => null
    };

    public string? TickAutonomous(bool active, bool held, double curiosity, double threat, bool cursorNearby)
    {
        if (!AutonomousEnabled || !active || held || now() < nextAutonomous || now() < nextAction) return null;
        DesktopAction? action = cursorNearby && threat > .35 ? DesktopAction.MoveCursor
            : !openedNotepad && curiosity > .35 && threat < .2 ? DesktopAction.OpenNotepad : null;
        if (action is null) return null;
        nextAutonomous = now().AddSeconds(60);
        // One launch attempt per session, even if Notepad is unavailable.
        if (action == DesktopAction.OpenNotepad) openedNotepad = true;
        nextAction = now().AddSeconds(5);
        try { return "Fly acted on its own: " + execute(action.Value); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { return "Autonomous action failed: " + ex.Message; }
    }

    public bool TryRun(string text, bool typed, out string status)
    {
        status = "";
        if (Parse(text) is not { } action) return false;
        if (!typed) { status = "Use the typed command or action buttons for desktop actions."; return true; }
        if (!Enabled) { status = "Enable desktop actions in this panel first."; return true; }
        if (now() < nextAction) { status = "Wait five seconds between desktop actions."; return true; }
        nextAction = now().AddSeconds(5);
        try { status = execute(action); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { status = "Desktop action failed: " + ex.Message; }
        return true;
    }

    private static string Execute(DesktopAction action)
    {
        if (action == DesktopAction.OpenNotepad)
        {
            using var process = Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe")) { UseShellExecute = false });
            return process is null ? "Notepad could not be started." : "Opened Notepad.";
        }
        // Never turn an existing mouse press into a drag of the user's content.
        if (new[] { 1, 2, 4, 5, 6 }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
            return "Release all mouse buttons before moving the cursor.";
        if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var bounds = System.Windows.Forms.Screen.FromPoint(point).Bounds;
        int x = point.X < bounds.Left + bounds.Width / 2 ? Math.Min(bounds.Right - 1, point.X + 160) : Math.Max(bounds.Left, point.X - 160);
        if (!SetCursorPos(x, point.Y)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return "Moved the cursor 160 pixels toward the centre of its screen.";
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out System.Drawing.Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
}
