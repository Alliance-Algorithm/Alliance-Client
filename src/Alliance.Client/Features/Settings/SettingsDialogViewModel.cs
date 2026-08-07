using System.ComponentModel;
using Alliance.Client.Features.Audio;
using Alliance.Client.Features.Hud;
using Alliance.Client.Features.ScreenRecording;
using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Features.Video;
using Alliance.Client.Infrastructure.Runtime;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Utils;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Alliance.Client.Features.Settings;

public sealed partial class SettingsDialogViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private readonly VideoStreamStore _videoStreamStore;
    private readonly AppSettings _settings;
    private readonly AppRuntimeCoordinator _runtimeCoordinator;
    private readonly HudLayoutSettings _hudLayoutSettings;
    private readonly RecordingSettings _recordingSettings;
    private readonly ScreenRecorderService _screenRecorder;
    private readonly EnemyRespawnAudioService _respawnAudio;
    private readonly SentinelAmmolessAlertService _sentinelAmmolessAlertService;
    private string _mqttStatusLabel;
    private string _linkStatusLabel;
    private string _videoStatusLabel;
    private string _lastUpdateText;
    private string? _selectedClientId;
    private bool _isBasicTab = true;
    private bool _isMessageTab;
    private bool _isDisplayTab;
    private bool _isRecordingsTab;
    private bool _isEffectsTab;
    private bool _isListeningForKey;
    private string _recordKeyDisplayText;
    private string? _selectedTopic;
    private IReadOnlyList<string> _fields = [];

    public SettingsDialogViewModel(
        TelemetryStore telemetryStore,
        VideoStreamStore videoStreamStore,
        AppSettings settings,
        AppRuntimeCoordinator runtimeCoordinator,
        HudLayoutSettings hudLayoutSettings,
        RecordingSettings recordingSettings,
        ScreenRecorderService screenRecorder,
        EnemyRespawnAudioService respawnAudio,
        SentinelAmmolessAlertService sentinelAmmolessAlertService)
    {
        _telemetryStore = telemetryStore;
        _videoStreamStore = videoStreamStore;
        _settings = settings;
        _runtimeCoordinator = runtimeCoordinator;
        _hudLayoutSettings = hudLayoutSettings;
        _recordingSettings = recordingSettings;
        _screenRecorder = screenRecorder;
        _respawnAudio = respawnAudio;
        _sentinelAmmolessAlertService = sentinelAmmolessAlertService;

        _recordKeyDisplayText = _recordingSettings.KeyBindingText;

        var snapshot = telemetryStore.CurrentSnapshot;
        _mqttStatusLabel = snapshot.MqttState.ToDisplayText();
        _linkStatusLabel = snapshot.LinkState.ToDisplayText();
        _videoStatusLabel = videoStreamStore.Snapshot.StatusText;
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
            nameof(RadarInfoToClient),
            nameof(CustomByteBlock)
        ];
        _selectedTopic = Topics[0];
        RefreshFields();

        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
        _videoStreamStore.PropertyChanged += HandleVideoChanged;
        _hudLayoutSettings.PropertyChanged += (_, _) => RaiseDisplayStateChanged();
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

    public string VideoStatusLabel
    {
        get => _videoStatusLabel;
        private set => SetProperty(ref _videoStatusLabel, value);
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
            {
                IsMessageTab = false;
                IsDisplayTab = false;
                IsRecordingsTab = false;
                IsEffectsTab = false;
            }
        }
    }

    public bool IsMessageTab
    {
        get => _isMessageTab;
        set
        {
            if (SetProperty(ref _isMessageTab, value) && value)
            {
                IsBasicTab = false;
                IsDisplayTab = false;
                IsRecordingsTab = false;
                IsEffectsTab = false;
                RefreshFields();
            }
        }
    }

    public bool IsDisplayTab
    {
        get => _isDisplayTab;
        set
        {
            if (SetProperty(ref _isDisplayTab, value) && value)
            {
                IsBasicTab = false;
                IsMessageTab = false;
                IsRecordingsTab = false;
                IsEffectsTab = false;
            }
        }
    }

    public bool IsRecordingsTab
    {
        get => _isRecordingsTab;
        set
        {
            if (SetProperty(ref _isRecordingsTab, value) && value)
            {
                IsBasicTab = false;
                IsMessageTab = false;
                IsDisplayTab = false;
                IsEffectsTab = false;
            }
        }
    }

    public bool IsEffectsTab
    {
        get => _isEffectsTab;
        set
        {
            if (SetProperty(ref _isEffectsTab, value) && value)
            {
                IsBasicTab = false;
                IsMessageTab = false;
                IsDisplayTab = false;
                IsRecordingsTab = false;
            }
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

    public string RobotTextScaleText => $"{_hudLayoutSettings.RobotTextScale:P0}";

    public string RobotWidthScaleText => $"{_hudLayoutSettings.RobotWidthScale:P0}";

    public string MatchInfoPanelBackgroundOpacityText =>
        $"{_hudLayoutSettings.MatchInfoPanelBackgroundOpacity:P0}";

    public bool IsListeningForKey
    {
        get => _isListeningForKey;
        set => SetProperty(ref _isListeningForKey, value);
    }

    public string RecordKeyDisplayText
    {
        get => _recordKeyDisplayText;
        set => SetProperty(ref _recordKeyDisplayText, value);
    }

    public int RecCrf
    {
        get => _recordingSettings.Crf;
        set
        {
            _recordingSettings.Crf = value;
            OnPropertyChanged();
        }
    }

    public int RecFrameRate
    {
        get => _recordingSettings.FrameRate;
        set
        {
            _recordingSettings.FrameRate = value;
            OnPropertyChanged();
        }
    }

    public string RecOutputDirectory
    {
        get => _recordingSettings.OutputDirectory;
        set
        {
            _recordingSettings.OutputDirectory = value;
            OnPropertyChanged();
        }
    }

    public string RecStatusText
    {
        get
        {
            var status = _screenRecorder.GetStatus();
            if (!status.IsRecording)
            {
                return "Idle";
            }

            var dur = FormatDuration(status.Duration);
            var size = FormatFileSize(status.FileSizeBytes);
            return $"Recording · {dur} · {size}";
        }
    }

    public string RecStatusError
    {
        get
        {
            var status = _screenRecorder.GetStatus();
            return status.Error ?? string.Empty;
        }
    }

    public bool HasRecStatusError => !string.IsNullOrEmpty(RecStatusError);

    public void StartKeyRebind()
    {
        IsListeningForKey = true;
        RecordKeyDisplayText = "Press a key...";
    }

    [RelayCommand]
    private void RebindKey()
    {
        StartKeyRebind();
    }

    public void OnKeyRebindCapture(KeyEventArgs e)
    {
        if (!IsListeningForKey) return;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        _recordingSettings.RecordKey = e.Key;
        _recordingSettings.RecordModifiers = e.KeyModifiers;
        RecordKeyDisplayText = _recordingSettings.KeyBindingText;
        IsListeningForKey = false;
    }

    public void RefreshRecStatus()
    {
        OnPropertyChanged(nameof(RecStatusText));
        OnPropertyChanged(nameof(RecStatusError));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
            >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
            >= 1_000 => $"{bytes / 1_000.0:F1} KB",
            _ => $"{bytes} B"
        };
    }

    [RelayCommand]
    private async Task ApplyClientIdAsync()
    {
        if (string.IsNullOrEmpty(SelectedClientId)) return;

        _settings.Mqtt.ClientId = SelectedClientId;
        await _runtimeCoordinator.RestartTelemetryAsync();
    }

    [RelayCommand]
    private void IncreaseRobotText()
    {
        if (_hudLayoutSettings.IncreaseRobotText())
        {
            RaiseDisplayStateChanged();
        }
    }

    [RelayCommand]
    private void DecreaseRobotText()
    {
        if (_hudLayoutSettings.DecreaseRobotText())
        {
            RaiseDisplayStateChanged();
        }
    }

    [RelayCommand]
    private void IncreaseRobotWidth()
    {
        if (_hudLayoutSettings.IncreaseRobotWidth())
        {
            RaiseDisplayStateChanged();
        }
    }

    [RelayCommand]
    private void DecreaseRobotWidth()
    {
        if (_hudLayoutSettings.DecreaseRobotWidth())
        {
            RaiseDisplayStateChanged();
        }
    }

    [RelayCommand]
    private void IncreaseMatchInfoPanelBackgroundOpacity()
    {
        if (_hudLayoutSettings.IncreaseMatchInfoPanelBackgroundOpacity())
        {
            RaiseDisplayStateChanged();
        }
    }

    [RelayCommand]
    private void DecreaseMatchInfoPanelBackgroundOpacity()
    {
        if (_hudLayoutSettings.DecreaseMatchInfoPanelBackgroundOpacity())
        {
            RaiseDisplayStateChanged();
        }
    }

    [RelayCommand]
    private void TriggerRespawnTest()
    {
        _respawnAudio.TestTrigger();
    }

    [RelayCommand]
    private void TriggerSentinelAmmolessTest()
    {
        _sentinelAmmolessAlertService.TestTrigger();
    }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot)) return;

        var snapshot = _telemetryStore.CurrentSnapshot;
        MqttStatusLabel = snapshot.MqttState.ToDisplayText();
        LinkStatusLabel = snapshot.LinkState.ToDisplayText();
        LastUpdateText = snapshot.LastUpdateText;
        if (IsMessageTab)
        {
            RefreshFields();
        }
    }

    private void HandleVideoChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(VideoStreamStore.Snapshot)) return;

        VideoStatusLabel = _videoStreamStore.Snapshot.StatusText;
    }

    private void RaiseDisplayStateChanged()
    {
        OnPropertyChanged(nameof(RobotTextScaleText));
        OnPropertyChanged(nameof(RobotWidthScaleText));
        OnPropertyChanged(nameof(MatchInfoPanelBackgroundOpacityText));
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
            nameof(CustomByteBlock) => BuildCustomByteBlockFields(),
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

    private IReadOnlyList<string> BuildCustomByteBlockFields()
    {
        var data = _telemetryStore.CustomByteBlockData;
        if (data is null) return ["(no data)"];
        var hex = Convert.ToHexString(data);
        if (hex.Length > 200) hex = hex[..200] + "...";
        return [$"length: {data.Length} bytes", $"data (hex): {hex}"];
    }

    private static string F(string key, uint value) => $"{key}: {value}";
    private static string F(string key, int value) => $"{key}: {value}";
    private static string F(string key, bool value) => $"{key}: {value}";
    private static string F(string key, float value) => $"{key}: {value:F2}";
    private static string F(string key, ulong value) => $"{key}: {value}";
    private static string F(string key, string value) => $"{key}: {value}";
}
