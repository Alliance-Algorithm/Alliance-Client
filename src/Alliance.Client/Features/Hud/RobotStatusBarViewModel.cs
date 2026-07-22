using Alliance.Client.Features.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace Alliance.Client.Features.Hud;

public sealed class RobotStatusBarViewModel : ObservableObject
{
    private string _caption;
    private IReadOnlyList<RobotStatusSnapshot> _robots;
    private double _robotTextScale = 1.0;

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
        set
        {
            if (SetProperty(ref _robots, value))
            {
                OnPropertyChanged(nameof(AliveSummaryText));
                OnPropertyChanged(nameof(TotalHealthText));
            }
        }
    }

    public double RobotTextScale
    {
        get => _robotTextScale;
        set => SetProperty(ref _robotTextScale, value);
    }

    public bool IsEnemy { get; }

    public string AliveSummaryText => $"存活 {Robots.Count(r => r.IsAlive)}/{Robots.Count}";

    public string TotalHealthText
    {
        get
        {
            var known = Robots.Where(r => r.HealthValue.HasValue).ToArray();
            if (known.Length == 0)
            {
                return "总血量 --";
            }

            return $"总血量 {known.Sum(r => r.HealthValue!.Value).ToString("N0", CultureInfo.InvariantCulture)}";
        }
    }
}
