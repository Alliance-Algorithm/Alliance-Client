using Alliance.Client.Features.Telemetry;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Hud;

public sealed class HudOverlayViewModel : ObservableObject
{
    public HudOverlayViewModel(TelemetryStore telemetryStore)
    {
        var snapshot = telemetryStore.CurrentSnapshot;

        ModeLabel = snapshot.ModeLabel;
        LinkStatusText = $"LINK {snapshot.LinkState.ToDisplayText().ToUpperInvariant()}";
        HeadingText = TelemetryText.FormatDegrees(snapshot.HeadingDegrees);
        AltitudeText = $"ALT {TelemetryText.FormatMeters(snapshot.AltitudeMeters)}";
        BatteryText = $"{snapshot.BatteryPercent}%";
        SpeedText = $"SPD {TelemetryText.FormatMetersPerSecond(snapshot.GroundSpeedMps)}";
        WarningText = snapshot.WarningText;
        LastUpdateText = snapshot.LastUpdateText;
    }

    public string ModeLabel { get; }

    public string LinkStatusText { get; }

    public string HeadingText { get; }

    public string AltitudeText { get; }

    public string BatteryText { get; }

    public string SpeedText { get; }

    public string WarningText { get; }

    public string LastUpdateText { get; }
}
