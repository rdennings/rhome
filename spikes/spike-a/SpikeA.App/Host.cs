namespace SpikeA.App;

/// <summary>What each platform head hands to the shared UI.</summary>
public static class Host
{
    /// <summary>Milliseconds since the OS started this process.</summary>
    public static Func<double> MsSinceProcessStart { get; set; } =
        () => (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;

    /// <summary>Folder whose files are reported automatically at startup (may be null).</summary>
    public static string? SavesFolder { get; set; }

    /// <summary>Where the startup report is written, so it can be pulled off the device (may be null).</summary>
    public static string? ReportPath { get; set; }

    public static string Platform { get; set; } = Environment.OSVersion.ToString();
}
