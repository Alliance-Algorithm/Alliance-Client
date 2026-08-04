namespace Alliance.Client.Features.HeroTelemetry;

public sealed record HeroRobotStatus
{
    public const int FrontMaxSpeed = 600;
    public const int RearMaxSpeed = 500;
    public const int FrontMarkerValue = 370;
    public const int RearMarkerValue = 308;

    public static readonly HeroRobotStatus Empty = new();

    public int PitchEncoder { get; init; }
    public int YawEncoder { get; init; }
    public int TargetBulletSpeed { get; init; }
    public int ControlVelocityFront { get; init; }
    public int ControlVelocityRear { get; init; }
    public int Velocity0 { get; init; }
    public int Velocity1 { get; init; }
    public int Velocity2 { get; init; }
    public int Velocity3 { get; init; }
    public int Velocity4 { get; init; }
    public int Velocity5 { get; init; }

    public bool HasData => PitchEncoder != int.MinValue;

    public string BulletSpeedText => HasData && TargetBulletSpeed != int.MinValue
        ? $"{TargetBulletSpeed} m/s"
        : "--";

    public string PitchText => HasData && PitchEncoder != int.MinValue
        ? PitchEncoder.ToString()
        : "--";

    public string YawText => HasData && YawEncoder != int.MinValue
        ? YawEncoder.ToString()
        : "--";

    public double Velocity0Percent => Normalize(Velocity0, FrontMaxSpeed);
    public double Velocity1Percent => Normalize(Velocity1, FrontMaxSpeed);
    public double Velocity2Percent => Normalize(Velocity2, FrontMaxSpeed);
    public double Velocity3Percent => Normalize(Velocity3, RearMaxSpeed);
    public double Velocity4Percent => Normalize(Velocity4, RearMaxSpeed);
    public double Velocity5Percent => Normalize(Velocity5, RearMaxSpeed);

    public string Velocity0Text => FormatVelocity(Velocity0);
    public string Velocity1Text => FormatVelocity(Velocity1);
    public string Velocity2Text => FormatVelocity(Velocity2);
    public string Velocity3Text => FormatVelocity(Velocity3);
    public string Velocity4Text => FormatVelocity(Velocity4);
    public string Velocity5Text => FormatVelocity(Velocity5);

    public double FrontMarkerPercent => (double)FrontMarkerValue / FrontMaxSpeed;
    public double RearMarkerPercent => (double)RearMarkerValue / RearMaxSpeed;

    private static double Normalize(int value, int max)
    {
        if (value == int.MinValue) return 0.0;
        return Math.Clamp((double)value / max, 0.0, 1.0);
    }

    private static string FormatVelocity(int value)
    {
        return value == int.MinValue ? "--" : value.ToString();
    }
}
