using Alliance.Client.Features.HeroTelemetry;
using Alliance.Client.Features.RadarTelemetry;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;

namespace Alliance.Client.Features.Telemetry;

public sealed record TeamPanelSnapshot(
    string SideLabel,
    string BaseHealthText,
    string OutpostHealthText,
    string DamageText,
    string EconomyText,
    int? BaseHealthValue = null,
    int? BaseShieldValue = null,
    int? OutpostHealthValue = null,
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

    public double BaseHpBarPercent
    {
        get
        {
            var hp = BaseHealthValue ?? 0;
            var shield = BaseShieldValue ?? 0;
            var total = hp + shield;
            return total > 0 ? Math.Clamp((double)hp / total, 0, 1) : 0;
        }
    }

    public double BaseShieldBarPercent
    {
        get
        {
            var hp = BaseHealthValue ?? 0;
            var shield = BaseShieldValue ?? 0;
            var total = hp + shield;
            return total > 0 ? Math.Clamp((double)shield / total, 0, 1) : 0;
        }
    }

    public bool HasBaseShield => (BaseShieldValue ?? 0) > 0;

    public double OutpostHealthPercent =>
        OutpostHealthValue.HasValue
            ? Math.Clamp(OutpostHealthValue.Value / 1500d, 0, 1)
            : 0;

    public string BaseBarColorClass => BaseHpBarPercent switch
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

    public string BaseHealthDisplay => BaseHealthValue.HasValue
        ? $"{BaseHealthValue.Value}（{BaseShieldValue ?? 0}）"
        : "--";

    public string OutpostHealthDisplay => OutpostHealthNumber;

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
    bool IsBlue = true,
    string RobotTypeText = "--",
    string HealthDisplayText = "--",
    string AmmoDisplayText = "弹 --",
    bool IsAlive = true,
    bool IsAerial = false,
    bool IsRadarLocked = false,
    bool IsAirSupportCountered = false,
    IReadOnlyList<string>? BuffLabels = null)
{
    public double HealthPercent =>
        HealthValue.HasValue && MaxHealthValue is > 0
            ? Math.Clamp((double)HealthValue.Value / MaxHealthValue.Value, 0, 1)
            : 0;

    public string BarColorClass => IsEnemy ? "enemy" : "ally";

    public IReadOnlyList<string> DisplayBuffLabels => BuffLabels ?? [];

    public bool HasFirstBuff => DisplayBuffLabels.Count >= 1;

    public bool HasSecondBuff => DisplayBuffLabels.Count >= 2;

    public string FirstBuffText => HasFirstBuff ? DisplayBuffLabels[0] : string.Empty;

    public string SecondBuffText => HasSecondBuff ? DisplayBuffLabels[1] : string.Empty;

    public double CardOpacity => IsAlive ? 1.0 : 0.46;

    public string StateText =>
        IsAerial
            ? (IsAirSupportCountered ? "【被反制】" : "空中单位")
            : IsAlive ? "ONLINE" : "已击毁";
}

public sealed record CurrentRobotPanelSnapshot(
    string RobotLabel,
    string HealthText,
    string PerformanceText,
    int? CurrentHealth = null,
    int? MaxHealth = null,
    int? Level = null,
    int? ExperienceForUpgrade = null,
    int? RemainingAmmo = null)
{
    public static CurrentRobotPanelSnapshot Empty(string robotLabel)
    {
        return new CurrentRobotPanelSnapshot(
            robotLabel,
            "HP --/--",
            "性能 --");
    }

    public double HealthPercent =>
        CurrentHealth.HasValue && MaxHealth is > 0
            ? Math.Clamp((double)CurrentHealth.Value / MaxHealth.Value, 0, 1)
            : 0;

    public string LevelText => Level.HasValue ? $"Lv.{Level.Value}" : "Lv.--";

    public string UpgradeNeededText => ExperienceForUpgrade.HasValue
        ? $"升级还需: {ExperienceForUpgrade.Value}"
        : "升级还需: --";

    public string AmmoText => RemainingAmmo.HasValue
        ? $"允许发弹量: {RemainingAmmo.Value}"
        : "允许发弹量: --";

    public string BarColorClass => HealthPercent switch
    {
        >= 0.6 => "healthy",
        >= 0.3 => "damaged",
        _ => "critical"
    };
}

public sealed record ReverseBuffLineSnapshot(
    int BuffType,
    string TypeLabel,
    string ValueText,
    int RemainingSeconds,
    string DisplayText);

