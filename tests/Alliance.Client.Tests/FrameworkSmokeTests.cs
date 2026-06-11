using Alliance.Client.Features.Control;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Infrastructure.Bootstrap;
using Alliance.Client.Shell;
using Alliance.Client.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Alliance.Client.Tests;

public sealed class FrameworkSmokeTests
{
    [Fact]
    public void ServiceProvider_Resolves_Framework_ViewModels()
    {
        using var services = AppBootstrapper.BuildServiceProvider(GetAppProjectPath());

        Assert.NotNull(services.GetRequiredService<MainWindowViewModel>());
        Assert.NotNull(services.GetRequiredService<VideoViewModel>());
        Assert.NotNull(services.GetRequiredService<HudOverlayViewModel>());
        Assert.NotNull(services.GetRequiredService<TelemetryStore>());
    }

    [Fact]
    public void AppSettings_Bind_From_Json_File()
    {
        using var services = AppBootstrapper.BuildServiceProvider(GetAppProjectPath());
        var settings = services.GetRequiredService<AppSettings>();

        Assert.Equal("Alliance Client", settings.ApplicationName);
        Assert.True(settings.EnableDebugMode);
        Assert.Equal("192.168.12.1", settings.Mqtt.Host);
        Assert.Equal(3333, settings.Mqtt.Port);
        Assert.Equal(3334, settings.UdpVideo.ListenPort);
        Assert.Equal("hevc", settings.UdpVideo.Codec);
    }

    [Fact]
    public async Task Placeholder_Services_Expose_Disconnected_State()
    {
        using var services = AppBootstrapper.BuildServiceProvider(GetAppProjectPath());

        var telemetryService = services.GetRequiredService<ITelemetryService>();
        var videoService = services.GetRequiredService<IVideoStreamService>();
        var commandService = services.GetRequiredService<ICommandService>();

        Assert.Equal("Not Connected", telemetryService.GetSnapshot().MqttState.ToDisplayText());
        Assert.Equal("No Stream", videoService.StatusText);

        await commandService.SendAsync("noop");
    }

    private static string GetAppProjectPath()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/Alliance.Client"));
    }
}
