using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace FlyGuy.Desktop;

/// <summary>Local Windows speech synthesis. All calls belong to the WPF UI thread.</summary>
internal sealed class FlyVoice : IDisposable
{
    // Asynchronous, replace queued speech, and treat the word as literal text.
    private const int SpeakFlags = 1 | 2 | 16;
    private object? voice;
    private bool disposed;
    public bool IsSpeaking
    {
        get
        {
            if (voice is null || disposed) return false;
            try { return (int)((dynamic)voice).Status.RunningState == 2; }
            catch (Exception ex) when (ex is COMException or RuntimeBinderException) { return false; }
        }
    }

    public bool Speak(string word)
    {
        if (disposed || string.IsNullOrWhiteSpace(word)) return false;
        try
        {
            if (voice is null)
            {
                var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type is null) return false;
                voice = Activator.CreateInstance(type);
                if (voice is null) return false;
                ((dynamic)voice).Rate = -2;
                ((dynamic)voice).Volume = 80;
            }
            ((dynamic)voice).Speak(word, SpeakFlags);
            return true;
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (voice is null) return;
        try { ((dynamic)voice).Speak("", SpeakFlags); }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        if (voice is not null && Marshal.IsComObject(voice)) Marshal.FinalReleaseComObject(voice);
        voice = null;
        disposed = true;
    }
}
