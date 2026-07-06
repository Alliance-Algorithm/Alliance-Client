using System.ComponentModel;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Infrastructure.Runtime;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly IVideoStreamService _videoStreamService;
    private readonly AppSettings _settings;
    private readonly AppRuntimeCoordinator _runtimeCoordinator;
    private string _currentRobotLabel;
    private string _mqttStatus;
    private string _videoStatus;
    private string _linkStatus;
    private string _statusFootnote;
    private string _footerSummary;
    private int _selectedRobotId;
    private bool _isRobotDropdownOpen;

    public MainWindowViewModel(
        VideoViewModel video,
        HudOverlayViewModel hud,
        TelemetryStore telemetryStore,
        IVideoStreamService videoStreamService,
        AppSettings settings,
        AppRuntimeCoordinator runtimeCoordinator)
    {
        Video = video;
        Hud = hud;
        _telemetryStore = telemetryStore;
        _videoStreamService = videoStreamService;
        _settings = settings;
        _runtimeCoordinator = runtimeCoordinator;

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

        _selectedRobotId = PlayerIdentity.TryResolveRobotId(settings.Mqtt.ClientId, out var robotId)
            ? robotId
            : 1;

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

    public IReadOnlyList<int> RobotIdOptions { get; } = PlayerIdentity.AvailableRobotIds;

    public bool IsRobotDropdownOpen
    {
        get => _isRobotDropdownOpen;
        set => SetProperty(ref _isRobotDropdownOpen, value);
    }

    public int SelectedRobotId
    {
        get => _selectedRobotId;
        set
        {
            if (SetProperty(ref _selectedRobotId, value))
            {
                IsRobotDropdownOpen = false;
                OnSelectedRobotIdChanged();
            }
        }
    }

    public void ToggleRobotDropdown()
    {
        IsRobotDropdownOpen = !IsRobotDropdownOpen;
    }

    private void OnSelectedRobotIdChanged()
    {
        if (PlayerIdentity.TryResolveClientId(_selectedRobotId, out var clientId))
        {
            _settings.Mqtt.ClientId = clientId;
            _ = _runtimeCoordinator.RestartTelemetryAsync();
        }
    }

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
