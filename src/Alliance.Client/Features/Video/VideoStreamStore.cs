using Alliance.Client.Features.Settings;
using Alliance.Client.Shared.Models;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Video;

public sealed class VideoStreamStore : ObservableObject
{
    private readonly AppSettings.VideoSettings _settings;
    private VideoStatusSnapshot _snapshot;
    private WriteableBitmap? _surface;

    public VideoStreamStore(AppSettings settings)
    {
        _settings = settings.Video;
        _snapshot = VideoStatusSnapshot.Empty(_settings.FrameWidth, _settings.FrameHeight);
    }

    public VideoStatusSnapshot Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public WriteableBitmap? Surface
    {
        get => _surface;
        private set => SetProperty(ref _surface, value);
    }

    public void SetStatus(
        ConnectionState state,
        string statusText,
        string metricsText,
        DateTimeOffset? lastPacketAt,
        DateTimeOffset? lastFrameAt,
        long frameVersion)
    {
        Snapshot = new VideoStatusSnapshot(
            state,
            statusText,
            metricsText,
            $"{_settings.FrameWidth}x{_settings.FrameHeight}",
            lastPacketAt,
            lastFrameAt,
            frameVersion);
    }

    public void EnsureSurface()
    {
        if (Surface is not null)
        {
            return;
        }

        Surface = new WriteableBitmap(
            new PixelSize(_settings.FrameWidth, _settings.FrameHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
    }

    public unsafe void UpdateFrame(ReadOnlySpan<byte> frameBytes)
    {
        EnsureSurface();
        if (Surface is null)
        {
            return;
        }

        using var locked = Surface.Lock();
        var destination = new Span<byte>((void*)locked.Address, frameBytes.Length);
        frameBytes.CopyTo(destination);
    }

    public unsafe void ClearFrame()
    {
        EnsureSurface();
        if (Surface is null)
        {
            return;
        }

        using var locked = Surface.Lock();
        new Span<byte>((void*)locked.Address, _settings.FrameWidth * _settings.FrameHeight * 4).Clear();
    }
}
