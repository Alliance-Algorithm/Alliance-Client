using Alliance.Client.Features.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Hud;

public sealed class RobotStatusBarViewModel : ObservableObject
{
    private string _caption;
    private IReadOnlyList<RobotStatusSnapshot> _robots;

    public RobotStatusBarViewModel(string caption, IReadOnlyList<RobotStatusSnapshot> robots, bool isEnemy = false)
    {
        _caption = caption;
        _robots = robots;
        IsEnemy = isEnemy;
    }

    public string Caption
    {
        get => _caption;
        set => SetProperty(ref _caption, value);
    }

    public IReadOnlyList<RobotStatusSnapshot> Robots
    {
        get => _robots;
        set => SetProperty(ref _robots, value);
    }

    public bool IsEnemy { get; }
}