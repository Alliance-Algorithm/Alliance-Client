using Alliance.Client.Infrastructure.Bootstrap;
using Avalonia;

namespace Alliance.Client;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.Services = AppBootstrapper.BuildServiceProvider(AppContext.BaseDirectory);
        AppBootstrapper.LogStartup(App.Services);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
