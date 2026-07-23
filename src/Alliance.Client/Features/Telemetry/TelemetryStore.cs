using System.Globalization;
using Alliance.Client.Features.RmcsImage;
using Alliance.Client.Features.Settings;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Telemetry;

public sealed class TelemetryStore : ObservableObject
{
    private static readonly string[] RobotSlots = ["1", "2", "3", "4", "7", "6"];
    private static readonly int[] VisibleRobotSlotIds = [1, 2, 3, 4, 7, 6];
    private static readonly int[] TeamHealthOrder = [1, 2, 3, 4, 7];
    private static readonly int[] RadarRelativeOrder = [1, 2, 3, 4, 6, 7];
    private static readonly int[] RedRobotIds = [1, 2, 3, 4, 6, 7];
    private static readonly int[] BlueRobotIds = [101, 102, 103, 104, 106, 107];

    private readonly object _gate = new();
    private readonly int? _configuredRobotId;
    private bool _isOwnTeamBlue;
    private readonly RmcsImageProcessor _rmcsImageProcessor;

    private TelemetrySnapshot _currentSnapshot;
    private GameStatus? _gameStatus;
    private GlobalUnitStatus? _globalUnitStatus;
    private GlobalLogisticsStatus? _globalLogisticsStatus;
    private RobotStaticStatus? _robotStaticStatus;
    private RobotDynamicStatus? _robotDynamicStatus;
    private EventState? _latestEvent;
    private List<MechanismState> _activeMechanisms = [];
    private Dictionary<BuffKey, BuffState> _activeBuffs = [];
    private List<RadarSlotState> _radarSlots = [];
    private byte[]? _customByteBlockData;
    private ConnectionState _mqttState = ConnectionState.NotConnected;
    private DateTimeOffset? _lastTelemetryAt;
    private string? _startupWarning;
    private string? _mqttNote;
    private bool _enemyAirSupportCountered;
    private DateTimeOffset? _baseAttackToastUntil;
    private readonly OutpostRebuildTracker _allyOutpostRebuild = new();
    private readonly OutpostRebuildTracker _enemyOutpostRebuild = new();

    public TelemetryStore(AppSettings settings, RmcsImageProcessor rmcsImageProcessor)
    {
        if (!PlayerIdentity.TryResolveRobotId(settings.Mqtt.ClientId, out var robotId))
        {
            _startupWarning = $"Invalid MQTT client id '{settings.Mqtt.ClientId}'";
        }
        else
        {
            _configuredRobotId = robotId;
            _isOwnTeamBlue = robotId >= 100;
        }

        _rmcsImageProcessor = rmcsImageProcessor;

        _currentSnapshot = BuildSnapshot();
    }

    public TelemetrySnapshot CurrentSnapshot
    {
        get => _currentSnapshot;
        private set => SetProperty(ref _currentSnapshot, value);
    }

    public GameStatus? GameStatus
    {
        get { lock (_gate) { return _gameStatus; } }
    }

    public GlobalUnitStatus? GlobalUnitStatus
    {
        get { lock (_gate) { return _globalUnitStatus; } }
    }

    public GlobalLogisticsStatus? GlobalLogisticsStatus
    {
        get { lock (_gate) { return _globalLogisticsStatus; } }
    }

    public RobotStaticStatus? RobotStaticStatus
    {
        get { lock (_gate) { return _robotStaticStatus; } }
    }

    public RobotDynamicStatus? RobotDynamicStatus
    {
        get { lock (_gate) { return _robotDynamicStatus; } }
    }

    public Event? Event
    {
        get
        {
            lock (_gate)
            {
                return _latestEvent is null
                    ? null
                    : new Event { EventId = _latestEvent.EventId, Param = _latestEvent.RawParam };
            }
        }
    }

    public GlobalSpecialMechanism? GlobalSpecialMechanism
    {
        get
        {
            lock (_gate)
            {
                var msg = new GlobalSpecialMechanism();
                foreach (var m in _activeMechanisms)
                {
                    msg.MechanismId.Add((uint)m.MechanismId);
                    msg.MechanismTimeSec.Add(m.InitialSeconds);
                }
                return msg;
            }
        }
    }

    public Buff? Buff
    {
        get
        {
            lock (_gate)
            {
                if (_activeBuffs.Count == 0) return null;
                var first = _activeBuffs.Values.First();
                return new Buff
                {
                    RobotId = (uint)first.RobotId,
                    BuffType = (uint)first.BuffType,
                    BuffLevel = first.BuffLevel,
                    BuffMaxTime = (uint)first.MaxSeconds,
                    BuffLeftTime = (uint)first.InitialSeconds
                };
            }
        }
    }

    public RadarInfoToClient? RadarInfoToClient
    {
        get
        {
            lock (_gate)
            {
                if (_radarSlots.Count == 0) return null;
                var msg = new RadarInfoToClient();
                foreach (var slot in _radarSlots)
                {
                    msg.RadarSingleRobotInfo.Add(new RadarSingleRobotInfo
                    {
                        TargetPosX = (uint)slot.PositionXcm,
                        TargetPosY = (uint)slot.PositionYcm,
                        IsHighLight = (uint)slot.HighlightState
                    });
                }
                return msg;
            }
        }
    }

    public byte[]? CustomByteBlockData
    {
        get { lock (_gate) { return _customByteBlockData; } }
    }

