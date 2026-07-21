using System.ComponentModel;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.RmcsImage;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Infrastructure.Runtime;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Alliance.Client.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly AppSettings _settings;
    private readonly AppRuntimeCoordinator _runtimeCoordinator;
    private readonly VideoStreamStore _videoStreamStore;
    private readonly ImageWindowViewModel _imageWindowViewModel;
    private string _currentRobotLabel;
    private Window? _settingsDialog;
    private Window? _imageWindow;

    public MainWindowViewModel(
        HudOverlayViewModel hud,
        TelemetryStore telemetryStore,
        AppSettings settings,
        VideoStreamStore videoStreamStore,
        AppRuntimeCoordinator runtimeCoordinator,
        ImageWindowViewModel imageWindowViewModel)
    {
        Hud = hud;
        _telemetryStore = telemetryStore;
        _settings = settings;
        _videoStreamStore = videoStreamStore;
        _runtimeCoordinator = runtimeCoordinator;
        _imageWindowViewModel = imageWindowViewModel;

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

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot)) return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        CurrentRobotLabel = snapshot.CurrentRobot.RobotLabel;
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
            _runtimeCoordinator);
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

        var dialog = new ImageWindow(_imageWindowViewModel);
        dialog.Closed += (_, _) => _imageWindow = null;
        _imageWindow = dialog;
        dialog.Show(owner);
    }
}
