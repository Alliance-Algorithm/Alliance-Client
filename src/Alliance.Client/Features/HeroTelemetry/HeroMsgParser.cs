using System.Buffers.Binary;

namespace Alliance.Client.Features.HeroTelemetry;

public static class HeroMsgParser
{
    private const int MinPayloadLength = 44;

    public static HeroRobotStatus? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinPayloadLength)
            return null;

        return new HeroRobotStatus
        {
            PitchEncoder = BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            YawEncoder = BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]),
            TargetBulletSpeed = BinaryPrimitives.ReadInt32LittleEndian(payload[8..12]),
            ControlVelocityFront = BinaryPrimitives.ReadInt32LittleEndian(payload[12..16]),
            ControlVelocityRear = BinaryPrimitives.ReadInt32LittleEndian(payload[16..20]),
            Velocity0 = BinaryPrimitives.ReadInt32LittleEndian(payload[20..24]),
            Velocity1 = BinaryPrimitives.ReadInt32LittleEndian(payload[24..28]),
            Velocity2 = BinaryPrimitives.ReadInt32LittleEndian(payload[28..32]),
            Velocity3 = BinaryPrimitives.ReadInt32LittleEndian(payload[32..36]),
            Velocity4 = BinaryPrimitives.ReadInt32LittleEndian(payload[36..40]),
            Velocity5 = BinaryPrimitives.ReadInt32LittleEndian(payload[40..44]),
        };
    }
}