    public void SetMqttState(ConnectionState state, string? note = null)
    {
        lock (_gate)
        {
            _mqttState = state;
            _mqttNote = note;
            PublishSnapshotLocked();
        }
    }

    public void ApplyGameStatus(GameStatus status)
    {
        lock (_gate)
        {
            _gameStatus = status.Clone();
            MarkTelemetryReceivedLocked();
        }
    }

    public void ApplyGlobalUnitStatus(GlobalUnitStatus status)
    {
        lock (_gate)
        {
            _globalUnitStatus = status.Clone();
            MarkTelemetryReceivedLocked();
        }
    }

    public void ApplyGlobalLogisticsStatus(GlobalLogisticsStatus status)
    {
        lock (_gate)
        {
            _globalLogisticsStatus = status.Clone();
            MarkTelemetryReceivedLocked();
        }
    }

    public void ApplyGlobalSpecialMechanism(GlobalSpecialMechanism status)
    {
        lock (_gate)
        {
            var receivedAt = DateTimeOffset.UtcNow;
            ApplyGlobalSpecialMechanismLocked(status, receivedAt);
            MarkTelemetryReceivedLocked(receivedAt);
        }
    }

    public void ApplyEvent(Event status)
    {
        lock (_gate)
        {
            ApplyEventLocked(status, DateTimeOffset.UtcNow);
            MarkTelemetryReceivedLocked();
        }
    }

    public void ApplyRobotStaticStatus(RobotStaticStatus status)
    {
        lock (_gate)
        {
            _robotStaticStatus = status.Clone();

            if (status.RobotId > 0)
            {
                _isOwnTeamBlue = status.RobotId >= 100;
            }

            MarkTelemetryReceivedLocked();
        }
    }

    public void ApplyRobotDynamicStatus(RobotDynamicStatus status)
    {
        lock (_gate)
        {
            _robotDynamicStatus = status.Clone();
            MarkTelemetryReceivedLocked();
        }
    }

    public void ApplyBuff(Buff status)
    {
        lock (_gate)
        {
            var receivedAt = DateTimeOffset.UtcNow;
            ApplyBuffLocked(status, receivedAt);
            MarkTelemetryReceivedLocked(receivedAt);
        }
    }

    public void ApplyRadarInfoToClient(RadarInfoToClient status)
    {
        lock (_gate)
        {
            var receivedAt = DateTimeOffset.UtcNow;
            ApplyRadarInfoToClientLocked(status, receivedAt);
            MarkTelemetryReceivedLocked(receivedAt);
        }
    }

    public void ApplyCustomByteBlock(CustomByteBlock status)
    {
        var data = status.Data.ToByteArray();
        ProcessCustomByteBlockImage(data);
        ApplyCustomByteBlockData(data);
    }

    public void ProcessCustomByteBlockImage(byte[] data)
    {
        _rmcsImageProcessor.Feed(data);
    }

    public void ApplyCustomByteBlockData(byte[] data)
    {
        var storedData = data.ToArray();
        var receivedAt = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _customByteBlockData = storedData;
            _lastTelemetryAt = receivedAt;
        }

