using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageProcessor : IDisposable
{
    private readonly RmcsImageStore _store;
    private readonly FrameAssembler _backgroundAssembler;
    private readonly FrameAssembler _trajectoryAssembler;

    public RmcsImageProcessor(RmcsImageStore store)
    {
        _store = store;

        _backgroundAssembler = new FrameAssembler(0x01);
        _backgroundAssembler.FrameCompleted += (jpegBytes, stats) =>
            Dispatcher.UIThread.InvokeAsync(() => DecodeAndStore(jpegBytes, stats, isBackground: true));

        _trajectoryAssembler = new FrameAssembler(0x02);
        _trajectoryAssembler.FrameCompleted += (jpegBytes, stats) =>
            Dispatcher.UIThread.InvokeAsync(() => DecodeAndStore(jpegBytes, stats, isBackground: false));
    }

    public void Feed(byte[] data)
    {
        if (data.Length < 4) return;

        byte messageType = data[0];

        if (messageType == 0x01)
            _backgroundAssembler.Feed(data);
        else if (messageType == 0x02)
            _trajectoryAssembler.Feed(data);
    }

    private void DecodeAndStore(byte[] jpegBytes, RmcsImageFrameStats? stats, bool isBackground)
    {
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
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _backgroundAssembler.Dispose();
        _trajectoryAssembler.Dispose();
    }
}
