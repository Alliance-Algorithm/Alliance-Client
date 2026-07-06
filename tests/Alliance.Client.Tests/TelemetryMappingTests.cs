using Alliance.Client.Features.Settings;
using Alliance.Client.Features.Telemetry;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;

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
        store.SetVideoState(ConnectionState.Ready, "Video ready");
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
        Assert.Equal("100", snapshot.AllyRobots[0].HealthText);
        Assert.Equal("707", snapshot.EnemyRobots[4].HealthText);
        Assert.Equal("Robot 1", snapshot.CurrentRobot.RobotLabel);
        Assert.Equal("HP 250/300", snapshot.CurrentRobot.HealthText);
        Assert.Equal("ROF 23.5", snapshot.CurrentRobot.FireRateText);
        Assert.Equal("AMMO 120", snapshot.CurrentRobot.AmmoText);
    }

    [Fact]
    public void TelemetryStore_Marks_Snapshot_As_Stale_When_Data_Stops()
    {
        var store = new TelemetryStore(CreateSettings());
        store.SetMqttState(ConnectionState.Ready, "MQTT ready");
        store.SetVideoState(ConnectionState.Ready, "Video ready");
        store.ApplyGameStatus(new GameStatus
        {
            StageCountdownSec = 10
        });

        store.RefreshStaleness(DateTimeOffset.UtcNow.AddSeconds(3));

        Assert.Equal(ConnectionState.Degraded, store.CurrentSnapshot.LinkState);
        Assert.Equal("Telemetry stale", store.CurrentSnapshot.WarningText);
    }

    private static AppSettings CreateSettings()
    {
        return new AppSettings
        {
            Mqtt = new AppSettings.MqttSettings
            {
                ClientId = "1"
            }
        };
    }
}
