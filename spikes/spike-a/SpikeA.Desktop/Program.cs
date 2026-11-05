using Avalonia;
using SpikeA.App;

namespace SpikeA.Desktop;

static class Program
{
    // Usage: SpikeA.Desktop [saves folder] [report file]
    // Reports every file in the folder at startup, and writes that report to the file if given.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0)
            Host.SavesFolder = args[0];
        if (args.Length > 1)
            Host.ReportPath = args[1];
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App.App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
