namespace Alliance.Client.Features.RmcsImage;

public sealed class FrameAssembler : IDisposable
{
    private const int K = 10;
    private const int R = 3;
    private const int PayloadSize = 296;
    private const double TimeoutMs = 500;

    private readonly object _gate = new();
    private readonly Dictionary<byte, FrameBuffer> _frames = new();
    private readonly byte _messageType;

    public FrameAssembler(byte messageType)
    {
        _messageType = messageType;
    }

    public event Action<byte[], RmcsImageFrameStats?>? FrameCompleted;

    public void Feed(byte[] packet)
    {
        if (packet.Length < 4) return;

        byte messageType = packet[0];
        if (messageType != _messageType) return;

        byte status = packet[1];
        byte imageSeq = packet[2];
        byte packetSeq = packet[3];
        byte[] payload = new byte[PayloadSize];
        Array.Copy(packet, 4, payload, 0, Math.Min(PayloadSize, packet.Length - 4));

        bool isStart = (status & 0x0F) == 0x01;
        bool isEnd = (status & 0x0F) == 0x02;

        FrameBuffer? completedBuffer = null;

        lock (_gate)
        {
            if (isStart && _frames.TryGetValue(imageSeq, out var oldBuffer) && !oldBuffer.IsProcessed
                && (DateTime.UtcNow - oldBuffer.CreatedAt).TotalSeconds > 30)
            {
                oldBuffer.Timer?.Stop();
                oldBuffer.Timer?.Dispose();
                _frames.Remove(imageSeq);
                completedBuffer = oldBuffer;
            }

            if (!_frames.TryGetValue(imageSeq, out var buffer) || buffer.IsProcessed)
            {
                buffer = new FrameBuffer();
                _frames[imageSeq] = buffer;
            }

            buffer.Payloads[packetSeq] = payload;
            buffer.Statuses[packetSeq] = status;
            buffer.LastSequence = Math.Max(buffer.LastSequence, packetSeq);

            if (isEnd)
            {
                buffer.Timer?.Stop();
                buffer.Timer?.Dispose();
                buffer.Timer = null;
                buffer.IsProcessed = true;
                _frames.Remove(imageSeq);
                completedBuffer = buffer;
            }
            else
            {
                buffer.Timer?.Stop();
                buffer.Timer?.Dispose();
                buffer.Timer = new System.Timers.Timer(TimeoutMs) { AutoReset = false };
                var capturedSeq = imageSeq;
                buffer.Timer.Elapsed += (_, _) => OnTimeout(capturedSeq);
                buffer.Timer.Start();
            }
        }

        if (completedBuffer != null)
            AssembleAndPublish(imageSeq, completedBuffer);
    }

    private void OnTimeout(byte imageSeq)
    {
        FrameBuffer? buffer;
        lock (_gate)
        {
            if (!_frames.TryGetValue(imageSeq, out buffer) || buffer.IsProcessed)
                return;
            buffer.IsProcessed = true;
            _frames.Remove(imageSeq);
        }

        AssembleAndPublish(imageSeq, buffer);
    }

    private void AssembleAndPublish(byte imageSeq, FrameBuffer buffer)
    {
        var (jpegBytes, stats) = TryAssemble(buffer);
        if (jpegBytes != null)
            FrameCompleted?.Invoke(jpegBytes, stats);
    }

