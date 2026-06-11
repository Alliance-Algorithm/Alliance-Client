using Alliance.Client.Features.Hud;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        VideoViewModel video,
        HudOverlayViewModel hud,
        TelemetryStore telemetryStore,
        IVideoStreamService videoStreamService,
        AppSettings settings)
    {
        Video = video;
        Hud = hud;

        var snapshot = telemetryStore.CurrentSnapshot;

        WindowTitle = settings.ApplicationName;
        Subtitle = "Avalonia scaffold with dependency injection, configuration, and placeholder feature wiring only.";
        StartupMode = snapshot.ModeLabel;
        MqttStatus = snapshot.MqttState.ToDisplayText();
        VideoStatus = videoStreamService.State.ToDisplayText();
        LinkStatus = snapshot.LinkState.ToDisplayText();
        DebugModeLabel = settings.EnableDebugMode ? "Enabled" : "Disabled";
        ScenarioSummary = $"MQTT {settings.Mqtt.Host}:{settings.Mqtt.Port} reserved | UDP {settings.UdpVideo.ListenPort} / {settings.UdpVideo.Codec.ToUpperInvariant()} reserved";
        StatusFootnote = "Static placeholder state only";
        FooterSummary = "Compose real telemetry, video, and control adapters behind the existing contracts when the next phase starts.";
    }

    public string WindowTitle { get; }

    public string Subtitle { get; }

    public string StartupMode { get; }

    public string MqttStatus { get; }

    public string VideoStatus { get; }

    public string LinkStatus { get; }

    public string DebugModeLabel { get; }

    public string ScenarioSummary { get; }

    public string StatusFootnote { get; }

    public string FooterSummary { get; }

    public string MqttStatusLabel => $"MQTT: {MqttStatus}";

    public string VideoStatusLabel => $"Video: {VideoStatus}";

    public string LinkStatusLabel => $"Link: {LinkStatus}";

    public VideoViewModel Video { get; }

    public HudOverlayViewModel Hud { get; }
}
