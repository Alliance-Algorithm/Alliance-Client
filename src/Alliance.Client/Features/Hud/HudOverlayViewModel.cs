using System.ComponentModel;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Hud;

public sealed class HudOverlayViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly VideoStreamStore _videoStreamStore;
    private readonly HudLayoutSettings _layoutSettings;
    private TelemetrySnapshot _snapshot;
    private RobotStatusBarViewModel _allyRobotsViewModel;
    private RobotStatusBarViewModel _enemyRobotsViewModel;

    public HudOverlayViewModel(
        TelemetryStore telemetryStore,
        VideoStreamStore videoStreamStore,
        HudLayoutSettings layoutSettings)
    {
        _telemetryStore = telemetryStore;
        _videoStreamStore = videoStreamStore;
        _layoutSettings = layoutSettings;
        _snapshot = telemetryStore.CurrentSnapshot;
        _allyRobotsViewModel = new RobotStatusBarViewModel("ALLY ROBOTS", _snapshot.AllyRobots, isEnemy: false);
        _enemyRobotsViewModel = new RobotStatusBarViewModel("ENEMY ROBOTS", _snapshot.EnemyRobots, isEnemy: true);
        ApplyRobotTextScale();
        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
        _layoutSettings.PropertyChanged += HandleLayoutChanged;
    }

    public TelemetrySnapshot Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public RobotStatusBarViewModel AllyRobotsViewModel
    {
        get => _allyRobotsViewModel;
        private set => SetProperty(ref _allyRobotsViewModel, value);
    }

    public RobotStatusBarViewModel EnemyRobotsViewModel
    {
        get => _enemyRobotsViewModel;
        private set => SetProperty(ref _enemyRobotsViewModel, value);
    }

    public VideoStreamStore Video => _videoStreamStore;

    public HudLayoutSettings LayoutSettings => _layoutSettings;

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TelemetryStore.CurrentSnapshot))
        {
            Snapshot = _telemetryStore.CurrentSnapshot;
            AllyRobotsViewModel.Robots = Snapshot.AllyRobots;
            EnemyRobotsViewModel.Robots = Snapshot.EnemyRobots;
        }
    }

    private void HandleLayoutChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(HudLayoutSettings.RobotTextScale))
        {
            ApplyRobotTextScale();
        }
    }

    private void ApplyRobotTextScale()
    {
        AllyRobotsViewModel.RobotTextScale = _layoutSettings.RobotTextScale;
        EnemyRobotsViewModel.RobotTextScale = _layoutSettings.RobotTextScale;
    }
}
