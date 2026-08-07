using System.ComponentModel;
using Alliance.Client.Features.Audio;
using Alliance.Client.Features.Dart;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.RmcsImage;
using Alliance.Client.Features.ScreenRecording;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Infrastructure.Runtime;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly AppSettings _settings;
    private readonly AppRuntimeCoordinator _runtimeCoordinator;
    private readonly VideoStreamStore _videoStreamStore;
    private readonly ImageWindowViewModel _imageWindowViewModel;
    private readonly RecordingSettings _recordingSettings;
    private readonly ScreenRecorderService _screenRecorder;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly HudLayoutSettings _hudLayoutSettings;
    private readonly DartAutoService _dartAuto;
    private readonly EnemyRespawnAudioService _respawnAudio;
    private readonly SentinelAmmolessAlertService _sentinelAmmolessAlertService;
    private string _currentRobotLabel;
    private uint _previousStage;
    private Window? _settingsDialog;
    private Window? _imageWindow;

    public MainWindowViewModel(
        HudOverlayViewModel hud,
        TelemetryStore telemetryStore,
        AppSettings settings,
        VideoStreamStore videoStreamStore,
        AppRuntimeCoordinator runtimeCoordinator,
        ImageWindowViewModel imageWindowViewModel,
        ScreenRecorderViewModel screenRecorder,
        ScreenRecorderService screenRecorderService,
        RecordingSettings recordingSettings,
        ILogger<MainWindowViewModel> logger,
        HudLayoutSettings hudLayoutSettings,
        DartAutoService dartAuto,
        EnemyRespawnAudioService respawnAudio,
        SentinelAmmolessAlertService sentinelAmmolessAlertService)
    {
        Hud = hud;
        _telemetryStore = telemetryStore;
        _settings = settings;
        _videoStreamStore = videoStreamStore;
        _runtimeCoordinator = runtimeCoordinator;
        _imageWindowViewModel = imageWindowViewModel;
        Recorder = screenRecorder;
        _recordingSettings = recordingSettings;
        _screenRecorder = screenRecorderService;
        _logger = logger;
        _hudLayoutSettings = hudLayoutSettings;
        _dartAuto = dartAuto;
        _respawnAudio = respawnAudio;
        _sentinelAmmolessAlertService = sentinelAmmolessAlertService;

        WindowTitle = settings.ApplicationName;

        var snapshot = telemetryStore.CurrentSnapshot;
        _currentRobotLabel = snapshot.CurrentRobot.RobotLabel;

        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
    }

    public string WindowTitle { get; }

    public string CurrentRobotLabel
    {
        get => _currentRobotLabel;
        private set => SetProperty(ref _currentRobotLabel, value);
    }

    public HudOverlayViewModel Hud { get; }

    public ScreenRecorderViewModel Recorder { get; }

    public DartAutoService Dart => _dartAuto;

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot)) return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        CurrentRobotLabel = snapshot.CurrentRobot.RobotLabel;

        var gs = _telemetryStore.GameStatus;
        if (gs is null) return;

        var stage = gs.CurrentStage;

        if (stage == 3 && _previousStage != 3 && !_screenRecorder.IsRecording)
            ToggleRecording();

        if (stage == 5 && _previousStage != 5 && _screenRecorder.IsRecording)
            ToggleRecording();

        _previousStage = stage;
    }

    public void OpenSettings(Window owner)
    {
        if (_settingsDialog is { IsVisible: true })
        {
            _settingsDialog.BringIntoView();
            return;
        }

        var vm = new SettingsDialogViewModel(
            _telemetryStore,
            _videoStreamStore,
            _settings,
            _runtimeCoordinator,
            _hudLayoutSettings,
            _recordingSettings,
            _screenRecorder,
            _respawnAudio,
            _sentinelAmmolessAlertService);
        var dialog = new SettingsDialog(vm);
        dialog.Closed += (_, _) => _settingsDialog = null;
        _settingsDialog = dialog;
        dialog.ShowDialog(owner);
    }

    public void OpenImage(Window owner)
    {
        if (_imageWindow is { IsVisible: true })
        {
            _imageWindow.BringIntoView();
            return;
        }

        _logger.LogInformation("Image window opened");
        var dialog = new ImageWindow(_imageWindowViewModel);
        dialog.Closed += (_, _) => _imageWindow = null;
        _imageWindow = dialog;
        dialog.Show(owner);
    }

    public void ToggleRecording()
    {
        Recorder.ToggleRecordingCommand.Execute(null);
    }

    public void HandleKeyDown(KeyEventArgs e)
    {
        if (_recordingSettings.KeyBindingPressed(e))
        {
            e.Handled = true;
            ToggleRecording();
        }
    }
}