        OnPropertyChanged(nameof(CustomByteBlockData));
    }

    internal void ApplyBatch(TelemetryUpdateBatch batch)
    {
        if (!batch.HasUpdates)
        {
            return;
        }

        lock (_gate)
        {
            if (batch.GameStatus is not null)
            {
                _gameStatus = batch.GameStatus.Clone();
            }

            if (batch.GlobalUnitStatus is not null)
            {
                _globalUnitStatus = batch.GlobalUnitStatus.Clone();
            }

            if (batch.GlobalLogisticsStatus is not null)
            {
                _globalLogisticsStatus = batch.GlobalLogisticsStatus.Clone();
            }

            if (batch.GlobalSpecialMechanism is not null)
            {
                ApplyGlobalSpecialMechanismLocked(batch.GlobalSpecialMechanism, batch.ReceivedAt);
            }

            foreach (var status in batch.Events)
            {
                ApplyEventLocked(status, batch.ReceivedAt);
            }

            if (batch.RobotStaticStatus is not null)
            {
                _robotStaticStatus = batch.RobotStaticStatus.Clone();

                if (batch.RobotStaticStatus.RobotId > 0)
                {
                    _isOwnTeamBlue = batch.RobotStaticStatus.RobotId >= 100;
                }
            }

            if (batch.RobotDynamicStatus is not null)
            {
                _robotDynamicStatus = batch.RobotDynamicStatus.Clone();
            }

            foreach (var status in batch.Buffs)
            {
                ApplyBuffLocked(status, batch.ReceivedAt);
            }

            if (batch.RadarInfoToClient is not null)
            {
                ApplyRadarInfoToClientLocked(batch.RadarInfoToClient, batch.ReceivedAt);
            }

            MarkTelemetryReceivedLocked(batch.ReceivedAt);
        }
    }

    private void ApplyEventLocked(Event status, DateTimeOffset receivedAt)
    {
        _latestEvent = new EventState(
            status.EventId,
            status.Param,
            BuildEventSummary(status.EventId, status.Param));

        switch (status.EventId)
        {
            case 7:
                _enemyAirSupportCountered = false;
                break;
            case 8:
                _enemyAirSupportCountered = true;
                break;
            case 11:
                _baseAttackToastUntil = receivedAt.AddSeconds(5);
                break;
        }
    }

    private void ApplyGlobalSpecialMechanismLocked(GlobalSpecialMechanism status, DateTimeOffset receivedAt)
    {
        var count = Math.Min(status.MechanismId.Count, status.MechanismTimeSec.Count);
        _activeMechanisms = new List<MechanismState>(count);
        for (var index = 0; index < count; index++)
        {
            _activeMechanisms.Add(new MechanismState(
                (int)status.MechanismId[index],
                status.MechanismTimeSec[index],
                receivedAt));
        }
    }

    private void ApplyBuffLocked(Buff status, DateTimeOffset receivedAt)
    {
        var key = new BuffKey((int)status.RobotId, (int)status.BuffType);
        if (status.BuffLeftTime == 0)
        {
            _activeBuffs.Remove(key);
        }
        else
        {
            _activeBuffs[key] = new BuffState(
                (int)status.RobotId,
                (int)status.BuffType,
                status.BuffLevel,
                (int)status.BuffMaxTime,
                (int)status.BuffLeftTime,
                receivedAt);
        }
    }

    private void ApplyRadarInfoToClientLocked(RadarInfoToClient status, DateTimeOffset receivedAt)
    {
        _radarSlots = new List<RadarSlotState>(status.RadarSingleRobotInfo.Count);
        for (var index = 0; index < status.RadarSingleRobotInfo.Count && index < 12; index++)
        {
            var info = status.RadarSingleRobotInfo[index];
            _radarSlots.Add(new RadarSlotState(
                index,
                (int)info.TargetPosX,
                (int)info.TargetPosY,
                (int)info.IsHighLight,
                receivedAt));
        }
    }

    public void RefreshStaleness(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_lastTelemetryAt is null)
            {
                return;
            }

            RemoveExpiredStateLocked(now);
            UpdateOutpostRebuildTrackersLocked(now);

            var nextBuffs = BuildActiveBuffSnapshots(now);
            var nextMechanisms = BuildMechanismSnapshots(now);
            var showToast = IsBaseAttackToastVisible(now);
            var shouldPublish = BuildLinkState(now) != CurrentSnapshot.LinkState ||
                                BuildWarningText(now) != CurrentSnapshot.WarningText ||
                                BuildLastUpdateText(now) != CurrentSnapshot.LastUpdateText ||
                                showToast != CurrentSnapshot.ShowBaseAttackToast ||
                                !CurrentSnapshot.ActiveBuffs.SequenceEqual(nextBuffs) ||
                                !CurrentSnapshot.ActiveMechanisms.SequenceEqual(nextMechanisms) ||
                                _allyOutpostRebuild.IsDirty ||
                                _enemyOutpostRebuild.IsDirty;

            if (shouldPublish)
            {
                _allyOutpostRebuild.IsDirty = false;
                _enemyOutpostRebuild.IsDirty = false;
                PublishSnapshotLocked(now, nextBuffs, nextMechanisms);
            }
        }
    }

    private void MarkTelemetryReceivedLocked()
    {
        MarkTelemetryReceivedLocked(DateTimeOffset.UtcNow);
    }

    private void MarkTelemetryReceivedLocked(DateTimeOffset receivedAt)
    {
        _lastTelemetryAt = receivedAt;
        PublishSnapshotLocked(receivedAt);
    }

    private void PublishSnapshotLocked()
    {
        PublishSnapshotLocked(DateTimeOffset.UtcNow);
    }

    private void PublishSnapshotLocked(DateTimeOffset now)
    {
        UpdateOutpostRebuildTrackersLocked(now);
        PublishSnapshotLocked(now, BuildActiveBuffSnapshots(now), BuildMechanismSnapshots(now));
    }

    private void PublishSnapshotLocked(
        DateTimeOffset now,
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs,
        IReadOnlyList<SpecialMechanismTelemetrySnapshot> activeMechanisms)
    {
        CurrentSnapshot = BuildSnapshot(now, activeBuffs, activeMechanisms);
    }

    private TelemetrySnapshot BuildSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        UpdateOutpostRebuildTrackersLocked(now);
        return BuildSnapshot(now, BuildActiveBuffSnapshots(now), BuildMechanismSnapshots(now));
    }

    private TelemetrySnapshot BuildSnapshot(
        DateTimeOffset now,
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs,
        IReadOnlyList<SpecialMechanismTelemetrySnapshot> activeMechanisms)
    {
        var radarRobots = BuildRadarSnapshots();

        return new TelemetrySnapshot
        {
            MqttState = _mqttState,
            LinkState = BuildLinkState(now),
            CurrentRound = _gameStatus is null ? null : (int)_gameStatus.CurrentRound,
            TotalRounds = _gameStatus is null ? null : (int)_gameStatus.TotalRounds,
            RedScore = _gameStatus is null ? null : (int)_gameStatus.RedScore,
            BlueScore = _gameStatus is null ? null : (int)_gameStatus.BlueScore,
            MatchTimeText = TelemetryText.FormatCountdown(_gameStatus?.StageCountdownSec ?? 0),
            StageText = BuildStageText(),
            AllyTeam = BuildTeamPanel(
                "ALLY",
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.BaseHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.BaseShield,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.OutpostHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.TotalDamageAlly,
                isBlue: _isOwnTeamBlue),
            EnemyTeam = BuildTeamPanel(
                "ENEMY",
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.EnemyBaseHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.EnemyBaseShield,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.EnemyOutpostHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.TotalDamageEnemy,
                isEnemy: true,
                isBlue: !_isOwnTeamBlue),
            AllyRobots = BuildRobotBars(isAllyTeam: true, activeBuffs, radarRobots),
            EnemyRobots = BuildRobotBars(isAllyTeam: false, activeBuffs, radarRobots),
            CurrentRobot = BuildCurrentRobotPanel(),
            ReversePanel = BuildReversePanel(now, activeBuffs),
            AllySideAlerts = BuildSideAlerts(isEnemy: false, now, activeMechanisms),
            EnemySideAlerts = BuildSideAlerts(isEnemy: true, now, activeMechanisms),
            ShowBaseAttackToast = IsBaseAttackToastVisible(now),
            LatestEvent = _latestEvent is null
                ? null
                : new EventTelemetrySnapshot(_latestEvent.EventId, _latestEvent.RawParam, _latestEvent.SummaryText),
            ActiveMechanisms = activeMechanisms,
            RadarRobots = radarRobots,
            ActiveBuffs = activeBuffs,
            LastUpdateText = BuildLastUpdateText(now),
            WarningText = BuildWarningText(now)
        };
    }

    private TeamPanelSnapshot BuildTeamPanel(
        string sideLabel,
        int? baseHealth,
        int? baseShield,
        int? outpostHealth,
        int? damage,
        bool isEnemy = false,
        bool isBlue = true)
    {
        var remainingEconomy = _globalLogisticsStatus is null ? (int?)null : (int)_globalLogisticsStatus.RemainingEconomy;
        var totalEconomy = _globalLogisticsStatus is null ? (long?)null : (long)_globalLogisticsStatus.TotalEconomyObtained;

        return new TeamPanelSnapshot(
            sideLabel,
            baseHealth.HasValue ? TelemetryText.FormatStructure("Base", baseHealth.Value) : "Base --",
            outpostHealth.HasValue ? TelemetryText.FormatStructure("Outpost", outpostHealth.Value) : "Outpost --",
            damage.HasValue ? TelemetryText.FormatDamage(damage.Value) : "DMG --",
            FormatEconomy(remainingEconomy, totalEconomy),
            baseHealth,
            baseShield,
            outpostHealth,
            damage,
            remainingEconomy,
            totalEconomy,
            isEnemy,
            isBlue);
    }

    private static string FormatEconomy(int? remaining, long? total)
    {
        if (remaining is null && total is null)
        {
            return "ECO --";
        }

        var remainingText = remaining.HasValue ? remaining.Value.ToString() : "--";
        var totalText = total.HasValue ? total.Value.ToString("N0", CultureInfo.InvariantCulture) : "--";
        return $"ECO {remainingText}/{totalText}";
    }

    private string BuildStageText()
    {
        if (_gameStatus is null)
        {
            return "--";
        }

        return _gameStatus.CurrentStage switch
        {
            0 => "NOT STARTED",
            1 => "PREPARING",
            2 => "SELF-TEST",
            3 => "COUNTDOWN",
            4 => "IN MATCH",
            5 => "SETTLING",
            _ => $"STAGE {_gameStatus.CurrentStage}"
        };
    }

    private IReadOnlyList<RobotStatusSnapshot> BuildRobotBars(
        bool isAllyTeam,
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs,
        IReadOnlyList<RadarRobotTelemetrySnapshot> radarRobots)
    {
        var values = new List<RobotStatusSnapshot>(RobotSlots.Length);
        for (var index = 0; index < RobotSlots.Length; index++)
        {
            var slotLabel = RobotSlots[index];
            var relativeRobotId = VisibleRobotSlotIds[index];
            var health = TryReadRobotHealth(isAllyTeam, relativeRobotId);
            var bullets = TryReadRobotBullets(isAllyTeam, relativeRobotId);
            var absoluteRobotId = ResolveTeamRobotId(relativeRobotId, isAllyTeam);
            var showHealthBar = relativeRobotId != 6;
            var isAerial = relativeRobotId == 6;
            var isAlive = isAerial || !health.HasValue || health.Value > 0;
            var buffLabels = BuildRobotBuffLabels(activeBuffs, absoluteRobotId, maxEntries: 2);
            var isRadarLocked = !isAllyTeam && radarRobots.Any(r => r.RobotId == absoluteRobotId && r.IsHighlighted);

            values.Add(new RobotStatusSnapshot(
                slotLabel,
                health?.ToString(CultureInfo.InvariantCulture) ?? "--",
                bullets?.ToString(CultureInfo.InvariantCulture) ?? "--",
                BuildRobotBuffSummary(buffLabels),
                health,
                500,
                bullets,
                showHealthBar,
                IsEnemy: !isAllyTeam,
                IsBlue: isAllyTeam ? _isOwnTeamBlue : !_isOwnTeamBlue,
                RobotTypeText: BuildRobotTypeText(relativeRobotId),
                HealthDisplayText: health?.ToString(CultureInfo.InvariantCulture) ?? "--",
                AmmoDisplayText: bullets.HasValue
                    ? $"弹 {bullets.Value.ToString(CultureInfo.InvariantCulture)}"
                    : "弹 --",
                IsAlive: isAlive,
                IsAerial: isAerial,
                IsRadarLocked: isRadarLocked,
                IsAirSupportCountered: !isAllyTeam && isAerial && _enemyAirSupportCountered,
                BuffLabels: buffLabels));
        }

        return values;
    }

    private int? TryReadRobotHealth(bool isAllyTeam, int relativeRobotId)
    {
        if (_globalUnitStatus is null || relativeRobotId == 6)
        {
            return null;
        }

        var baseIndex = isAllyTeam ? 0 : TeamHealthOrder.Length;
        var relativeIndex = Array.IndexOf(TeamHealthOrder, relativeRobotId);
        if (relativeIndex < 0)
        {
            return null;
        }

        var index = baseIndex + relativeIndex;
        if (index >= _globalUnitStatus.RobotHealth.Count)
        {
            return null;
        }

        return (int)_globalUnitStatus.RobotHealth[index];
    }

    private int? TryReadRobotBullets(bool isAllyTeam, int relativeRobotId)
    {
        if (_globalUnitStatus is null || !isAllyTeam)
        {
            return null;
        }

        var index = Array.IndexOf(VisibleRobotSlotIds, relativeRobotId);
        if (index < 0 || index >= _globalUnitStatus.RobotBullets.Count)
        {
            return null;
        }

        return _globalUnitStatus.RobotBullets[index];
    }

    private CurrentRobotPanelSnapshot BuildCurrentRobotPanel()
    {
        var robotId = ResolveCurrentRobotId();
        var currentHealth = _robotDynamicStatus is null ? (int?)null : (int)_robotDynamicStatus.CurrentHealth;
        var maxHealth = _robotStaticStatus is null ? (int?)null : (int)_robotStaticStatus.MaxHealth;
        var level = _robotStaticStatus is null ? (int?)null : (int)_robotStaticStatus.Level;
        var expForUpgrade = _robotDynamicStatus is null ? (int?)null : (int)_robotDynamicStatus.ExperienceForUpgrade;
        var remainingAmmo = _robotDynamicStatus is null ? (int?)null : (int)_robotDynamicStatus.RemainingAmmo;

        return new CurrentRobotPanelSnapshot(
            BuildRobotDisplayName(robotId),
            currentHealth.HasValue || maxHealth.HasValue
                ? TelemetryText.FormatHealth(currentHealth ?? 0, maxHealth ?? 0)
                : "HP --/--",
            BuildPerformanceText(
                _robotStaticStatus is null ? null : (int)_robotStaticStatus.PerformanceSystemShooter,
                _robotStaticStatus is null ? null : (int)_robotStaticStatus.PerformanceSystemChassis),
            currentHealth,
            maxHealth,
            level,
            expForUpgrade,
            remainingAmmo);
    }

    private ReversePanelSnapshot BuildReversePanel(
        DateTimeOffset now,
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs)
    {
        var robotId = ResolveCurrentRobotId();
        var buffLines = robotId.HasValue
            ? BuildReverseBuffLines(activeBuffs, robotId.Value)
            : [];

        return new ReversePanelSnapshot(
            buffLines,
            _robotDynamicStatus?.LastProjectileFireRate,
            _robotStaticStatus?.HeatCooldownRate,
            _robotDynamicStatus?.CurrentHeat,
            _robotStaticStatus is null ? null : (int)_robotStaticStatus.MaxHeat,
            _robotDynamicStatus is null ? null : (int)_robotDynamicStatus.CurrentChassisEnergy,
            _robotStaticStatus is null ? null : (int)_robotStaticStatus.MaxChassisEnergy);
    }

    private int? ResolveCurrentRobotId()
    {
        return _robotStaticStatus?.RobotId is > 0
            ? (int)_robotStaticStatus.RobotId
            : _configuredRobotId;
    }

    private static string BuildRobotDisplayName(int? robotId)
    {
        if (!robotId.HasValue)
        {
            return "机器人 --";
        }

        var id = robotId.Value;
        var isBlue = id >= 100;
        var relativeId = isBlue ? id - 100 : id;
        var side = isBlue ? "蓝方" : "红方";
        return $"{side}{relativeId}号-{BuildRobotTypeText(relativeId)}";
    }

    private static string BuildPerformanceText(int? shooter, int? chassis)
    {
        var shooterText = FormatShooterPerformance(shooter);
        var chassisText = FormatChassisPerformance(chassis);
        if (shooterText is null && chassisText is null)
        {
            return "性能 --";
        }

        return $"{shooterText ?? "--"} / {chassisText ?? "--"}";
    }

    private static string? FormatShooterPerformance(int? value)
    {
        return value switch
        {
            1 => "冷却优先",
            2 => "爆发优先",
            3 => "英雄近战",
            4 => "英雄远程",
            _ => null
        };
    }

    private static string? FormatChassisPerformance(int? value)
    {
        return value switch
        {
            1 => "血量优先",
            2 => "功率优先",
            3 => "英雄近战",
            4 => "英雄远程",
            _ => null
        };
    }

    private static IReadOnlyList<ReverseBuffLineSnapshot> BuildReverseBuffLines(
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs,
        int robotId)
    {
        return activeBuffs
            .Where(buff => buff.RobotId == robotId && IsDisplayBuffType(buff.BuffType))
            .OrderBy(buff => buff.BuffType)
            .Select(buff =>
            {
                var typeLabel = GetBuffTypeLabel(buff.BuffType);
                var valueText = FormatReverseBuffValue(buff.BuffType, buff.BuffLevel);
                return new ReverseBuffLineSnapshot(
                    buff.BuffType,
                    typeLabel,
                    valueText,
                    buff.RemainingSeconds,
                    $"【{typeLabel}】{valueText}（{buff.RemainingSeconds}s）");
            })
            .ToArray();
    }

    private static bool IsDisplayBuffType(int buffType) => buffType is 1 or 2 or 3 or 5;

    private static string GetBuffTypeLabel(int buffType)
    {
        return buffType switch
        {
            1 => "攻击",
            2 => "防御",
            3 => "冷却",
            5 => "回血",
            _ => $"类型{buffType}"
        };
    }

    private static string FormatReverseBuffValue(int buffType, int buffLevel)
    {
        var sign = buffLevel > 0 ? "+" : string.Empty;
        return buffType switch
        {
            1 or 2 or 5 => $"{sign}{buffLevel}%",
            3 => $"{sign}{buffLevel}",
            _ => $"{sign}{buffLevel}"
        };
    }

    private IReadOnlyList<SideAlertSnapshot> BuildSideAlerts(
        bool isEnemy,
        DateTimeOffset now,
        IReadOnlyList<SpecialMechanismTelemetrySnapshot> activeMechanisms)
    {
        var alerts = new List<SideAlertSnapshot>(3);
        var isBlue = isEnemy ? !_isOwnTeamBlue : _isOwnTeamBlue;
        var tracker = isEnemy ? _enemyOutpostRebuild : _allyOutpostRebuild;

        if (tracker.ShowRebuildable)
        {
            alerts.Add(new SideAlertSnapshot(
                SideAlertKind.OutpostRebuildable,
                isEnemy ? "对方前哨站可重建" : "己方前哨站可重建",
                Progress: 0,
                IsEnemy: isEnemy,
                IsBlue: isBlue));
        }

        if (tracker.ShowRebuilding)
        {
            var progress = Math.Clamp(tracker.RebuildElapsedSeconds / 10d, 0, 1);
            alerts.Add(new SideAlertSnapshot(
                SideAlertKind.OutpostRebuilding,
                isEnemy ? "对方前哨站重建中" : "己方前哨站重建中",
                progress,
                ShowStageMark: true,
                StageMarkProgress: 0.5,
                IsEnemy: isEnemy,
                IsBlue: isBlue));
        }

        var mechanismId = isEnemy ? 2 : 1;
        var mechanism = activeMechanisms.FirstOrDefault(m => m.MechanismId == mechanismId);
        if (mechanism is not null)
        {
            var progress = Math.Clamp(1d - mechanism.RemainingSeconds / 20d, 0, 1);
            alerts.Add(new SideAlertSnapshot(
                SideAlertKind.FortressCapture,
                isEnemy ? "对方堡垒正在被占领" : "己方堡垒正在被占领",
                progress,
                IsEnemy: isEnemy,
                IsBlue: isBlue));
        }

        return alerts;
    }

    private bool IsBaseAttackToastVisible(DateTimeOffset now)
    {
        return _baseAttackToastUntil.HasValue && now < _baseAttackToastUntil.Value;
    }

    private void UpdateOutpostRebuildTrackersLocked(DateTimeOffset now)
    {
        var allyStatus = _globalUnitStatus is null ? (int?)null : (int)_globalUnitStatus.OutpostStatus;
        var enemyStatus = _globalUnitStatus is null ? (int?)null : (int)_globalUnitStatus.EnemyOutpostStatus;
        _allyOutpostRebuild.Update(allyStatus, now);
        _enemyOutpostRebuild.Update(enemyStatus, now);
    }

    private ConnectionState BuildLinkState(DateTimeOffset now)
    {
        if (_mqttState == ConnectionState.Connecting)
        {
            return ConnectionState.Connecting;
        }

        if (_mqttState == ConnectionState.NotConnected)
        {
            return ConnectionState.NotConnected;
        }

        if (_mqttState == ConnectionState.Ready && !IsTelemetryStale(now))
        {
            return ConnectionState.Ready;
        }

        return ConnectionState.Degraded;
    }

    private string BuildWarningText(DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(_startupWarning))
        {
            return _startupWarning!;
        }

        if (_mqttState != ConnectionState.Ready)
        {
            return _mqttNote ?? "MQTT offline";
        }

        if (IsTelemetryStale(now))
        {
            return "Telemetry stale";
        }

        return "Telemetry live";
    }

    private string BuildLastUpdateText(DateTimeOffset now)
    {
        if (_lastTelemetryAt is null)
        {
            return "Awaiting MQTT packets";
        }

        var age = now - _lastTelemetryAt.Value;
        return $"Last telemetry {age.TotalSeconds:0.0}s ago";
    }

    private bool IsTelemetryStale(DateTimeOffset now)
    {
        return _lastTelemetryAt.HasValue && now - _lastTelemetryAt.Value > TimeSpan.FromSeconds(2);
    }

    private void RemoveExpiredStateLocked(DateTimeOffset now)
    {
        _activeMechanisms = _activeMechanisms
            .Where(mechanism => GetRemainingSeconds(now, mechanism.InitialSeconds, mechanism.ReceivedAt) > 0)
            .ToList();

        _activeBuffs = _activeBuffs
            .Where(entry => GetRemainingSeconds(now, entry.Value.InitialSeconds, entry.Value.ReceivedAt) > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        if (_baseAttackToastUntil.HasValue && now >= _baseAttackToastUntil.Value)
        {
            _baseAttackToastUntil = null;
        }
    }

    private IReadOnlyList<RobotBuffTelemetrySnapshot> BuildActiveBuffSnapshots(DateTimeOffset now)
    {
        return _activeBuffs.Values
            .Select(buff => BuildBuffSnapshot(now, buff))
            .Where(snapshot => snapshot is not null)
            .Cast<RobotBuffTelemetrySnapshot>()
            .OrderBy(snapshot => snapshot.RobotId)
            .ThenBy(snapshot => snapshot.BuffType)
            .ToArray();
    }

    private RobotBuffTelemetrySnapshot? BuildBuffSnapshot(DateTimeOffset now, BuffState buff)
    {
        var remainingSeconds = GetRemainingSeconds(now, buff.InitialSeconds, buff.ReceivedAt);
        if (remainingSeconds <= 0)
        {
            return null;
        }

        return new RobotBuffTelemetrySnapshot(
            buff.RobotId,
            buff.BuffType,
            buff.BuffLevel,
            buff.MaxSeconds,
            remainingSeconds,
            BuildBuffSummaryText(buff.BuffType, buff.BuffLevel, remainingSeconds));
    }

    private IReadOnlyList<SpecialMechanismTelemetrySnapshot> BuildMechanismSnapshots(DateTimeOffset now)
    {
        return _activeMechanisms
            .Select(mechanism => BuildMechanismSnapshot(now, mechanism))
            .Where(snapshot => snapshot is not null)
            .Cast<SpecialMechanismTelemetrySnapshot>()
            .OrderBy(snapshot => snapshot.MechanismId)
            .ToArray();
    }

    private SpecialMechanismTelemetrySnapshot? BuildMechanismSnapshot(DateTimeOffset now, MechanismState mechanism)
    {
        var remainingSeconds = GetRemainingSeconds(now, mechanism.InitialSeconds, mechanism.ReceivedAt);
        if (remainingSeconds <= 0)
        {
            return null;
        }

        return new SpecialMechanismTelemetrySnapshot(
            mechanism.MechanismId,
            remainingSeconds,
            BuildMechanismSummary(mechanism.MechanismId, remainingSeconds));
    }

    private IReadOnlyList<RadarRobotTelemetrySnapshot> BuildRadarSnapshots()
    {
        if (_radarSlots.Count == 0)
        {
            return [];
        }

        var enemyIds = _isOwnTeamBlue ? RedRobotIds : BlueRobotIds;
        var allyIds = _isOwnTeamBlue ? BlueRobotIds : RedRobotIds;
        var results = new List<RadarRobotTelemetrySnapshot>(_radarSlots.Count);

        foreach (var slot in _radarSlots)
        {
            if (slot.SlotIndex >= 12)
            {
                continue;
            }

            var isEnemy = slot.SlotIndex < RadarRelativeOrder.Length;
            var relativeIndex = slot.SlotIndex % RadarRelativeOrder.Length;
            var robotId = isEnemy ? enemyIds[relativeIndex] : allyIds[relativeIndex];

            results.Add(new RadarRobotTelemetrySnapshot(
                robotId,
                slot.PositionXcm,
                slot.PositionYcm,
                slot.HighlightState,
                slot.HighlightState == 1 || slot.HighlightState == 2,
                slot.HighlightState == 2));
        }

        return results;
    }

    private int ResolveTeamRobotId(int relativeRobotId, bool isAllyTeam)
    {
        return ResolveTeamRobotIds(isAllyTeam).TryGetValue(relativeRobotId, out var absoluteRobotId)
            ? absoluteRobotId
            : relativeRobotId;
    }

    private IReadOnlyDictionary<int, int> ResolveTeamRobotIds(bool isAllyTeam)
    {
        var teamIds = isAllyTeam
            ? (_isOwnTeamBlue ? BlueRobotIds : RedRobotIds)
            : (_isOwnTeamBlue ? RedRobotIds : BlueRobotIds);

        return new Dictionary<int, int>
        {
            [1] = teamIds[0],
            [2] = teamIds[1],
            [3] = teamIds[2],
            [4] = teamIds[3],
            [6] = teamIds[4],
            [7] = teamIds[5]
        };
    }

    private static int GetRemainingSeconds(DateTimeOffset now, int initialSeconds, DateTimeOffset receivedAt)
    {
        var elapsed = (int)Math.Floor((now - receivedAt).TotalSeconds);
        return Math.Max(0, initialSeconds - elapsed);
    }

    private static string BuildEventSummary(int eventId, string rawParam)
    {
        var values = ParseIntList(rawParam);

        return eventId switch
        {
            1 when values.Count >= 2 => $"击杀 {values[1]}->{values[0]}",
            2 when values.Count >= 1 => $"前哨站摧毁 {values[0]}",
            3 => string.IsNullOrWhiteSpace(rawParam) ? "大能量机关激活" : $"大能量机关 {rawParam}",
            4 when values.Count >= 1 => $"能量机关激活 {values[0]}",
            5 when values.Count >= 1 => $"己方英雄狙击 {values[0]}",
            6 when values.Count >= 1 => $"对方英雄狙击 {values[0]}",
            7 => "对方呼叫空中支援",
            8 when values.Count >= 1 => $"对方空支被反制 {values[0]}",
            9 when values.Count >= 2 => $"飞镖命中 方{values[0]} 目标{values[1]}",
            10 => "对方飞镖闸门开启",
            11 => "基地遭到攻击",
            12 => "对方前哨站停转",
            13 => "对方基地护甲展开",
            14 => "对方请求四级装配",
            15 => $"装配结果 {BuildAssemblyResultText(values.FirstOrDefault())}",
            _ => $"事件 {eventId}"
        };
    }

    private static string BuildAssemblyResultText(int resultCode)
    {
        return resultCode switch
        {
            0 => "成功",
            1 => "拔出",
            2 => "超时",
            3 => "离开装配区过久",
            4 => "工程战亡",
            5 => "四级协作超时",
            6 => "主动退出",
            7 => "结算未检测到能量单元",
            8 => "缓冲期到期",
            _ => resultCode.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyList<int> ParseIntList(string rawParam)
    {
        if (string.IsNullOrWhiteSpace(rawParam))
        {
            return [];
        }

        var values = new List<int>();
        foreach (var segment in rawParam.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string BuildMechanismSummary(int mechanismId, int remainingSeconds)
    {
        return mechanismId switch
        {
            1 => $"己方堡垒占领 {remainingSeconds}s",
            2 => $"对方堡垒占领 {remainingSeconds}s",
            _ => $"机制 {mechanismId} {remainingSeconds}s"
        };
    }

    private static string BuildBuffSummaryText(int buffType, int buffLevel, int remainingSeconds)
    {
        var label = buffType switch
        {
            1 => $"ATK+{buffLevel}%",
            2 when buffLevel >= 0 => $"DEF+{buffLevel}%",
            2 => $"VUL{buffLevel}%",
            3 => $"COOL+{buffLevel}",
            4 => $"PWR+{buffLevel}%",
            5 => $"HEAL+{buffLevel}%",
            6 => $"AMMO+{buffLevel}",
            7 => $"MOVE+{buffLevel}",
            _ => $"BUFF{buffType}:{buffLevel}"
        };

        return $"{label} {remainingSeconds}s";
    }

    private static string BuildRobotTypeText(int relativeRobotId)
    {
        return relativeRobotId switch
        {
            1 => "英雄",
            2 => "工程",
            3 => "步兵",
            4 => "步兵",
            6 => "无人机",
            7 => "哨兵",
            _ => "机器人"
        };
    }

    private static string BuildRobotBuffSummary(
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs,
        int robotId,
        int maxEntries)
    {
        return BuildRobotBuffSummary(BuildRobotBuffLabels(activeBuffs, robotId, maxEntries));
    }

    private static string BuildRobotBuffSummary(IReadOnlyList<string> labels)
    {
        return labels.Count == 0 ? "BUFF --" : string.Join(" | ", labels);
    }

    private static IReadOnlyList<string> BuildRobotBuffLabels(
        IReadOnlyList<RobotBuffTelemetrySnapshot> activeBuffs,
        int robotId,
        int maxEntries)
    {
        var summaries = activeBuffs
            .Where(buff => buff.RobotId == robotId)
            .OrderBy(buff => buff.BuffType)
            .Take(maxEntries)
            .Select(buff => buff.SummaryText)
            .ToArray();

        return summaries;
    }

    private sealed record EventState(int EventId, string RawParam, string SummaryText);

    private sealed record MechanismState(int MechanismId, int InitialSeconds, DateTimeOffset ReceivedAt);

    private sealed record BuffKey(int RobotId, int BuffType);

    private sealed record BuffState(
        int RobotId,
        int BuffType,
        int BuffLevel,
        int MaxSeconds,
        int InitialSeconds,
        DateTimeOffset ReceivedAt);

    private sealed class OutpostRebuildTracker
    {
        private DateTimeOffset? _rebuildStartedAt;
        private DateTimeOffset? _leftRebuildingAt;
        private bool _showRebuildable;
        private bool _showRebuilding;
        private double _rebuildElapsedSeconds;

        public bool ShowRebuildable => _showRebuildable;
        public bool ShowRebuilding => _showRebuilding;
        public double RebuildElapsedSeconds => _rebuildElapsedSeconds;
        public bool IsDirty { get; set; }

        public void Update(int? status, DateTimeOffset now)
        {
            var prevRebuildable = _showRebuildable;
            var prevRebuilding = _showRebuilding;
            var prevElapsed = _rebuildElapsedSeconds;

            _showRebuildable = status == 4;

            if (status == 5)
            {
                _leftRebuildingAt = null;
                _rebuildStartedAt ??= now;
                _showRebuilding = true;
                _rebuildElapsedSeconds = Math.Min(10d, (now - _rebuildStartedAt.Value).TotalSeconds);
            }
            else
            {
                if (_showRebuilding || _rebuildStartedAt.HasValue)
                {
                    _leftRebuildingAt ??= now;
                    if ((now - _leftRebuildingAt.Value).TotalSeconds >= 1)
                    {
                        _rebuildStartedAt = null;
                        _leftRebuildingAt = null;
                        _showRebuilding = false;
                        _rebuildElapsedSeconds = 0;
                    }
                    else if (_rebuildStartedAt.HasValue)
                    {
                        _rebuildElapsedSeconds = Math.Min(10d, (now - _rebuildStartedAt.Value).TotalSeconds);
                    }
                }
                else
                {
                    _showRebuilding = false;
                    _rebuildElapsedSeconds = 0;
                }
            }

            if (prevRebuildable != _showRebuildable ||
                prevRebuilding != _showRebuilding ||
                Math.Abs(prevElapsed - _rebuildElapsedSeconds) > 0.05)
            {
                IsDirty = true;
            }
        }
    }

    private sealed record RadarSlotState(
        int SlotIndex,
        int PositionXcm,
        int PositionYcm,
        int HighlightState,
        DateTimeOffset ReceivedAt);
}
