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
        Assert.NotNull(services.GetRequiredService<HudOverlayViewModel>());
        Assert.NotNull(services.GetRequiredService<TelemetryStore>());
        Assert.NotNull(services.GetRequiredService<VideoStreamStore>());
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
        Assert.Equal("101", settings.Mqtt.ClientId);
        Assert.True(settings.Video.Enabled);
        Assert.Equal(3334, settings.Video.UdpPort);
        Assert.Equal(1.2, settings.Hud.RobotTextScale);
        Assert.Equal(0.8, settings.Hud.RobotWidthScale);
        Assert.Equal(0.8, settings.Hud.MatchInfoPanelBackgroundOpacity);
    }

    [Fact]
    public void HudLayoutSettings_Uses_Display_Defaults_And_Clamps_To_50_Percent()
    {
        var settings = new AppSettings();
        var layoutSettings = new HudLayoutSettings(settings);

        Assert.Equal(1.2, layoutSettings.RobotTextScale);
        Assert.Equal(0.8, layoutSettings.RobotWidthScale);

        for (var index = 0; index < 20; index++)
        {
            layoutSettings.DecreaseRobotText();
            layoutSettings.DecreaseRobotWidth();
        }

        Assert.Equal(0.5, layoutSettings.RobotTextScale);
        Assert.Equal(0.5, layoutSettings.RobotWidthScale);
        Assert.Equal(0.5, settings.Hud.RobotTextScale);
        Assert.Equal(0.5, settings.Hud.RobotWidthScale);
    }

    [Fact]
    public async Task Runtime_Services_Expose_Initial_Disconnected_State()
    {
        using var services = AppBootstrapper.BuildServiceProvider(GetAppProjectPath());

        var telemetryStore = services.GetRequiredService<TelemetryStore>();
        var commandService = services.GetRequiredService<ICommandService>();

        Assert.Equal("Not Connected", telemetryStore.CurrentSnapshot.MqttState.ToDisplayText());

        await commandService.SendAsync("noop");
    }

    private static string GetAppProjectPath()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/Alliance.Client"));
    }
}
