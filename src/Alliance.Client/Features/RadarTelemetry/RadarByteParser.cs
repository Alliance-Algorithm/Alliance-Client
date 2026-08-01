using System.Buffers.Binary;

namespace Alliance.Client.Features.RadarTelemetry;

public static class RadarByteParser
{
    private const int MinPayloadLength = 71;

    public static EnemyRadarData? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinPayloadLength)
            return null;

        return new EnemyRadarData
        {
            HeroHealth = BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2]),
            EngineerHealth = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]),
            Infantry3Health = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..6]),
            Infantry4Health = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..8]),
            ReservedHealth = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..10]),
            SentinelHealth = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..12]),

            HeroBullets = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..14]),
            Infantry3Bullets = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..16]),
            Infantry4Bullets = BinaryPrimitives.ReadUInt16LittleEndian(payload[16..18]),
            AerialBullets = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..20]),
            SentinelBullets = BinaryPrimitives.ReadUInt16LittleEndian(payload[20..22]),

            EnemyRemainingGold = BinaryPrimitives.ReadUInt16LittleEndian(payload[22..24]),
            EnemyTotalGold = BinaryPrimitives.ReadUInt16LittleEndian(payload[24..26]),

            FieldStatus = ParseFieldStatus(BinaryPrimitives.ReadUInt32LittleEndian(payload[26..30])),

            HeroBuffs = ParseRobotBuffs(payload[30..37]),
            EngineerBuffs = ParseRobotBuffs(payload[37..44]),
            Infantry3Buffs = ParseRobotBuffs(payload[44..51]),
            Infantry4Buffs = ParseRobotBuffs(payload[51..58]),
            SentinelBuffs = ParseRobotBuffs(payload[58..65]),

            SentinelPosture = (SentinelPosture)payload[65],
            HeroState = (RobotMainState)payload[66],
            EngineerState = (RobotMainState)payload[67],
            Infantry3State = (RobotMainState)payload[68],
            Infantry4State = (RobotMainState)payload[69],
            SentinelState = (RobotMainState)payload[70]
        };
    }

    private static RobotBuffStatus ParseRobotBuffs(ReadOnlySpan<byte> span)
    {
        return new RobotBuffStatus
        {
            RegenPercent = span[0],
            HeatCoolValue = BinaryPrimitives.ReadUInt16LittleEndian(span[1..3]),
            DefensePercent = span[3],
            NegDefensePercent = span[4],
            AttackPercent = BinaryPrimitives.ReadUInt16LittleEndian(span[5..7])
        };
    }

    private static FieldOccupationStatus ParseFieldStatus(uint raw)
    {
        return new FieldOccupationStatus
        {
            SupplyZoneOccupied = (raw & (1u << 0)) != 0,
            CentralHighland = (CentralHighlandOccupation)((raw >> 1) & 0x3),
            TrapezoidHighlandOccupied = (raw & (1u << 3)) != 0,
            FortressBuff = (FortressBuffOccupation)((raw >> 4) & 0x3),
            OutpostBuff = (OutpostBuffOccupation)((raw >> 6) & 0x3),
            BaseBuffOccupied = (raw & (1u << 8)) != 0,
            EnemySidePreRampTunnelCard = (raw & (1u << 9)) != 0,
            EnemySidePostRampTunnelCard = (raw & (1u << 10)) != 0,
            AllySidePreRampTunnelCard = (raw & (1u << 11)) != 0,
            AllySidePostRampTunnelCard = (raw & (1u << 12)) != 0,
            EnemyHighlandTerrainCard = (raw & (1u << 13)) != 0,
            EnemyRampRearTerrainCard = (raw & (1u << 14)) != 0,
            EnemyRoadUpperTerrainCard = (raw & (1u << 15)) != 0
        };
    }
}
