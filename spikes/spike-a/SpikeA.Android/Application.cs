using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using SpikeA.App;

namespace SpikeA.Android;

[global::Android.App.Application]
public class Application : AvaloniaAndroidApplication<App.App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // The app's own external folder (/sdcard/Android/data/<id>/files) needs no permission,
        // and adb can drop test saves into files/saves/ for an automated read.
        var files = GetExternalFilesDir(null)?.AbsolutePath;
        if (files is not null)
        {
            Host.SavesFolder = Path.Combine(files, "saves");
            Host.ReportPath = Path.Combine(files, "report.txt");
        }
        Host.MsSinceProcessStart = () =>
            global::Android.OS.SystemClock.ElapsedRealtime() - global::Android.OS.Process.StartElapsedRealtime;
        string release = global::Android.OS.Build.VERSION.Release ?? "?", model = global::Android.OS.Build.Model ?? "?";
        Host.Platform = $"Android {release} ({model})";
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
