using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageProcessor : IDisposable
{
    private readonly RmcsImageStore _store;
    private readonly RmcsPipelineProgress _progress;
    private readonly ILogger<RmcsImageProcessor> _logger;
    private readonly FrameAssembler _backgroundAssembler;
    private readonly FrameAssembler _trajectoryAssembler;

    private readonly object _gate = new();
    private readonly Dictionary<byte, PendingFrame> _pendingBackground = new();
    private readonly Dictionary<byte, PendingFrame> _pendingTrajectory = new();

    private sealed record PendingFrame(byte[] JpegBytes, RmcsImageFrameStats? Stats);

    public RmcsImageProcessor(RmcsImageStore store, ILogger<RmcsImageProcessor> logger, ILogger<FrameAssembler> frameLogger)
    {
        _store = store;
        _progress = store.PipelineProgress;
        _logger = logger;

        _backgroundAssembler = new FrameAssembler(0x01, frameLogger);
        _backgroundAssembler.FrameCompleted += (imageSeq, jpegBytes, stats) =>
            Dispatcher.UIThread.InvokeAsync(() => OnBackgroundComplete(imageSeq, jpegBytes, stats));

        _trajectoryAssembler = new FrameAssembler(0x02, frameLogger);
        _trajectoryAssembler.FrameCompleted += (imageSeq, jpegBytes, stats) =>
            Dispatcher.UIThread.InvokeAsync(() => OnTrajectoryComplete(imageSeq, jpegBytes, stats));
    }

    public void Feed(byte[] data)
    {
        if (data.Length < 4) return;

        byte messageType = data[0];

        _logger.LogInformation("Rmcs packet received: type=0x{Type:X2} len={Len}", messageType, data.Length);

        if (messageType == 0x01)
            _backgroundAssembler.Feed(data);
        else if (messageType == 0x02)
            _trajectoryAssembler.Feed(data);
    }

    private void OnBackgroundComplete(byte imageSeq, byte[] jpegBytes, RmcsImageFrameStats? stats)
    {
        _logger.LogInformation("Rmcs background ready: seq={Seq} stats={Stats}", imageSeq, stats?.SummaryText ?? "-");
        _progress.SetImageSeq(imageSeq);
        _progress.BackgroundReceived = true;

        if (stats != null)
        {
            _progress.BgLossRateText = stats.LossRateText;
            _progress.BgRecvText = stats.RecvText;
            _progress.BgAsmText = stats.AsmText;
        }

        PendingFrame? trajPending;
        lock (_gate)
        {
            _pendingBackground[imageSeq] = new PendingFrame(jpegBytes, stats);
            _pendingTrajectory.TryGetValue(imageSeq, out trajPending);
        }

        if (trajPending != null)
            ComposeAndDisplay(imageSeq, new PendingFrame(jpegBytes, stats), trajPending);
        else
            _progress.BackgroundDecoded = true;
    }

    private void OnTrajectoryComplete(byte imageSeq, byte[] jpegBytes, RmcsImageFrameStats? stats)
    {
        _logger.LogInformation("Rmcs trajectory ready: seq={Seq} stats={Stats}", imageSeq, stats?.SummaryText ?? "-");
        _progress.TrajectoryReceived = true;

        if (stats != null)
        {
            _progress.TrajLossRateText = stats.LossRateText;
            _progress.TrajRecvText = stats.RecvText;
            _progress.TrajAsmText = stats.AsmText;
        }

        PendingFrame? bgPending;
        lock (_gate)
        {
            _pendingTrajectory[imageSeq] = new PendingFrame(jpegBytes, stats);
            _pendingBackground.TryGetValue(imageSeq, out bgPending);
        }

        if (bgPending != null)
            ComposeAndDisplay(imageSeq, bgPending, new PendingFrame(jpegBytes, stats));
        else
            _progress.TrajectoryDecoded = true;
    }

    private void ComposeAndDisplay(byte imageSeq, PendingFrame bgFrame, PendingFrame trajFrame)
    {
        _logger.LogInformation("Rmcs composing: seq={Seq} bg={BgLen} traj={TrjLen}", imageSeq, bgFrame.JpegBytes.Length, trajFrame.JpegBytes.Length);
        _progress.BackgroundDecoded = true;
        _progress.TrajectoryDecoded = true;

        if (bgFrame.Stats != null)
        {
            _progress.BgLossRateText = bgFrame.Stats.LossRateText;
            _progress.BgRecvText = bgFrame.Stats.RecvText;
            _progress.BgAsmText = bgFrame.Stats.AsmText;
        }

        if (trajFrame.Stats != null)
        {
            _progress.TrajLossRateText = trajFrame.Stats.LossRateText;
            _progress.TrajRecvText = trajFrame.Stats.RecvText;
            _progress.TrajAsmText = trajFrame.Stats.AsmText;
        }

        if (bgFrame.Stats != null && trajFrame.Stats != null)
        {
            var firstBgAt = bgFrame.Stats.FirstPacketAt;
            var trajCompletedAt = trajFrame.Stats.CompletedAt;
            if (firstBgAt != default && trajCompletedAt != default)
            {
                var delta = trajCompletedAt - firstBgAt;
                _progress.TotalDurationText = $"{delta.TotalMilliseconds:F0}ms";
            }
        }

        if (bgFrame.Stats == null)
            _progress.BgLossRateText = _progress.BgRecvText = _progress.BgAsmText = "-";
        if (trajFrame.Stats == null)
            _progress.TrajLossRateText = _progress.TrajRecvText = _progress.TrajAsmText = "-";

        try
        {
            var composed = RmcsImageComposer.Compose(bgFrame.JpegBytes, trajFrame.JpegBytes);
            _store.ComposedImage?.Dispose();
            _store.ComposedImage = composed;
            _progress.Composed = true;

            _logger.LogInformation("Rmcs composition done: seq={Seq}", imageSeq);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rmcs composition failed: seq={Seq}", imageSeq);
        }
        finally
        {
            lock (_gate)
            {
                _pendingBackground.Remove(imageSeq);
                _pendingTrajectory.Remove(imageSeq);
            }
        }
    }

    public void Dispose()
    {
        _backgroundAssembler.Dispose();
        _trajectoryAssembler.Dispose();
    }
}
