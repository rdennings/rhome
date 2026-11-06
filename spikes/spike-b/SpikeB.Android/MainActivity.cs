using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using SpikeB.App;

namespace SpikeB.Android;

[Activity(
    Label = "rhome spike B",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    readonly Dictionary<int, TaskCompletionSource<(Result, Intent?)>> _pending = [];
    int _nextRequest = 0x5B00;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var files = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath;
        Host.DataDir = files;
        Host.Folders = new SafFolderAccess(this, files);
        string release = Build.VERSION.Release ?? "?", model = Build.Model ?? "?";
        Host.Platform = $"Android {release} ({model}), security patch {Build.VERSION.SecurityPatch}";
        base.OnCreate(savedInstanceState);
    }

    public Task<(Result, Intent?)> StartForResultAsync(Intent intent)
    {
        var tcs = new TaskCompletionSource<(Result, Intent?)>();
        int code = _nextRequest++;
        _pending[code] = tcs;
        StartActivityForResult(intent, code);
        return tcs.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (_pending.Remove(requestCode, out var tcs))
            tcs.SetResult((resultCode, data));
    }
}
