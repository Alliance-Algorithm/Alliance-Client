using System.ComponentModel;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Infrastructure.Runtime;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Alliance.Client.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly AppSettings _settings;
    private readonly AppRuntimeCoordinator _runtimeCoordinator;
    private readonly VideoStreamStore _videoStreamStore;
    private readonly HudLayoutSettings _hudLayoutSettings;
    private string _currentRobotLabel;
    private Window? _settingsDialog;

    public MainWindowViewModel(
        HudOverlayViewModel hud,
        TelemetryStore telemetryStore,
        AppSettings settings,
        VideoStreamStore videoStreamStore,
        AppRuntimeCoordinator runtimeCoordinator,
        HudLayoutSettings hudLayoutSettings)
    {
        Hud = hud;
        _telemetryStore = telemetryStore;
        _settings = settings;
        _videoStreamStore = videoStreamStore;
        _runtimeCoordinator = runtimeCoordinator;
        _hudLayoutSettings = hudLayoutSettings;

        WindowTitle = settings.ApplicationName;

        var snapshot = telemetryStore.CurrentSnapshot;
        _currentRobotLabel = snapshot.CurrentRobot.RobotLabel;

        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
        _hudLayoutSettings.PropertyChanged += HandleHudLayoutSettingsChanged;
    }

    public string WindowTitle { get; }

    public string CurrentRobotLabel
    {
        get => _currentRobotLabel;
        private set => SetProperty(ref _currentRobotLabel, value);
    }

    public HudOverlayViewModel Hud { get; }

    public HorizontalAlignment SettingsButtonHorizontalAlignment =>
        _hudLayoutSettings.RobotStatusBarsOnLeft
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

    public Thickness SettingsButtonMargin =>
        _hudLayoutSettings.RobotStatusBarsOnLeft
            ? new Thickness(0, 30, 30, 0)
            : new Thickness(30, 30, 0, 0);

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot)) return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        CurrentRobotLabel = snapshot.CurrentRobot.RobotLabel;
    }

    private void HandleHudLayoutSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(HudLayoutSettings.RobotStatusBarsOnLeft)) return;

        OnPropertyChanged(nameof(SettingsButtonHorizontalAlignment));
        OnPropertyChanged(nameof(SettingsButtonMargin));
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
            _hudLayoutSettings);
        var dialog = new SettingsDialog(vm);
        dialog.Closed += (_, _) => _settingsDialog = null;
        _settingsDialog = dialog;
        dialog.ShowDialog(owner);
    }
}