    private static (byte[]? jpeg, RmcsImageFrameStats? stats) TryAssemble(FrameBuffer buffer)
    {
        var payloads = buffer.Payloads;
        var statuses = buffer.Statuses;

        if (payloads.Count == 0) return (null, null);

        int kPrime = 10;
        bool hasFec = false;

        foreach (var (seq, status) in statuses)
        {
            byte lowNibble = (byte)(status & 0x0F);
            if (lowNibble == 0x02 || lowNibble == 0x03)
            {
                kPrime = status >> 4;
                hasFec = true;
                break;
            }
        }

        int N, G, D;
        if (hasFec)
        {
            int lastSeq = buffer.LastSequence;
            G = (lastSeq + 1 + K - kPrime + (K + R) - 1) / (K + R);
            N = G * (K + R) - K + kPrime;
            D = N - G * R;
        }
        else
        {
            D = 0;
            foreach (var (seq, status) in statuses)
            {
                byte lowNibble = (byte)(status & 0x0F);
                if (lowNibble == 0x01 || lowNibble == 0x00)
                    D++;
            }

            if (D == 0) return (null, null);

            kPrime = D % K;
            if (kPrime == 0 && D > 0) kPrime = K;
            int fullGroups = D / K;
            if (kPrime == K && fullGroups > 0)
                G = fullGroups;
            else if (D % K > 0)
                G = fullGroups + 1;
            else
                G = 0;

            if (G <= 0) return (null, null);
            N = D;
        }

        if (G <= 0 || D <= 0) return (null, null);

        var jpegBuf = new byte[D * PayloadSize];
        int jpegPos = 0;

        for (int g = 0; g < G; g++)
        {
            int kg = (g == G - 1) ? kPrime : K;
            int groupStart = g * (K + R);

            int dataStart = groupStart;
            int dataEnd = groupStart + kg;
            int fecEnd = groupStart + kg + R;

            var missingData = new List<int>();
            for (int seq = dataStart; seq < dataEnd; seq++)
            {
                if (!payloads.ContainsKey(seq))
                    missingData.Add(seq);
            }

            if (missingData.Count == 0)
            {
                for (int seq = dataStart; seq < dataEnd; seq++)
                {
                    Buffer.BlockCopy(payloads[seq], 0, jpegBuf, jpegPos, PayloadSize);
                    jpegPos += PayloadSize;
                }
            }
            else if (missingData.Count <= R)
            {
                var available = new List<int>();
                for (int seq = dataStart; seq < fecEnd; seq++)
                {
                    if (payloads.ContainsKey(seq))
                        available.Add(seq);
                }

                if (available.Count < kg)
                    return (null, null);

                var survivingIndices = new int[kg];
                var survivingPayloads = new byte[kg][];
                for (int i = 0; i < kg; i++)
                {
                    int seq = available[i];
                    survivingIndices[i] = seq - groupStart;
                    survivingPayloads[i] = payloads[seq];
                }

                byte[][] recovered;
                try
                {
                    recovered = CauchyRsDecoder.Decode(kg, R, survivingPayloads, survivingIndices);
                }
                catch
                {
                    return (null, null);
                }

                for (int d = 0; d < kg; d++)
                {
                    Buffer.BlockCopy(recovered[d], 0, jpegBuf, jpegPos, PayloadSize);
                    jpegPos += PayloadSize;
                }
            }
            else
            {
                return (null, null);
            }
        }

        int eoiPos = FindEoi(jpegBuf, jpegPos);
        if (eoiPos <= 0) return (null, null);

        var result = new byte[eoiPos];
        Buffer.BlockCopy(jpegBuf, 0, result, 0, eoiPos);

        var missingList = new List<int>();
        for (int seq = 0; seq < N; seq++)
        {
            if (!payloads.ContainsKey(seq))
                missingList.Add(seq);
        }

        var recvDataCount = 0;
        foreach (var (_, status) in statuses)
        {
            byte lowNibble = (byte)(status & 0x0F);
            if (lowNibble == 0x01 || lowNibble == 0x00)
                recvDataCount++;
        }

        var stats = new RmcsImageFrameStats
        {
            ReceivedPackets = payloads.Count,
            TotalPackets = hasFec ? N : null,
            ReceivedDataPackets = recvDataCount,
            TotalDataPackets = hasFec ? D : null,
            MissingSequences = missingList
        };

        return (result, stats);
    }

    private static int FindEoi(byte[] data, int length)
    {
        for (int i = length - 2; i >= 0; i--)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xD9)
                return i + 2;
        }

        return -1;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (_, buffer) in _frames)
            {
                buffer.Timer?.Stop();
                buffer.Timer?.Dispose();
            }

            _frames.Clear();
        }
    }

    private sealed class FrameBuffer
    {
        public readonly Dictionary<int, byte[]> Payloads = new();
        public readonly Dictionary<int, byte> Statuses = new();
        public readonly DateTime CreatedAt = DateTime.UtcNow;
        public int LastSequence = -1;
        public System.Timers.Timer? Timer;
        public volatile bool IsProcessed;
    }
}
