using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Telemetry;

public sealed record TeamPanelSnapshot(
    string SideLabel,
    string BaseHealthText,
    string OutpostHealthText,
    string DamageText,
    string EconomyText,
    int? BaseHealthValue = null,
    int? BaseMaxHealth = null,
    int? OutpostHealthValue = null,
    int? OutpostMaxHealth = null,
    int? TotalDamage = null,
    int? RemainingEconomy = null,
    long? TotalEconomy = null,
    bool IsEnemy = false,
    bool IsBlue = true)
{
    public static TeamPanelSnapshot CreateEmpty(string sideLabel)
    {
        return new TeamPanelSnapshot(
            sideLabel,
            "Base --",
            "Outpost --",
            "DMG --",
            "ECO --");
    }

    public double BaseHealthPercent =>
        BaseHealthValue.HasValue && BaseMaxHealth is > 0
            ? Math.Clamp((double)BaseHealthValue.Value / BaseMaxHealth.Value, 0, 1)
            : 0;

    public double OutpostHealthPercent =>
        OutpostHealthValue.HasValue && OutpostMaxHealth is > 0
            ? Math.Clamp((double)OutpostHealthValue.Value / OutpostMaxHealth.Value, 0, 1)
            : 0;

    public string BaseBarColorClass => BaseHealthPercent switch
    {
        >= 0.6 => "healthy",
        >= 0.3 => "damaged",
        _ => "critical"
    };

    public string OutpostBarColorClass => OutpostHealthPercent switch
    {
        >= 0.6 => "healthy",
        >= 0.3 => "damaged",
        _ => "critical"
    };

    public string DamageValueText => TotalDamage.HasValue
        ? TotalDamage.Value.ToString("N0")
        : "--";

    public string EconomyValueText => RemainingEconomy.HasValue || TotalEconomy.HasValue
        ? $"{RemainingEconomy?.ToString() ?? "--"} | {TotalEconomy?.ToString() ?? "--"}"
        : "-- | --";

    public string EconomyDisplayText => RemainingEconomy.HasValue || TotalEconomy.HasValue
        ? $"当前经济: {RemainingEconomy?.ToString() ?? "--"} | 累计经济: {TotalEconomy?.ToString() ?? "--"}"
        : "当前经济: -- | 累计经济: --";

    public string BaseHealthNumber => BaseHealthValue.HasValue
        ? BaseHealthValue.Value.ToString()
        : "--";

    public string OutpostHealthNumber => OutpostHealthValue.HasValue
        ? OutpostHealthValue.Value.ToString()
        : "--";

    public string TeamColorClass => IsBlue ? "blue" : "red";
}

public sealed record RobotStatusSnapshot(
    string SlotLabel,
    string HealthText,
    string AmmoText,
    string BuffText,
    int? HealthValue = null,
    int? MaxHealthValue = null,
    int? AmmoValue = null,
    bool ShowHealthBar = true,
    bool IsEnemy = false,
    bool IsBlue = true)
{
    public double HealthPercent =>
        HealthValue.HasValue && MaxHealthValue is > 0
            ? Math.Clamp((double)HealthValue.Value / MaxHealthValue.Value, 0, 1)
            : 0;

    public string BarColorClass => IsEnemy ? "enemy" : "ally";
}

