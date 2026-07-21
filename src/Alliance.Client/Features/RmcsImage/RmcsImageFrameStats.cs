namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageFrameStats
{
    public int ReceivedPackets { get; init; }
    public int? TotalPackets { get; init; }
    public int ReceivedDataPackets { get; init; }
    public int? TotalDataPackets { get; init; }
    public IReadOnlyList<int> MissingSequences { get; init; } = [];

    public double? LossRate
    {
        get
        {
            if (TotalPackets is not { } total || total == 0)
                return null;
            return 1.0 - (double)ReceivedPackets / total;
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
