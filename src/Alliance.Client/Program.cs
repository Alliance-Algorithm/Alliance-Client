using Alliance.Client.Infrastructure.Bootstrap;
using Avalonia;

namespace Alliance.Client;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[FATAL] Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        App.Services = AppBootstrapper.BuildServiceProvider(AppContext.BaseDirectory);
        AppBootstrapper.LogStartup(App.Services);

        Console.WriteLine("[STARTUP] Application services initialized, starting Avalonia...");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
