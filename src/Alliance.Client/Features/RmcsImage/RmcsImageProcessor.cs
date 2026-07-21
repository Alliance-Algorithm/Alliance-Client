using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageProcessor : IDisposable
{
    private readonly RmcsImageStore _store;
    private readonly ILogger<RmcsImageProcessor> _logger;
    private readonly FrameAssembler _backgroundAssembler;
    private readonly FrameAssembler _trajectoryAssembler;

    public RmcsImageProcessor(RmcsImageStore store, ILogger<RmcsImageProcessor> logger, ILogger<FrameAssembler> frameLogger)
    {
        _store = store;
        _logger = logger;

        _backgroundAssembler = new FrameAssembler(0x01, frameLogger);
        _backgroundAssembler.FrameCompleted += (jpegBytes, stats) =>
            Dispatcher.UIThread.InvokeAsync(() => DecodeAndStore(jpegBytes, stats, isBackground: true));

        _trajectoryAssembler = new FrameAssembler(0x02, frameLogger);
        _trajectoryAssembler.FrameCompleted += (jpegBytes, stats) =>
            Dispatcher.UIThread.InvokeAsync(() => DecodeAndStore(jpegBytes, stats, isBackground: false));
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

    private void DecodeAndStore(byte[] jpegBytes, RmcsImageFrameStats? stats, bool isBackground)
    {
        var label = isBackground ? "bg" : "traj";
        _logger.LogDebug("Rmcs decoding JPEG: type={Type} size={Size}", label, jpegBytes.Length);

        try
        {
            using var ms = new MemoryStream(jpegBytes);
            var bitmap = new Bitmap(ms);
            if (isBackground)
            {
                _store.BackgroundImage?.Dispose();
                _store.BackgroundImage = bitmap;
                _store.BackgroundStatsText = stats?.SummaryText ?? "";
            }
            else
            {
                _store.TrajectoryImage?.Dispose();
                _store.TrajectoryImage = bitmap;
                _store.TrajectoryStatsText = stats?.SummaryText ?? "";
            }

            _logger.LogDebug("Rmcs image updated: type={Type} stats={Stats}", label, stats?.SummaryText ?? "-");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rmcs JPEG decode failed: type={Type} size={Size}", label, jpegBytes.Length);
        }
    }

    public void Dispose()
    {
        _backgroundAssembler.Dispose();
        _trajectoryAssembler.Dispose();
    }
}
