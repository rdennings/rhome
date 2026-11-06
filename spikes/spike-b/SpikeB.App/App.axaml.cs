using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SpikeB.App.Views;

namespace SpikeB.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new Window
                {
                    Title = "rhome spike B",
                    Width = 720,
                    Height = 900,
                    Content = new MainView(),
                };
                break;
            case IActivityApplicationLifetime activity:
                activity.MainViewFactory = () => new MainView();
                break;
            case ISingleViewApplicationLifetime single:
                single.MainView = new MainView();
                break;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
