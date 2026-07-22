using Alliance.Client.Features.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Hud;

public sealed class HudLayoutSettings : ObservableObject
{
    private const double MinTextScale = 0.5;
    private const double MaxTextScale = 1.5;
    private const double MinWidthScale = 0.5;
    private const double MaxWidthScale = 6.0;
    private const double Step = 0.1;
    private const double MinOpacity = 0.3;
    private const double MaxOpacity = 1.0;
    private const double OpacityStep = 0.05;
    private const double DefaultTextScale = 1.2;
    private const double DefaultWidthScale = 0.8;

    private readonly AppSettings? _settings;
    private double _robotTextScale = DefaultTextScale;
    private double _robotWidthScale = DefaultWidthScale;
    private double _matchInfoPanelBackgroundOpacity = 0.8;

    public HudLayoutSettings()
    {
    }

    public HudLayoutSettings(AppSettings settings)
    {
        _settings = settings;
        _robotTextScale = NormalizeTextScale(settings.Hud.RobotTextScale);
        _robotWidthScale = NormalizeWidthScale(settings.Hud.RobotWidthScale);
        _matchInfoPanelBackgroundOpacity = NormalizeOpacity(settings.Hud.MatchInfoPanelBackgroundOpacity);
    }

    public double RobotTextScale
    {
        get => _robotTextScale;
        private set
        {
            if (SetProperty(ref _robotTextScale, value) && _settings is not null)
            {
                _settings.Hud.RobotTextScale = value;
            }
        }
    }

    public double RobotWidthScale
    {
        get => _robotWidthScale;
        private set
        {
            if (SetProperty(ref _robotWidthScale, value) && _settings is not null)
            {
                _settings.Hud.RobotWidthScale = value;
            }
        }
    }

    public double MatchInfoPanelBackgroundOpacity
    {
        get => _matchInfoPanelBackgroundOpacity;
        private set
        {
            if (SetProperty(ref _matchInfoPanelBackgroundOpacity, value) && _settings is not null)
            {
                _settings.Hud.MatchInfoPanelBackgroundOpacity = value;
            }
        }
    }

    public bool IncreaseRobotText() => SetRobotText(RobotTextScale + Step);

    public bool DecreaseRobotText() => SetRobotText(RobotTextScale - Step);

    public bool IncreaseRobotWidth() => SetRobotWidth(RobotWidthScale + Step);

    public bool DecreaseRobotWidth() => SetRobotWidth(RobotWidthScale - Step);

    public bool IncreaseMatchInfoPanelBackgroundOpacity() =>
        SetMatchInfoPanelBackgroundOpacity(MatchInfoPanelBackgroundOpacity + OpacityStep);

    public bool DecreaseMatchInfoPanelBackgroundOpacity() =>
        SetMatchInfoPanelBackgroundOpacity(MatchInfoPanelBackgroundOpacity - OpacityStep);

    private bool SetRobotText(double value)
    {
        var next = NormalizeTextScale(value);
        if (Math.Abs(next - RobotTextScale) < 0.001)
        {
            return false;
        }

        RobotTextScale = next;
        return true;
    }

    private bool SetRobotWidth(double value)
    {
        var next = NormalizeWidthScale(value);
        if (Math.Abs(next - RobotWidthScale) < 0.001)
        {
            return false;
        }

        RobotWidthScale = next;
        return true;
    }

    private bool SetMatchInfoPanelBackgroundOpacity(double value)
    {
        var next = NormalizeOpacity(value);
        if (Math.Abs(next - MatchInfoPanelBackgroundOpacity) < 0.001)
        {
            return false;
        }

        MatchInfoPanelBackgroundOpacity = next;
        return true;
    }

    private static double NormalizeTextScale(double value)
    {
        return Math.Round(Math.Clamp(value, MinTextScale, MaxTextScale), 2);
    }

    private static double NormalizeWidthScale(double value)
    {
        return Math.Round(Math.Clamp(value, MinWidthScale, MaxWidthScale), 2);
    }

    private static double NormalizeOpacity(double value)
    {
        return Math.Round(Math.Clamp(value, MinOpacity, MaxOpacity), 2);
    }
}
