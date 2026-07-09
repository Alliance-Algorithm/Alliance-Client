using System.ComponentModel;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Infrastructure.Runtime;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Alliance.Client.Features.Settings;

public sealed partial class SettingsDialogViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly AppSettings _settings;
    private readonly AppRuntimeCoordinator _runtimeCoordinator;
    private string _mqttStatusLabel;
    private string _linkStatusLabel;
    private string _lastUpdateText;
    private string? _selectedClientId;
    private bool _isBasicTab = true;
    private bool _isMessageTab;
    private string? _selectedTopic;
    private IReadOnlyList<string> _fields = [];

    public SettingsDialogViewModel(
        TelemetryStore telemetryStore,
        AppSettings settings,
        AppRuntimeCoordinator runtimeCoordinator)
    {
        _telemetryStore = telemetryStore;
        _settings = settings;
        _runtimeCoordinator = runtimeCoordinator;

        var snapshot = telemetryStore.CurrentSnapshot;
        _mqttStatusLabel = snapshot.MqttState.ToDisplayText();
        _linkStatusLabel = snapshot.LinkState.ToDisplayText();
        _lastUpdateText = snapshot.LastUpdateText;

        AvailableClientIds = PlayerIdentity.AvailableRobotIds
            .Select(id => id.ToString()).ToList();
        _selectedClientId = settings.Mqtt.ClientId;

        Topics =
        [
            nameof(GameStatus),
            nameof(GlobalUnitStatus),
            nameof(GlobalLogisticsStatus),
            nameof(GlobalSpecialMechanism),
            nameof(Event),
            nameof(RobotStaticStatus),
            nameof(RobotDynamicStatus),
            nameof(Buff),
            nameof(RadarInfoToClient)
        ];
        _selectedTopic = Topics[0];
        RefreshFields();

        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
    }

    public string MqttStatusLabel
    {
        get => _mqttStatusLabel;
        private set => SetProperty(ref _mqttStatusLabel, value);
    }

    public string LinkStatusLabel
    {
        get => _linkStatusLabel;
        private set => SetProperty(ref _linkStatusLabel, value);
    }

    public string LastUpdateText
    {
        get => _lastUpdateText;
        private set => SetProperty(ref _lastUpdateText, value);
    }

    public string? SelectedClientId
    {
        get => _selectedClientId;
        set => SetProperty(ref _selectedClientId, value);
    }

    public IReadOnlyList<string> AvailableClientIds { get; }

    public bool IsBasicTab
    {
        get => _isBasicTab;
        set
        {
            if (SetProperty(ref _isBasicTab, value) && value)
                IsMessageTab = false;
        }
    }

    public bool IsMessageTab
    {
        get => _isMessageTab;
        set
        {
            if (SetProperty(ref _isMessageTab, value) && value)
                IsBasicTab = false;
        }
    }

    public IReadOnlyList<string> Topics { get; }

    public string? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (SetProperty(ref _selectedTopic, value))
                RefreshFields();
        }
    }

    public IReadOnlyList<string> Fields
    {
        get => _fields;
        private set => SetProperty(ref _fields, value);
    }

    [RelayCommand]
    private async Task ApplyClientIdAsync()
    {
        if (string.IsNullOrEmpty(SelectedClientId)) return;

        _settings.Mqtt.ClientId = SelectedClientId;
        await _runtimeCoordinator.RestartTelemetryAsync();
    }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot)) return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        MqttStatusLabel = snapshot.MqttState.ToDisplayText();
        LinkStatusLabel = snapshot.LinkState.ToDisplayText();
        LastUpdateText = snapshot.LastUpdateText;
    }

    private void RefreshFields()
    {
        if (_selectedTopic is null) { Fields = []; return; }

        Fields = _selectedTopic switch
        {
            nameof(GameStatus) => BuildGameStatusFields(),
            nameof(GlobalUnitStatus) => BuildGlobalUnitStatusFields(),
            nameof(GlobalLogisticsStatus) => BuildGlobalLogisticsStatusFields(),
            nameof(GlobalSpecialMechanism) => BuildGlobalSpecialMechanismFields(),
            nameof(Event) => BuildEventFields(),
            nameof(RobotStaticStatus) => BuildRobotStaticStatusFields(),
            nameof(RobotDynamicStatus) => BuildRobotDynamicStatusFields(),
            nameof(Buff) => BuildBuffFields(),
            nameof(RadarInfoToClient) => BuildRadarFields(),
            _ => []
        };
    }

    private IReadOnlyList<string> BuildGameStatusFields()
    {
        var s = _telemetryStore.GameStatus;
        if (s is null) return ["(no data)"];
        return [F("current_round", s.CurrentRound), F("total_rounds", s.TotalRounds),
            F("red_score", s.RedScore), F("blue_score", s.BlueScore),
            F("current_stage", s.CurrentStage), F("stage_countdown_sec", s.StageCountdownSec),
            F("stage_elapsed_sec", s.StageElapsedSec), F("is_paused", s.IsPaused),
            F("game_result", s.GameResult), F("end_reason", s.EndReason)];
    }

    private IReadOnlyList<string> BuildGlobalUnitStatusFields()
    {
        var s = _telemetryStore.GlobalUnitStatus;
        if (s is null) return ["(no data)"];
        return [F("base_health", s.BaseHealth), F("base_status", s.BaseStatus),
            F("base_shield", s.BaseShield), F("outpost_health", s.OutpostHealth),
            F("outpost_status", s.OutpostStatus), F("enemy_base_health", s.EnemyBaseHealth),
            F("enemy_base_status", s.EnemyBaseStatus), F("enemy_base_shield", s.EnemyBaseShield),
            F("enemy_outpost_health", s.EnemyOutpostHealth), F("enemy_outpost_status", s.EnemyOutpostStatus),
            F("robot_health", $"[{string.Join(", ", s.RobotHealth)}]"),
            F("robot_bullets", $"[{string.Join(", ", s.RobotBullets)}]"),
            F("total_damage_ally", s.TotalDamageAlly), F("total_damage_enemy", s.TotalDamageEnemy)];
    }

    private IReadOnlyList<string> BuildGlobalLogisticsStatusFields()
    {
        var s = _telemetryStore.GlobalLogisticsStatus;
        if (s is null) return ["(no data)"];
        return [F("remaining_economy", s.RemainingEconomy), F("total_economy_obtained", s.TotalEconomyObtained),
            F("tech_level", s.TechLevel), F("encryption_level", s.EncryptionLevel)];
    }

    private IReadOnlyList<string> BuildGlobalSpecialMechanismFields()
    {
        var s = _telemetryStore.GlobalSpecialMechanism;
        if (s is null) return ["(no data)"];
        return [F("mechanism_id", $"[{string.Join(", ", s.MechanismId)}]"),
            F("mechanism_time_sec", $"[{string.Join(", ", s.MechanismTimeSec)}]")];
    }

    private IReadOnlyList<string> BuildEventFields()
    {
        var s = _telemetryStore.Event;
        if (s is null) return ["(no data)"];
        return [F("event_id", s.EventId), F("param", $"\"{s.Param}\"")];
    }

    private IReadOnlyList<string> BuildRobotStaticStatusFields()
    {
        var s = _telemetryStore.RobotStaticStatus;
        if (s is null) return ["(no data)"];
        return [F("connection_state", s.ConnectionState), F("field_state", s.FieldState),
            F("alive_state", s.AliveState), F("robot_id", s.RobotId), F("robot_type", s.RobotType),
            F("performance_system_shooter", s.PerformanceSystemShooter),
            F("performance_system_chassis", s.PerformanceSystemChassis),
            F("level", s.Level), F("max_health", s.MaxHealth), F("max_heat", s.MaxHeat),
            F("heat_cooldown_rate", s.HeatCooldownRate.ToString("F2")),
            F("max_power", s.MaxPower), F("max_buffer_energy", s.MaxBufferEnergy),
            F("max_chassis_energy", s.MaxChassisEnergy)];
    }

    private IReadOnlyList<string> BuildRobotDynamicStatusFields()
    {
        var s = _telemetryStore.RobotDynamicStatus;
        if (s is null) return ["(no data)"];
        return [F("current_health", s.CurrentHealth), F("current_heat", s.CurrentHeat.ToString("F2")),
            F("last_projectile_fire_rate", s.LastProjectileFireRate.ToString("F2")),
            F("current_chassis_energy", s.CurrentChassisEnergy),
            F("current_buffer_energy", s.CurrentBufferEnergy),
            F("current_experience", s.CurrentExperience), F("experience_for_upgrade", s.ExperienceForUpgrade),
            F("total_projectiles_fired", s.TotalProjectilesFired), F("remaining_ammo", s.RemainingAmmo),
            F("is_out_of_combat", s.IsOutOfCombat), F("out_of_combat_countdown", s.OutOfCombatCountdown),
            F("can_remote_heal", s.CanRemoteHeal), F("can_remote_ammo", s.CanRemoteAmmo)];
    }

    private IReadOnlyList<string> BuildBuffFields()
    {
        var s = _telemetryStore.Buff;
        if (s is null) return ["(no data)"];
        return [F("robot_id", s.RobotId), F("buff_type", s.BuffType), F("buff_level", s.BuffLevel),
            F("buff_max_time", s.BuffMaxTime), F("buff_left_time", s.BuffLeftTime)];
    }

    private IReadOnlyList<string> BuildRadarFields()
    {
        var s = _telemetryStore.RadarInfoToClient;
        if (s is null) return ["(no data)"];
        var fields = new List<string>();
        for (var i = 0; i < s.RadarSingleRobotInfo.Count; i++)
        {
            var r = s.RadarSingleRobotInfo[i];
            fields.Add(F($"[{i}] target_pos_x", r.TargetPosX));
            fields.Add(F($"[{i}] target_pos_y", r.TargetPosY));
            fields.Add(F($"[{i}] is_high_light", r.IsHighLight));
        }
        return fields.Count == 0 ? ["(no data)"] : fields;
    }

    private static string F(string key, uint value) => $"{key}: {value}";
    private static string F(string key, int value) => $"{key}: {value}";
    private static string F(string key, bool value) => $"{key}: {value}";
    private static string F(string key, float value) => $"{key}: {value:F2}";
    private static string F(string key, ulong value) => $"{key}: {value}";
    private static string F(string key, string value) => $"{key}: {value}";
}
