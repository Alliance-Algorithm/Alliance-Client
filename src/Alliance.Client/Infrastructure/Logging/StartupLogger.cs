using Alliance.Client.Features.Settings;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Infrastructure.Logging;

public sealed class StartupLogger
{
    private readonly ILogger<StartupLogger> _logger;

    public StartupLogger(ILogger<StartupLogger> logger)
    {
        _logger = logger;
    }

    public void LogFrameworkReady(AppSettings settings)
    {
        _logger.LogInformation(
            "Starting {ApplicationName}. Debug mode: {DebugMode}. MQTT {MqttHost}:{MqttPort}, client {ClientId}, UDP {UdpPort}.",
            settings.ApplicationName,
            settings.EnableDebugMode,
            settings.Mqtt.Host,
            settings.Mqtt.Port,
            settings.Mqtt.ClientId,
            settings.UdpVideo.ListenPort);
    }
}
