using Alliance.Client.Features.Control;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Infrastructure.Logging;
using Alliance.Client.Infrastructure.Runtime;
using Alliance.Client.Shell;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Infrastructure.Bootstrap;

public static class AppBootstrapper
{
    public static ServiceProvider BuildServiceProvider(string contentRootPath)
    {
        var configuration = BuildConfiguration(contentRootPath);
        var settings = BindSettings(configuration);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(settings);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<TelemetryStore>();
        services.AddSingleton<ITelemetryService, MqttTelemetryService>();
        services.AddSingleton<ICommandService, NoOpCommandService>();
        services.AddSingleton<StartupLogger>();
        services.AddSingleton<AppRuntimeCoordinator>();

        services.AddSingleton<HudOverlayViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MessageViewerWindowViewModel>();

        return services.BuildServiceProvider();
    }

    public static IConfiguration BuildConfiguration(string contentRootPath)
    {
        return new ConfigurationBuilder()
            .SetBasePath(contentRootPath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
    }

    public static AppSettings BindSettings(IConfiguration configuration)
    {
        var settings = new AppSettings();
        configuration.Bind(settings);
        return settings;
    }

    public static void LogStartup(IServiceProvider services)
    {
        services.GetRequiredService<StartupLogger>().LogFrameworkReady(
            services.GetRequiredService<AppSettings>());
    }
}
