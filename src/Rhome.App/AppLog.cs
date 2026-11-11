using System.Globalization;

namespace Rhome.App;

/// <summary>
/// The app's log: shown on screen while rhome is in development, and appended to <c>log.txt</c> in app storage so adb
/// can pull it from a device.
/// </summary>
public static class AppLog
{
    static readonly Lock Gate = new();
    static readonly List<string> recent = [];

    /// <summary>The newest lines, newest first, so a screen opened later can show what came before.</summary>
    public static IReadOnlyList<string> Recent
    {
        get
        {
            lock (Gate)
                return [.. recent];
        }
    }

    /// <summary>A stamped line was written. May be raised off the UI thread.</summary>
    public static event Action<string>? Added;

    public static void Write(string line)
    {
        var stamped = $"{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} {line}";
        lock (Gate)
        {
            recent.Insert(0, stamped);
            if (recent.Count > 200)
                recent.RemoveAt(recent.Count - 1);
        }
        if (Host.Platform is { } platform)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(platform.Storage.Root);
                    File.AppendAllText(Path.Combine(platform.Storage.Root, "log.txt"), stamped + "\n");
                }
            }
            catch (IOException)
            {
                // The log on screen is enough.
            }
        }
        Added?.Invoke(stamped);
    }
}
