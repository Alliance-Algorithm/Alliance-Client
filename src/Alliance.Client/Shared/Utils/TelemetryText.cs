namespace Alliance.Client.Shared.Utils;

public static class TelemetryText
{
    public static string FormatCountdown(int totalSeconds)
    {
        var safeSeconds = Math.Max(totalSeconds, 0);
        var minutes = safeSeconds / 60;
        var seconds = safeSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    public static string FormatHealth(int current, int maximum)
    {
        return maximum > 0
            ? $"HP {current}/{maximum}"
            : $"HP {current}/--";
    }

    public static string FormatFireRate(double value)
    {
        return $"ROF {value:0.0}";
    }

    public static string FormatAmmo(int value)
    {
        return $"AMMO {value}";
    }

    public static string FormatStructure(string label, int value)
    {
        return $"{label} {value}";
    }

    public static string FormatDamage(int value)
    {
        return $"DMG {value}";
    }
}