public sealed record ReversePanelSnapshot(
    IReadOnlyList<ReverseBuffLineSnapshot> BuffLines,
    float? LastProjectileFireRate = null,
    float? HeatCooldownRate = null,
    float? CurrentHeat = null,
    int? MaxHeat = null,
    int? CurrentChassisEnergy = null,
    int? MaxChassisEnergy = null)
{
    public static ReversePanelSnapshot Empty { get; } = new([]);

    public bool HasBuffs => BuffLines.Count > 0;

    public string FireRateText => LastProjectileFireRate.HasValue
        ? LastProjectileFireRate.Value.ToString("0.##")
        : "--";

    public string HeatCooldownText => HeatCooldownRate.HasValue
        ? $"{HeatCooldownRate.Value:0.##}/s"
        : "--/s";

    public string HeatValueText => CurrentHeat.HasValue || MaxHeat.HasValue
        ? $"{CurrentHeat?.ToString("0.##") ?? "--"} | {MaxHeat?.ToString() ?? "--"}"
        : "-- | --";

    public double HeatPercent =>
        CurrentHeat.HasValue && MaxHeat is > 0
            ? Math.Clamp(CurrentHeat.Value / MaxHeat.Value, 0, 1)
            : 0;

    public string ChassisEnergyText => CurrentChassisEnergy.HasValue && MaxChassisEnergy.HasValue
        ? $"{CurrentChassisEnergy.Value}/{MaxChassisEnergy.Value} J"
        : "-- J";

    public double ChassisEnergyPercent =>
        CurrentChassisEnergy.HasValue && MaxChassisEnergy is > 0
            ? Math.Clamp((double)CurrentChassisEnergy.Value / MaxChassisEnergy.Value, 0, 1)
            : 0;
}

public enum SideAlertKind
{
    OutpostRebuildable,
    OutpostRebuilding,
    FortressCapture
}

public sealed record SideAlertSnapshot(
    SideAlertKind Kind,
    string Title,
    double Progress,
    bool ShowStageMark = false,
    double StageMarkProgress = 0.5,
    bool IsEnemy = false,
    bool IsBlue = true)
{
    public bool ShowProgress => Kind is SideAlertKind.OutpostRebuilding or SideAlertKind.FortressCapture;
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

    public int? CurrentRound { get; init; }

    public int? TotalRounds { get; init; }

    public int? RedScore { get; init; }

    public int? BlueScore { get; init; }

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

    public ReversePanelSnapshot ReversePanel { get; init; } = ReversePanelSnapshot.Empty;

    public IReadOnlyList<SideAlertSnapshot> AllySideAlerts { get; init; } = [];

    public IReadOnlyList<SideAlertSnapshot> EnemySideAlerts { get; init; } = [];

    public bool ShowBaseAttackToast { get; init; }

    public string BaseAttackToastText { get; init; } = "基地遭到攻击";

    public EventTelemetrySnapshot? LatestEvent { get; init; }

    public IReadOnlyList<SpecialMechanismTelemetrySnapshot> ActiveMechanisms { get; init; } = [];

    public IReadOnlyList<RadarRobotTelemetrySnapshot> RadarRobots { get; init; } = [];

    public EnemyRadarData? EnemyRadarData { get; init; }

    public HeroRobotStatus? HeroRobotStatus { get; init; }

    public IReadOnlyList<RobotBuffTelemetrySnapshot> ActiveBuffs { get; init; } = [];

    public string LastUpdateText { get; init; } = "Awaiting MQTT packets";

    public string WarningText { get; init; } = "Telemetry offline";

    public string MqttStatusText => $"MQTT {MqttState.ToDisplayText()}";

    public string LinkStatusText => $"LINK {LinkState.ToDisplayText()}";

    public string LastUpdateCompactText
    {
        get
        {
            const string prefix = "Last telemetry ";
            const string suffix = " ago";
            if (LastUpdateText.StartsWith(prefix, StringComparison.Ordinal) &&
                LastUpdateText.EndsWith(suffix, StringComparison.Ordinal))
            {
                return $"DATA {LastUpdateText[prefix.Length..^suffix.Length]}";
            }

            return LastUpdateText == "Awaiting MQTT packets" ? "DATA --" : LastUpdateText;
        }
    }

    public string RoundText =>
        CurrentRound.HasValue || TotalRounds.HasValue
            ? $"Round {CurrentRound?.ToString() ?? "--"}/{TotalRounds?.ToString() ?? "--"}"
            : "Round --";

    public string BlueScoreText => BlueScore?.ToString() ?? "--";

    public string RedScoreText => RedScore?.ToString() ?? "--";

    public string MechanismSummaryText =>
        ActiveMechanisms.Count == 0
            ? "机制 --"
            : string.Join("  ", ActiveMechanisms.Select(m => m.SummaryText));

    private static IReadOnlyList<RobotStatusSnapshot> CreateDefaultRobotBars()
    {
        return
        [
            new RobotStatusSnapshot("1", "--", "--", "--"),
            new RobotStatusSnapshot("2", "--", "--", "--"),
            new RobotStatusSnapshot("3", "--", "--", "--"),
            new RobotStatusSnapshot("4", "--", "--", "--"),
            new RobotStatusSnapshot("7", "--", "--", "--"),
            new RobotStatusSnapshot("6", "--", "--", "--", ShowHealthBar: false, IsAerial: true)
        ];
    }
}
