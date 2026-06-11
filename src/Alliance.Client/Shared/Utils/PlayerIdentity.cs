namespace Alliance.Client.Shared.Utils;

public static class PlayerIdentity
{
    private static readonly IReadOnlyDictionary<ushort, int> ClientToRobotMap = new Dictionary<ushort, int>
    {
        [0x0101] = 1,
        [0x0102] = 2,
        [0x0103] = 3,
        [0x0104] = 4,
        [0x0105] = 5,
        [0x0106] = 6,
        [0x0165] = 101,
        [0x0166] = 102,
        [0x0167] = 103,
        [0x0168] = 104,
        [0x0169] = 105,
        [0x016A] = 106
    };

    public static bool TryResolveRobotId(string? clientIdText, out int robotId)
    {
        robotId = 0;

        if (string.IsNullOrWhiteSpace(clientIdText))
        {
            return false;
        }

        var normalized = clientIdText.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (!ushort.TryParse(
                normalized,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var clientId))
        {
            return false;
        }

        return ClientToRobotMap.TryGetValue(clientId, out robotId);
    }
}
