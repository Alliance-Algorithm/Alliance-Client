using Alliance.Client.Protocol;

namespace Alliance.Client.Features.Telemetry;

internal sealed class TelemetryUpdateBatch
{
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;

    public GameStatus? GameStatus { get; init; }

    public GlobalUnitStatus? GlobalUnitStatus { get; init; }

    public GlobalLogisticsStatus? GlobalLogisticsStatus { get; init; }

    public GlobalSpecialMechanism? GlobalSpecialMechanism { get; init; }

    public RobotStaticStatus? RobotStaticStatus { get; init; }

    public RobotDynamicStatus? RobotDynamicStatus { get; init; }

    public RadarInfoToClient? RadarInfoToClient { get; init; }

    public IReadOnlyList<Event> Events { get; init; } = [];

    public IReadOnlyList<Buff> Buffs { get; init; } = [];

    public bool HasUpdates =>
        GameStatus is not null ||
        GlobalUnitStatus is not null ||
        GlobalLogisticsStatus is not null ||
        GlobalSpecialMechanism is not null ||
        RobotStaticStatus is not null ||
        RobotDynamicStatus is not null ||
        RadarInfoToClient is not null ||
        Events.Count > 0 ||
        Buffs.Count > 0;
}
