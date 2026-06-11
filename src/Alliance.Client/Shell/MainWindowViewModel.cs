using System.ComponentModel;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly IVideoStreamService _videoStreamService;
    private string _currentRobotLabel;
    private string _mqttStatus;
    private string _videoStatus;
    private string _linkStatus;
    private string _statusFootnote;
    private string _footerSummary;

    public MainWindowViewModel(
        VideoViewModel video,
        HudOverlayViewModel hud,
        TelemetryStore telemetryStore,
        IVideoStreamService videoStreamService,
        AppSettings settings)
    {
        Video = video;
        Hud = hud;
        _telemetryStore = telemetryStore;
        _videoStreamService = videoStreamService;

        WindowTitle = settings.ApplicationName;
        Subtitle = "Real MQTT telemetry and UDP HEVC video feed for live robot operation.";
        DebugModeLabel = settings.EnableDebugMode ? "Enabled" : "Disabled";
        ScenarioSummary =
            $"MQTT {settings.Mqtt.Host}:{settings.Mqtt.Port} | Client {settings.Mqtt.ClientId} | UDP {settings.UdpVideo.ListenPort} / {settings.UdpVideo.Codec.ToUpperInvariant()}";

        var snapshot = telemetryStore.CurrentSnapshot;
        _currentRobotLabel = snapshot.CurrentRobot.RobotLabel;
        _mqttStatus = snapshot.MqttState.ToDisplayText();
        _videoStatus = videoStreamService.State.ToDisplayText();
        _linkStatus = snapshot.LinkState.ToDisplayText();
        _statusFootnote = snapshot.LastUpdateText;
        _footerSummary = snapshot.WarningText;

        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
        _videoStreamService.PropertyChanged += HandleVideoChanged;
    }

    public string WindowTitle { get; }

    public string Subtitle { get; }

    public string CurrentRobotLabel
    {
        get => _currentRobotLabel;
        private set => SetProperty(ref _currentRobotLabel, value);
    }

    public string MqttStatus
    {
        get => _mqttStatus;
        private set
        {
            if (SetProperty(ref _mqttStatus, value))
            {
                OnPropertyChanged(nameof(MqttStatusLabel));
            }
        }
    }

    public string VideoStatus
    {
        get => _videoStatus;
        private set
        {
            if (SetProperty(ref _videoStatus, value))
            {
                OnPropertyChanged(nameof(VideoStatusLabel));
            }
        }
    }

    public string LinkStatus
    {
        get => _linkStatus;
        private set
        {
            if (SetProperty(ref _linkStatus, value))
            {
                OnPropertyChanged(nameof(LinkStatusLabel));
            }
        }
    }

    public string DebugModeLabel { get; }

    public string ScenarioSummary { get; }

    public string StatusFootnote
    {
        get => _statusFootnote;
        private set => SetProperty(ref _statusFootnote, value);
    }

    public string FooterSummary
    {
        get => _footerSummary;
        private set => SetProperty(ref _footerSummary, value);
    }

    public string MqttStatusLabel => $"MQTT: {MqttStatus}";

    public string VideoStatusLabel => $"Video: {VideoStatus}";

    public string LinkStatusLabel => $"Link: {LinkStatus}";

    public VideoViewModel Video { get; }

    public HudOverlayViewModel Hud { get; }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot))
        {
            return;
        }

        var snapshot = _telemetryStore.CurrentSnapshot;
        CurrentRobotLabel = snapshot.CurrentRobot.RobotLabel;
        MqttStatus = snapshot.MqttState.ToDisplayText();
        LinkStatus = snapshot.LinkState.ToDisplayText();
        StatusFootnote = snapshot.LastUpdateText;
        FooterSummary = snapshot.WarningText;
    }

    private void HandleVideoChanged(object? sender, PropertyChangedEventArgs args)
    {
        VideoStatus = _videoStreamService.State.ToDisplayText();
    }
}