public sealed record CurrentRobotPanelSnapshot(
    string RobotLabel,
    string HealthText,
    string BuffText,
    int? CurrentHealth = null,
    int? MaxHealth = null,
    int? Level = null,
    int? ExperienceForUpgrade = null,
    int? RemainingAmmo = null,
    int? CurrentChassisEnergy = null,
    int? MaxChassisEnergy = null)
{
    public static CurrentRobotPanelSnapshot Empty(string robotLabel)
    {
        return new CurrentRobotPanelSnapshot(
            robotLabel,
            "HP --/--",
            "BUFF --");
    }

    public double HealthPercent =>
        CurrentHealth.HasValue && MaxHealth is > 0
            ? Math.Clamp((double)CurrentHealth.Value / MaxHealth.Value, 0, 1)
            : 0;

    public double ChassisEnergyPercent =>
        CurrentChassisEnergy.HasValue && MaxChassisEnergy is > 0
            ? Math.Clamp((double)CurrentChassisEnergy.Value / MaxChassisEnergy.Value, 0, 1)
            : 0;

    public string LevelText => Level.HasValue ? $"Lv.{Level.Value}" : "Lv.--";

    public string UpgradeNeededText => ExperienceForUpgrade.HasValue
        ? $"升级还需: {ExperienceForUpgrade.Value}"
        : "升级还需: --";

    public string AmmoText => RemainingAmmo.HasValue
        ? $"允许发弹量: {RemainingAmmo.Value}"
        : "允许发弹量: --";

    public string ChassisEnergyText => CurrentChassisEnergy.HasValue && MaxChassisEnergy.HasValue
        ? $"剩余能量: {CurrentChassisEnergy.Value}/{MaxChassisEnergy.Value} J"
        : "剩余能量: -- J";

    public string BarColorClass => HealthPercent switch
    {
        >= 0.6 => "healthy",
        >= 0.3 => "damaged",
        _ => "critical"
    };
}

public sealed record EventTelemetrySnapshot(
    int EventId,
    string RawParam,
    string SummaryText);

public sealed record SpecialMechanismTelemetrySnapshot(
    int MechanismId,
    int RemainingSeconds,
    string SummaryText);

public sealed record RadarRobotTelemetrySnapshot(
    int RobotId,
    int? PositionXcm,
    int? PositionYcm,
    int HighlightState,
    bool IsHighlighted,
    bool IsOfflineHighlighted);

public sealed record RobotBuffTelemetrySnapshot(
    int RobotId,
    int BuffType,
    int BuffLevel,
    int MaxSeconds,
    int RemainingSeconds,
    string SummaryText);

public sealed record TelemetrySnapshot
{
    public ConnectionState MqttState { get; init; } = ConnectionState.NotConnected;

    public ConnectionState LinkState { get; init; } = ConnectionState.NotConnected;

    public string MatchTimeText { get; init; } = "00:00";

    public string StageText { get; init; } = "--";

    public TeamPanelSnapshot AllyTeam { get; init; } = TeamPanelSnapshot.CreateEmpty("ALLY");

    public TeamPanelSnapshot EnemyTeam { get; init; } = TeamPanelSnapshot.CreateEmpty("ENEMY");

    public IReadOnlyList<RobotStatusSnapshot> AllyRobots { get; init; } =
        CreateDefaultRobotBars();

    public IReadOnlyList<RobotStatusSnapshot> EnemyRobots { get; init; } =
        CreateDefaultRobotBars();

    public CurrentRobotPanelSnapshot CurrentRobot { get; init; } =
        CurrentRobotPanelSnapshot.Empty("Robot --");

    public EventTelemetrySnapshot? LatestEvent { get; init; }

    public IReadOnlyList<SpecialMechanismTelemetrySnapshot> ActiveMechanisms { get; init; } = [];

    public IReadOnlyList<RadarRobotTelemetrySnapshot> RadarRobots { get; init; } = [];

    public IReadOnlyList<RobotBuffTelemetrySnapshot> ActiveBuffs { get; init; } = [];

    public string LastUpdateText { get; init; } = "Awaiting MQTT packets";

    public string WarningText { get; init; } = "Telemetry offline";

    private static IReadOnlyList<RobotStatusSnapshot> CreateDefaultRobotBars()
    {
        return
        [
            new RobotStatusSnapshot("1", "--", "--", "--"),
            new RobotStatusSnapshot("2", "--", "--", "--"),
            new RobotStatusSnapshot("3", "--", "--", "--"),
            new RobotStatusSnapshot("4", "--", "--", "--"),
            new RobotStatusSnapshot("7", "--", "--", "--"),
            new RobotStatusSnapshot("6", "--", "--", "--", ShowHealthBar: false)
        ];
    }
}
