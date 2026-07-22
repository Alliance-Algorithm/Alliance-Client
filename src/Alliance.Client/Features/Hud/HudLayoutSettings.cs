using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Hud;

public sealed class HudLayoutSettings : ObservableObject
{
    private const double MinTextScale = 0.8;
    private const double MaxTextScale = 1.5;
    private const double MinWidthScale = 0.8;
    private const double MaxWidthScale = 6.0;
    private const double Step = 0.1;

    private double _robotTextScale = 1.0;
    private double _robotWidthScale = 1.0;
    private bool _robotStatusBarsOnLeft;

    public double RobotTextScale
    {
        get => _robotTextScale;
        private set => SetProperty(ref _robotTextScale, value);
    }

    public double RobotWidthScale
    {
        get => _robotWidthScale;
        private set => SetProperty(ref _robotWidthScale, value);
    }

    public bool RobotStatusBarsOnLeft
    {
        get => _robotStatusBarsOnLeft;
        private set => SetProperty(ref _robotStatusBarsOnLeft, value);
    }

    public bool IncreaseRobotText() => SetRobotText(RobotTextScale + Step);

    public bool DecreaseRobotText() => SetRobotText(RobotTextScale - Step);

    public bool IncreaseRobotWidth() => SetRobotWidth(RobotWidthScale + Step);

    public bool DecreaseRobotWidth() => SetRobotWidth(RobotWidthScale - Step);

    public bool SetRobotStatusBarsOnLeft(bool value)
    {
        if (RobotStatusBarsOnLeft == value)
        {
            return false;
        }

        RobotStatusBarsOnLeft = value;
        return true;
    }

    private bool SetRobotText(double value)
    {
        var next = Math.Round(Math.Clamp(value, MinTextScale, MaxTextScale), 2);
        if (Math.Abs(next - RobotTextScale) < 0.001)
        {
            return false;
        }

        RobotTextScale = next;
        return true;
    }

    private bool SetRobotWidth(double value)
    {
        var next = Math.Round(Math.Clamp(value, MinWidthScale, MaxWidthScale), 2);
        if (Math.Abs(next - RobotWidthScale) < 0.001)
        {
            return false;
        }

        RobotWidthScale = next;
        return true;
    }
}
