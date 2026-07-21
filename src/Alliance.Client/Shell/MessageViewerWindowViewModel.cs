using System.ComponentModel;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Protocol;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Shell;

public sealed class MessageViewerWindowViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private string? _selectedTopic;
    private IReadOnlyList<string> _fields = [];

    public MessageViewerWindowViewModel(TelemetryStore telemetryStore)
    {
        _telemetryStore = telemetryStore;
        Topics = new[]
        {
            nameof(GameStatus),
            nameof(GlobalUnitStatus),
            nameof(GlobalLogisticsStatus),
            nameof(GlobalSpecialMechanism),
            nameof(Event),
            nameof(RobotStaticStatus),
            nameof(RobotDynamicStatus),
            nameof(Buff),
            nameof(RadarInfoToClient),
            nameof(CustomByteBlock)
        };
        _selectedTopic = Topics[0];
        _telemetryStore.PropertyChanged += OnTelemetryChanged;
        RefreshFields();
    }

    public IReadOnlyList<string> Topics { get; }

    public string? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (SetProperty(ref _selectedTopic, value))
            {
                RefreshFields();
            }
        }
    }

    public IReadOnlyList<string> Fields
    {
        get => _fields;
        private set => SetProperty(ref _fields, value);
    }

    private void OnTelemetryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TelemetryStore.CurrentSnapshot))
        {
            RefreshFields();
        }
    }

    private void RefreshFields()
    {
        if (_selectedTopic is null)
        {
            Fields = [];
            return;
        }

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
            nameof(CustomByteBlock) => BuildCustomByteBlockFields(),
            _ => []
        };
    }

    private IReadOnlyList<string> BuildGameStatusFields()
    {
        var s = _telemetryStore.GameStatus;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("current_round", s.CurrentRound),
            F("total_rounds", s.TotalRounds),
            F("red_score", s.RedScore),
            F("blue_score", s.BlueScore),
            F("current_stage", s.CurrentStage),
            F("stage_countdown_sec", s.StageCountdownSec),
            F("stage_elapsed_sec", s.StageElapsedSec),
            F("is_paused", s.IsPaused),
            F("game_result", s.GameResult),
            F("end_reason", s.EndReason),
        };
    }

    private IReadOnlyList<string> BuildGlobalUnitStatusFields()
    {
        var s = _telemetryStore.GlobalUnitStatus;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("base_health", s.BaseHealth),
            F("base_status", s.BaseStatus),
            F("base_shield", s.BaseShield),
            F("outpost_health", s.OutpostHealth),
            F("outpost_status", s.OutpostStatus),
            F("enemy_base_health", s.EnemyBaseHealth),
            F("enemy_base_status", s.EnemyBaseStatus),
            F("enemy_base_shield", s.EnemyBaseShield),
            F("enemy_outpost_health", s.EnemyOutpostHealth),
            F("enemy_outpost_status", s.EnemyOutpostStatus),
            F("robot_health", $"[{string.Join(", ", s.RobotHealth)}]"),
            F("robot_bullets", $"[{string.Join(", ", s.RobotBullets)}]"),
            F("total_damage_ally", s.TotalDamageAlly),
            F("total_damage_enemy", s.TotalDamageEnemy),
        };
    }

    private IReadOnlyList<string> BuildGlobalLogisticsStatusFields()
    {
        var s = _telemetryStore.GlobalLogisticsStatus;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("remaining_economy", s.RemainingEconomy),
            F("total_economy_obtained", s.TotalEconomyObtained),
            F("tech_level", s.TechLevel),
            F("encryption_level", s.EncryptionLevel),
        };
    }

    private IReadOnlyList<string> BuildGlobalSpecialMechanismFields()
    {
        var s = _telemetryStore.GlobalSpecialMechanism;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("mechanism_id", $"[{string.Join(", ", s.MechanismId)}]"),
            F("mechanism_time_sec", $"[{string.Join(", ", s.MechanismTimeSec)}]"),
        };
    }

    private IReadOnlyList<string> BuildEventFields()
    {
        var s = _telemetryStore.Event;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("event_id", s.EventId),
            F("param", $"\"{s.Param}\""),
        };
    }

    private IReadOnlyList<string> BuildRobotStaticStatusFields()
    {
        var s = _telemetryStore.RobotStaticStatus;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("connection_state", s.ConnectionState),
            F("field_state", s.FieldState),
            F("alive_state", s.AliveState),
            F("robot_id", s.RobotId),
            F("robot_type", s.RobotType),
            F("performance_system_shooter", s.PerformanceSystemShooter),
            F("performance_system_chassis", s.PerformanceSystemChassis),
            F("level", s.Level),
            F("max_health", s.MaxHealth),
            F("max_heat", s.MaxHeat),
            F("heat_cooldown_rate", s.HeatCooldownRate.ToString("F2")),
            F("max_power", s.MaxPower),
            F("max_buffer_energy", s.MaxBufferEnergy),
            F("max_chassis_energy", s.MaxChassisEnergy),
        };
    }

    private IReadOnlyList<string> BuildRobotDynamicStatusFields()
    {
        var s = _telemetryStore.RobotDynamicStatus;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("current_health", s.CurrentHealth),
            F("current_heat", s.CurrentHeat.ToString("F2")),
            F("last_projectile_fire_rate", s.LastProjectileFireRate.ToString("F2")),
            F("current_chassis_energy", s.CurrentChassisEnergy),
            F("current_buffer_energy", s.CurrentBufferEnergy),
            F("current_experience", s.CurrentExperience),
            F("experience_for_upgrade", s.ExperienceForUpgrade),
            F("total_projectiles_fired", s.TotalProjectilesFired),
            F("remaining_ammo", s.RemainingAmmo),
            F("is_out_of_combat", s.IsOutOfCombat),
            F("out_of_combat_countdown", s.OutOfCombatCountdown),
            F("can_remote_heal", s.CanRemoteHeal),
            F("can_remote_ammo", s.CanRemoteAmmo),
        };
    }

    private IReadOnlyList<string> BuildBuffFields()
    {
        var s = _telemetryStore.Buff;
        if (s is null) return NoDataFields();
        return new[]
        {
            F("robot_id", s.RobotId),
            F("buff_type", s.BuffType),
            F("buff_level", s.BuffLevel),
            F("buff_max_time", s.BuffMaxTime),
            F("buff_left_time", s.BuffLeftTime),
        };
    }

    private IReadOnlyList<string> BuildRadarFields()
    {
        var s = _telemetryStore.RadarInfoToClient;
        if (s is null) return NoDataFields();
        var fields = new List<string>();
        for (var i = 0; i < s.RadarSingleRobotInfo.Count; i++)
        {
            var r = s.RadarSingleRobotInfo[i];
            fields.Add(F($"[{i}] target_pos_x", r.TargetPosX));
            fields.Add(F($"[{i}] target_pos_y", r.TargetPosY));
            fields.Add(F($"[{i}] is_high_light", r.IsHighLight));
        }
        return fields.Count == 0 ? NoDataFields() : fields;
    }

    private IReadOnlyList<string> BuildCustomByteBlockFields()
    {
        var data = _telemetryStore.CustomByteBlockData;
        if (data is null) return NoDataFields();
        var hex = Convert.ToHexString(data);
        if (hex.Length > 200) hex = hex[..200] + "...";
        return [$"length: {data.Length} bytes", $"data (hex): {hex}"];
    }

    private static IReadOnlyList<string> NoDataFields()
    {
        return new[] { "(no data)" };
    }

    private static string F(string key, uint value) => $"{key}: {value}";
    private static string F(string key, int value) => $"{key}: {value}";
    private static string F(string key, bool value) => $"{key}: {value}";
    private static string F(string key, float value) => $"{key}: {value:F2}";
    private static string F(string key, ulong value) => $"{key}: {value}";
    private static string F(string key, string value) => $"{key}: {value}";
}
