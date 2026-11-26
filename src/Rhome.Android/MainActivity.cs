using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Rhome.App;

namespace Rhome.Android;

[Activity(
    Label = "rhome",
    Theme = "@style/RhomeTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    readonly Dictionary<int, TaskCompletionSource<(Result, Intent?)>> _pending = [];
    int _nextRequest = 0x5200;

    AndroidPlatform? platform;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Host.Platform = platform = new AndroidPlatform(this);
        base.OnCreate(savedInstanceState);
    }

    // The bottom screen is rhome's only while rhome is in front (issue #49): in the background an emulator needs it.
    protected override void OnStart()
    {
        base.OnStart();
        platform?.SecondScreen.OnStart();
    }

    protected override void OnStop()
    {
        platform?.SecondScreen.OnStop();
        base.OnStop();
    }

    /// <summary>Starts another activity (the folder picker) and waits for its result.</summary>
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
