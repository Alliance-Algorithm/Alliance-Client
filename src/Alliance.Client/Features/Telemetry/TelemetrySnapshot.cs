using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Telemetry;

public sealed record TeamPanelSnapshot(
    string SideLabel,
    string BaseHealthText,
    string OutpostHealthText,
    string DamageText)
{
    public static TeamPanelSnapshot CreateEmpty(string sideLabel)
    {
        return new TeamPanelSnapshot(
            sideLabel,
            "Base --",
            "Outpost --",
            "Damage --");
    }
}

public sealed record RobotHealthBarSnapshot(
    string SlotLabel,
    string HealthText);

public sealed record CurrentRobotPanelSnapshot(
    string RobotLabel,
    string HealthText,
    string FireRateText,
    string AmmoText)
{
    public static CurrentRobotPanelSnapshot Empty(string robotLabel)
    {
        return new CurrentRobotPanelSnapshot(
            robotLabel,
            "HP --/--",
            "ROF --",
            "AMMO --");
    }
}

public sealed record TelemetrySnapshot
{
    public ConnectionState MqttState { get; init; } = ConnectionState.NotConnected;

    public ConnectionState VideoState { get; init; } = ConnectionState.NotConnected;

    public ConnectionState LinkState { get; init; } = ConnectionState.NotConnected;

    public string MatchTimeText { get; init; } = "00:00";

    public TeamPanelSnapshot AllyTeam { get; init; } = TeamPanelSnapshot.CreateEmpty("ALLY");

    public TeamPanelSnapshot EnemyTeam { get; init; } = TeamPanelSnapshot.CreateEmpty("ENEMY");

    public IReadOnlyList<RobotHealthBarSnapshot> AllyRobots { get; init; } =
        CreateDefaultRobotBars();

    public IReadOnlyList<RobotHealthBarSnapshot> EnemyRobots { get; init; } =
        CreateDefaultRobotBars();

    public CurrentRobotPanelSnapshot CurrentRobot { get; init; } =
        CurrentRobotPanelSnapshot.Empty("Robot --");

    public string LastUpdateText { get; init; } = "Awaiting MQTT packets";

    public string WarningText { get; init; } = "Telemetry offline";

    private static IReadOnlyList<RobotHealthBarSnapshot> CreateDefaultRobotBars()
    {
        return
        [
            new RobotHealthBarSnapshot("1", "--"),
            new RobotHealthBarSnapshot("2", "--"),
            new RobotHealthBarSnapshot("3", "--"),
            new RobotHealthBarSnapshot("4", "--"),
            new RobotHealthBarSnapshot("7", "--")
        ];
    }
}
