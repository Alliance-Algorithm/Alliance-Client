namespace Alliance.Client.Shared.Utils;

public static class TelemetryText
{
    public static string FormatDegrees(double value)
    {
        return $"{value:000} DEG";
    }

    public static string FormatMeters(double value)
    {
        return $"{value:0.0} m";
    }

    public static string FormatMetersPerSecond(double value)
    {
        return $"{value:0.0} m/s";
    }
}
