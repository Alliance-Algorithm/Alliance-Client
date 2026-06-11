using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Telemetry;

public sealed record TelemetrySnapshot
{
    public ConnectionState MqttState { get; init; } = ConnectionState.NotConnected;

    public ConnectionState VideoState { get; init; } = ConnectionState.NotConnected;

    public ConnectionState LinkState { get; init; } = ConnectionState.Degraded;

    public string ModeLabel { get; init; } = "Scaffold";

    public int BatteryPercent { get; init; } = 100;

    public double HeadingDegrees { get; init; }

    public double AltitudeMeters { get; init; }

    public double GroundSpeedMps { get; init; }

    public string LastUpdateText { get; init; } = "No telemetry feed configured";

    public string WarningText { get; init; } = "External services disabled";
}
