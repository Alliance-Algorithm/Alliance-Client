using Alliance.Client.Features.Settings;
using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Telemetry;

public sealed class TelemetryStore
{
    public TelemetryStore(AppSettings settings)
    {
        CurrentSnapshot = new TelemetrySnapshot
        {
            MqttState = ConnectionState.NotConnected,
            VideoState = ConnectionState.NotConnected,
            LinkState = ConnectionState.Degraded,
            ModeLabel = "Framework Shell",
            BatteryPercent = 100,
            HeadingDegrees = 0,
            AltitudeMeters = 0,
            GroundSpeedMps = 0,
            LastUpdateText = $"Awaiting MQTT endpoint {settings.Mqtt.Host}:{settings.Mqtt.Port}",
            WarningText = "MQTT and UDP adapters are intentionally offline"
        };
    }

    public TelemetrySnapshot CurrentSnapshot { get; }
}
