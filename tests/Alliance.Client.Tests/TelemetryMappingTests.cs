using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;
using Google.Protobuf;

namespace Alliance.Client.Tests;

public sealed class TelemetryMappingTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData("101", 101)]
    [InlineData("106", 106)]
    public void PlayerIdentity_Maps_ClientId_To_RobotId(string clientId, int expectedRobotId)
    {
        Assert.True(PlayerIdentity.TryResolveRobotId(clientId, out var robotId));
        Assert.Equal(expectedRobotId, robotId);
    }

    [Fact]
    public void TelemetryStore_Maps_ProtocolMessages_Into_HudSnapshot()
    {
        var settings = CreateSettings();
        var store = new TelemetryStore(settings);

        store.SetMqttState(ConnectionState.Ready, "MQTT ready");
        store.ApplyGameStatus(new GameStatus
        {
            StageCountdownSec = 89
        });
        store.ApplyGlobalUnitStatus(new GlobalUnitStatus
        {
            BaseHealth = 1500,
            OutpostHealth = 800,
            EnemyBaseHealth = 1300,
            EnemyOutpostHealth = 620,
            TotalDamageAlly = 345,
            TotalDamageEnemy = 678,
            RobotHealth = { 100, 200, 300, 400, 700, 101, 202, 303, 404, 707 }
        });
        store.ApplyRobotStaticStatus(new RobotStaticStatus
        {
            RobotId = 1,
            MaxHealth = 300
        });
        store.ApplyRobotDynamicStatus(new RobotDynamicStatus
        {
            CurrentHealth = 250,
            LastProjectileFireRate = 23.5f,
            RemainingAmmo = 120
        });

        var snapshot = store.CurrentSnapshot;

        Assert.Equal(ConnectionState.Ready, snapshot.LinkState);
        Assert.Equal("01:29", snapshot.MatchTimeText);
        Assert.Equal("Base 1500", snapshot.AllyTeam.BaseHealthText);
        Assert.Equal("Outpost 800", snapshot.AllyTeam.OutpostHealthText);
        Assert.Equal("DMG 345", snapshot.AllyTeam.DamageText);
        Assert.Equal("Base 1300", snapshot.EnemyTeam.BaseHealthText);
        Assert.False(snapshot.AllyTeam.IsBlue);
        Assert.True(snapshot.EnemyTeam.IsBlue);
        Assert.Equal("100", snapshot.AllyRobots[0].HealthText);
        Assert.False(snapshot.AllyRobots[0].IsBlue);
        Assert.Equal("707", snapshot.EnemyRobots[4].HealthText);
        Assert.True(snapshot.EnemyRobots[0].IsBlue);
        Assert.Equal("Robot 1", snapshot.CurrentRobot.RobotLabel);
        Assert.Equal("HP 250/300", snapshot.CurrentRobot.HealthText);
        Assert.Equal("允许发弹量: 120", snapshot.CurrentRobot.AmmoText);
    }

    [Fact]
    public void ProtocolMessages_RoundTrip_ExtendedSchemaFields()
    {
        var status = new GameStatus
        {
            GameResult = 2,
            EndReason = 9
        };
        var units = new GlobalUnitStatus
        {
            RobotBullets = { 120, -5 }
        };

        var parsedStatus = GameStatus.Parser.ParseFrom(status.ToByteArray());
        var parsedUnits = GlobalUnitStatus.Parser.ParseFrom(units.ToByteArray());

        Assert.Equal((uint)2, parsedStatus.GameResult);
        Assert.Equal((uint)9, parsedStatus.EndReason);
        Assert.Equal(120, parsedUnits.RobotBullets[0]);
        Assert.Equal(-5, parsedUnits.RobotBullets[1]);
    }

    [Fact]
    public void TelemetryStore_Maps_Buffs_Events_And_Mechanisms_Into_State_And_Hud()
    {
        var store = new TelemetryStore(CreateSettings());
        store.SetMqttState(ConnectionState.Ready, "MQTT ready");

        store.ApplyBuff(new Buff
        {
            RobotId = 1,
            BuffType = 1,
            BuffLevel = 50,
            BuffMaxTime = 20,
            BuffLeftTime = 20
        });
        store.ApplyBuff(new Buff
        {
            RobotId = 1,
            BuffType = 3,
            BuffLevel = 30,
            BuffMaxTime = 9,
            BuffLeftTime = 9
        });
        store.ApplyGlobalSpecialMechanism(new GlobalSpecialMechanism
        {
            MechanismId = { 1, 2 },
            MechanismTimeSec = { 15, 9 }
        });
        store.ApplyEvent(new Event
        {
            EventId = 9,
            Param = "2,5"
        });

        var snapshot = store.CurrentSnapshot;

        Assert.Equal(2, snapshot.ActiveBuffs.Count);
        Assert.Equal("ATK+50% 20s | COOL+30 9s", snapshot.AllyRobots[0].BuffText);
        Assert.Equal("ATK+50% 20s | COOL+30 9s", snapshot.CurrentRobot.BuffText);
        Assert.Equal("飞镖命中 方2 目标5", snapshot.LatestEvent?.SummaryText);
        Assert.Equal(2, snapshot.ActiveMechanisms.Count);
        Assert.Equal("己方堡垒占领 15s", snapshot.ActiveMechanisms[0].SummaryText);
        Assert.Equal("对方堡垒占领 9s", snapshot.ActiveMechanisms[1].SummaryText);
    }

    [Fact]
    public void TelemetryStore_RefreshStaleness_CountsDown_And_Expires_TimedState()
    {
        var store = new TelemetryStore(CreateSettings());
        store.SetMqttState(ConnectionState.Ready, "MQTT ready");
        store.ApplyBuff(new Buff
        {
            RobotId = 1,
            BuffType = 4,
            BuffLevel = 20,
            BuffMaxTime = 5,
            BuffLeftTime = 5
        });
        store.ApplyGlobalSpecialMechanism(new GlobalSpecialMechanism
        {
            MechanismId = { 1 },
            MechanismTimeSec = { 4 }
        });

        var now = DateTimeOffset.UtcNow;

        store.RefreshStaleness(now.AddSeconds(3));

        Assert.Equal(2, store.CurrentSnapshot.ActiveBuffs[0].RemainingSeconds);
        Assert.Equal(1, store.CurrentSnapshot.ActiveMechanisms[0].RemainingSeconds);

        store.RefreshStaleness(now.AddSeconds(6));

        Assert.Empty(store.CurrentSnapshot.ActiveBuffs);
        Assert.Empty(store.CurrentSnapshot.ActiveMechanisms);
    }

    [Theory]
    [InlineData("1", 101, 1)]
    [InlineData("101", 1, 101)]
    public void TelemetryStore_Maps_RadarInfo_By_Client_Side(string clientId, int expectedEnemyHero, int expectedAllyHero)
    {
        var store = new TelemetryStore(CreateSettings(clientId));
        store.SetMqttState(ConnectionState.Ready, "MQTT ready");

        var radar = new RadarInfoToClient();
        for (var index = 0; index < 12; index++)
        {
            radar.RadarSingleRobotInfo.Add(new RadarSingleRobotInfo
            {
                TargetPosX = (uint)(100 + index),
                TargetPosY = (uint)(200 + index),
                IsHighLight = (uint)(index % 3)
            });
        }

        store.ApplyRadarInfoToClient(radar);

        var snapshot = store.CurrentSnapshot;

        Assert.Equal(12, snapshot.RadarRobots.Count);
        Assert.Equal(expectedEnemyHero, snapshot.RadarRobots[0].RobotId);
        Assert.Equal(expectedAllyHero, snapshot.RadarRobots[6].RobotId);
        Assert.Equal(100, snapshot.RadarRobots[0].PositionXcm);
        Assert.Equal(206, snapshot.RadarRobots[6].PositionYcm);
        Assert.True(snapshot.RadarRobots[1].IsHighlighted);
        Assert.True(snapshot.RadarRobots[2].IsOfflineHighlighted);
    }

    [Fact]
    public void TelemetryStore_RuntimeRobotId_Overrides_ClientSide_For_Team_Color_And_Radar()
    {
        var store = new TelemetryStore(CreateSettings("101"));
        store.SetMqttState(ConnectionState.Ready, "MQTT ready");

        store.ApplyGlobalUnitStatus(new GlobalUnitStatus
        {
            BaseHealth = 1600,
            EnemyBaseHealth = 1400,
            RobotHealth = { 110, 210, 310, 410, 710, 120, 220, 320, 420, 720 }
        });

        var radar = new RadarInfoToClient();
        for (var index = 0; index < 12; index++)
        {
            radar.RadarSingleRobotInfo.Add(new RadarSingleRobotInfo
            {
                TargetPosX = (uint)(100 + index),
                TargetPosY = (uint)(200 + index),
                IsHighLight = 0
            });
        }

        store.ApplyRobotStaticStatus(new RobotStaticStatus
        {
            RobotId = 1,
            MaxHealth = 300
        });
        store.ApplyRadarInfoToClient(radar);

        var snapshot = store.CurrentSnapshot;

        Assert.False(snapshot.AllyTeam.IsBlue);
        Assert.True(snapshot.EnemyTeam.IsBlue);
        Assert.False(snapshot.AllyRobots[0].IsBlue);
        Assert.True(snapshot.EnemyRobots[0].IsBlue);
        Assert.Equal(101, snapshot.RadarRobots[0].RobotId);
        Assert.Equal(1, snapshot.RadarRobots[6].RobotId);
        Assert.Equal("110", snapshot.AllyRobots[0].HealthText);
        Assert.Equal("120", snapshot.EnemyRobots[0].HealthText);
    }

    [Fact]
    public void TelemetryStore_Marks_Snapshot_As_Stale_When_Data_Stops()
    {
        var store = new TelemetryStore(CreateSettings());
        store.SetMqttState(ConnectionState.Ready, "MQTT ready");
        store.ApplyGameStatus(new GameStatus
        {
            StageCountdownSec = 10
        });

        store.RefreshStaleness(DateTimeOffset.UtcNow.AddSeconds(3));

        Assert.Equal(ConnectionState.Degraded, store.CurrentSnapshot.LinkState);
        Assert.Equal("Telemetry stale", store.CurrentSnapshot.WarningText);
    }

    private static AppSettings CreateSettings(string clientId = "1")
    {
        return new AppSettings
        {
            Mqtt = new AppSettings.MqttSettings
            {
                ClientId = clientId
            }
        };
    }
}
