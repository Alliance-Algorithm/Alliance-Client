namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageFrameStats
{
    public int ReceivedPackets { get; init; }
    public int? TotalPackets { get; init; }
    public int ReceivedDataPackets { get; init; }
    public int? TotalDataPackets { get; init; }
    public IReadOnlyList<int> MissingSequences { get; init; } = [];
    public DateTime FirstPacketAt { get; init; }
    public DateTime LastPacketAt { get; init; }
    public DateTime CompletedAt { get; set; }

    public TimeSpan? ReceptionDuration =>
        LastPacketAt != default && FirstPacketAt != default ? LastPacketAt - FirstPacketAt : null;

    public TimeSpan? AssemblyDuration =>
        CompletedAt != default && LastPacketAt != default ? CompletedAt - LastPacketAt : null;

    public double? LossRate
    {
        get
        {
            if (TotalPackets is not { } total || total == 0)
                return null;
            return 1.0 - (double)ReceivedPackets / total;
        }
    }

    public string LossRateText
    {
        get
        {
            if (LossRate is { } lr)
                return $"{lr * 100:F1}%";
            return "-";
        }
    }

    public string RecvText
    {
        get
        {
            if (ReceptionDuration is { } d)
                return $"{d.TotalMilliseconds:F0}ms";
            return "-";
        }
    }

    public string AsmText
    {
        get
        {
            if (AssemblyDuration is { } d)
                return $"{d.TotalMilliseconds:F0}ms";
            return "-";
        }
    }

    public string SummaryText
    {
        get
        {
            var totalStr = TotalPackets?.ToString() ?? "?";
            var recv = $"Recv:{ReceivedPackets}/{totalStr}";

            if (LossRate is { } lr)
            {
                var pct = (lr * 100).ToString("F1");
                recv += $"  Loss:{pct}%";
            }

            if (MissingSequences.Count > 0)
            {
                var missList = string.Join(",", MissingSequences);
                if (missList.Length > 40)
                    missList = missList[..40] + "...";
                recv += $"  Miss:[{missList}]";
            }

            return recv;
        }
    }
}
