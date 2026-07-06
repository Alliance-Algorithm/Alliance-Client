namespace Alliance.Client.Shared.Utils;

public static class PlayerIdentity
{
    private static readonly IReadOnlyDictionary<int, string> RobotToClientIdMap = new Dictionary<int, string>
    {
        [1] = "1",
        [2] = "2",
        [3] = "3",
        [4] = "4",
        [6] = "6",
        [101] = "101",
        [102] = "102",
        [103] = "103",
        [104] = "104",
        [106] = "106"
    };

    public static IReadOnlyList<int> AvailableRobotIds { get; } = [1, 2, 3, 4, 6, 101, 102, 103, 104, 106];

    public static bool TryResolveRobotId(string? clientIdText, out int robotId)
    {
        robotId = 0;
        if (string.IsNullOrWhiteSpace(clientIdText)) return false;
        if (int.TryParse(clientIdText.Trim(), out robotId) && RobotToClientIdMap.ContainsKey(robotId))
            return true;
        return false;
    }

    public static bool TryResolveClientId(int robotId, out string clientId)
    {
        return RobotToClientIdMap.TryGetValue(robotId, out clientId!);
    }
}
