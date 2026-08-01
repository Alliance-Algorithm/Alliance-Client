namespace Alliance.Client.Features.RadarTelemetry;

public enum RobotMainState : byte
{
    Alive = 0,
    Dead = 1,
    Invincible = 2,
    InvincibleAndWeakened = 3
}

public enum SentinelPosture : byte
{
    None = 0,
    Attack = 1,
    Defense = 2,
    Move = 3,
    StrengthenedAttack = 4,
    StrengthenedDefense = 5,
    StrengthenedMove = 6
}

public enum CentralHighlandOccupation : byte
{
    None = 0,
    Enemy = 1,
    Ally = 2
}

public enum FortressBuffOccupation : byte
{
    None = 0,
    Enemy = 1,
    Ally = 2,
    Both = 3
}

public enum OutpostBuffOccupation : byte
{
    None = 0,
    Enemy = 1,
    Ally = 2
}

public sealed record EnemyRadarData
{
    public ushort HeroHealth { get; init; }
    public ushort EngineerHealth { get; init; }
    public ushort Infantry3Health { get; init; }
    public ushort Infantry4Health { get; init; }
    public ushort ReservedHealth { get; init; }
    public ushort SentinelHealth { get; init; }

    public ushort HeroBullets { get; init; }
    public ushort Infantry3Bullets { get; init; }
    public ushort Infantry4Bullets { get; init; }
    public ushort AerialBullets { get; init; }
    public ushort SentinelBullets { get; init; }

    public ushort EnemyRemainingGold { get; init; }
    public ushort EnemyTotalGold { get; init; }

    public FieldOccupationStatus FieldStatus { get; init; } = new();

    public RobotBuffStatus HeroBuffs { get; init; } = new();
    public RobotBuffStatus EngineerBuffs { get; init; } = new();
    public RobotBuffStatus Infantry3Buffs { get; init; } = new();
    public RobotBuffStatus Infantry4Buffs { get; init; } = new();
    public RobotBuffStatus SentinelBuffs { get; init; } = new();

    public SentinelPosture SentinelPosture { get; init; }

    public RobotMainState HeroState { get; init; }
    public RobotMainState EngineerState { get; init; }
    public RobotMainState Infantry3State { get; init; }
    public RobotMainState Infantry4State { get; init; }
    public RobotMainState SentinelState { get; init; }
}

public sealed record RobotBuffStatus
{
    public byte RegenPercent { get; init; }
    public ushort HeatCoolValue { get; init; }
    public byte DefensePercent { get; init; }
    public byte NegDefensePercent { get; init; }
    public ushort AttackPercent { get; init; }
}

public sealed record FieldOccupationStatus
{
    public bool SupplyZoneOccupied { get; init; }
    public CentralHighlandOccupation CentralHighland { get; init; }
    public bool TrapezoidHighlandOccupied { get; init; }
    public FortressBuffOccupation FortressBuff { get; init; }
    public OutpostBuffOccupation OutpostBuff { get; init; }
    public bool BaseBuffOccupied { get; init; }
    public bool EnemySidePreRampTunnelCard { get; init; }
    public bool EnemySidePostRampTunnelCard { get; init; }
    public bool AllySidePreRampTunnelCard { get; init; }
    public bool AllySidePostRampTunnelCard { get; init; }
    public bool EnemyHighlandTerrainCard { get; init; }
    public bool EnemyRampRearTerrainCard { get; init; }
    public bool EnemyRoadUpperTerrainCard { get; init; }
}
