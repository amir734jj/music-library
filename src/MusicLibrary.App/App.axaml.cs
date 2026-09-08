using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.MaterialDesign;

namespace MusicLibrary.App;

public class App : Application
{
    public static bool IsBrowserHost { get; set; }

    public override void Initialize()
    {
        IconProvider.Current.Register<MaterialDesignIconProvider>();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView(IsBrowserHost);
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}