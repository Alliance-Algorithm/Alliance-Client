using Alliance.Client.Shell;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Alliance.Client.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Alliance.Client;

public partial class App : Application
{
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Services is null)
            {
                throw new InvalidOperationException("Application services were not configured.");
            }

            var runtimeCoordinator = Services.GetRequiredService<AppRuntimeCoordinator>();
            runtimeCoordinator.Start();
            desktop.Exit += async (_, _) => await runtimeCoordinator.StopAsync();
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
