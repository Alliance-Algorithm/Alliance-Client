using Alliance.Client.Features.Settings;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Telemetry;

public sealed class TelemetryStore : ObservableObject
{
    private static readonly string[] RobotSlots = ["1", "2", "3", "4", "7"];

    private readonly object _gate = new();
    private readonly int? _configuredRobotId;

    private TelemetrySnapshot _currentSnapshot;
    private GameStatus? _gameStatus;
    private GlobalUnitStatus? _globalUnitStatus;
    private RobotStaticStatus? _robotStaticStatus;
    private RobotDynamicStatus? _robotDynamicStatus;
    private ConnectionState _mqttState = ConnectionState.NotConnected;
    private ConnectionState _videoState = ConnectionState.NotConnected;
    private DateTimeOffset? _lastTelemetryAt;
    private string? _startupWarning;
    private string? _mqttNote;
    private string? _videoNote;

    public TelemetryStore(AppSettings settings)
    {
        if (!PlayerIdentity.TryResolveRobotId(settings.Mqtt.ClientId, out var robotId))
        {
            _startupWarning = $"Invalid MQTT client id '{settings.Mqtt.ClientId}'";
        }
        else
        {
            _configuredRobotId = robotId;
        }

        _currentSnapshot = BuildSnapshot();
    }

    public TelemetrySnapshot CurrentSnapshot
    {
        get => _currentSnapshot;
        private set => SetProperty(ref _currentSnapshot, value);
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

    public void SetVideoState(ConnectionState state, string? note = null)
    {
        lock (_gate)
        {
            _videoState = state;
            _videoNote = note;
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

    public void ApplyRobotStaticStatus(RobotStaticStatus status)
    {
        lock (_gate)
        {
            _robotStaticStatus = status.Clone();
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

    public void RefreshStaleness(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_lastTelemetryAt is null)
            {
                return;
            }

            if (BuildLinkState(now) != CurrentSnapshot.LinkState ||
                BuildWarningText(now) != CurrentSnapshot.WarningText ||
                BuildLastUpdateText(now) != CurrentSnapshot.LastUpdateText)
            {
                PublishSnapshotLocked(now);
            }
        }
    }

    private void MarkTelemetryReceivedLocked()
    {
        _lastTelemetryAt = DateTimeOffset.UtcNow;
        PublishSnapshotLocked(_lastTelemetryAt.Value);
    }

    private void PublishSnapshotLocked()
    {
        PublishSnapshotLocked(DateTimeOffset.UtcNow);
    }

    private void PublishSnapshotLocked(DateTimeOffset now)
    {
        CurrentSnapshot = BuildSnapshot(now);
    }

    private TelemetrySnapshot BuildSnapshot()
    {
        return BuildSnapshot(DateTimeOffset.UtcNow);
    }

    private TelemetrySnapshot BuildSnapshot(DateTimeOffset now)
    {
        return new TelemetrySnapshot
        {
            MqttState = _mqttState,
            VideoState = _videoState,
            LinkState = BuildLinkState(now),
            MatchTimeText = TelemetryText.FormatCountdown(_gameStatus?.StageCountdownSec ?? 0),
            AllyTeam = BuildTeamPanel(
                "ALLY",
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.BaseHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.OutpostHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.TotalDamageAlly),
            EnemyTeam = BuildTeamPanel(
                "ENEMY",
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.EnemyBaseHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.EnemyOutpostHealth,
                _globalUnitStatus is null ? null : (int)_globalUnitStatus.TotalDamageEnemy),
            AllyRobots = BuildRobotBars(enemyOffset: 0),
            EnemyRobots = BuildRobotBars(enemyOffset: 5),
            CurrentRobot = BuildCurrentRobotPanel(),
            LastUpdateText = BuildLastUpdateText(now),
            WarningText = BuildWarningText(now)
        };
    }

    private TeamPanelSnapshot BuildTeamPanel(string sideLabel, int? baseHealth, int? outpostHealth, int? damage)
    {
        return new TeamPanelSnapshot(
            sideLabel,
            baseHealth.HasValue ? TelemetryText.FormatStructure("Base", baseHealth.Value) : "Base --",
            outpostHealth.HasValue ? TelemetryText.FormatStructure("Outpost", outpostHealth.Value) : "Outpost --",
            damage.HasValue ? TelemetryText.FormatDamage(damage.Value) : "DMG --");
    }

    private IReadOnlyList<RobotHealthBarSnapshot> BuildRobotBars(int enemyOffset)
    {
        var values = new List<RobotHealthBarSnapshot>(RobotSlots.Length);
        for (var index = 0; index < RobotSlots.Length; index++)
        {
            var health = TryReadRobotHealth(enemyOffset + index);
            values.Add(new RobotHealthBarSnapshot(RobotSlots[index], health?.ToString() ?? "--"));
        }

        return values;
    }

    private int? TryReadRobotHealth(int index)
    {
        if (_globalUnitStatus is null || index >= _globalUnitStatus.RobotHealth.Count)
        {
            return null;
        }

        return (int)_globalUnitStatus.RobotHealth[index];
    }

    private CurrentRobotPanelSnapshot BuildCurrentRobotPanel()
    {
        var robotId = _robotStaticStatus?.RobotId is > 0
            ? (int)_robotStaticStatus.RobotId
            : _configuredRobotId;
        var currentHealth = _robotDynamicStatus is null ? (int?)null : (int)_robotDynamicStatus.CurrentHealth;
        var maxHealth = _robotStaticStatus is null ? (int?)null : (int)_robotStaticStatus.MaxHealth;
        var fireRate = _robotDynamicStatus is null ? (double?)null : _robotDynamicStatus.LastProjectileFireRate;
        var ammo = _robotDynamicStatus is null ? (int?)null : (int)_robotDynamicStatus.RemainingAmmo;

        return new CurrentRobotPanelSnapshot(
            BuildRobotLabel(robotId),
            currentHealth.HasValue || maxHealth.HasValue
                ? TelemetryText.FormatHealth(currentHealth ?? 0, maxHealth ?? 0)
                : "HP --/--",
            fireRate.HasValue ? TelemetryText.FormatFireRate(fireRate.Value) : "ROF --",
            ammo.HasValue ? TelemetryText.FormatAmmo(ammo.Value) : "AMMO --");
    }

    private static string BuildRobotLabel(int? robotId)
    {
        return robotId.HasValue ? $"Robot {robotId.Value}" : "Robot --";
    }

    private ConnectionState BuildLinkState(DateTimeOffset now)
    {
        if (_mqttState == ConnectionState.Connecting || _videoState == ConnectionState.Connecting)
        {
            return ConnectionState.Connecting;
        }

        if (_mqttState == ConnectionState.NotConnected && _videoState == ConnectionState.NotConnected)
        {
            return ConnectionState.NotConnected;
        }

        if (_mqttState == ConnectionState.Ready &&
            _videoState == ConnectionState.Ready &&
            !IsTelemetryStale(now))
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

        if (_videoState != ConnectionState.Ready)
        {
            return _videoNote ?? "Video waiting";
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
}
