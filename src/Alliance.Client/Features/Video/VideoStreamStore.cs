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
        private set
        {
            if (SetProperty(ref _surface, value))
            {
                OnPropertyChanged(nameof(IsWaitingForFrame));
            }
        }
    }

    public bool IsWaitingForFrame => Surface is null;

    public event Action? FrameUpdated;

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

        using (var locked = Surface.Lock())
        {
            var sourceStride = _settings.FrameWidth * 4;
            var destinationStride = locked.RowBytes;
            if (destinationStride == sourceStride)
            {
                var destination = new Span<byte>((void*)locked.Address, frameBytes.Length);
                frameBytes.CopyTo(destination);
            }
            else
            {
                var rows = Math.Min(_settings.FrameHeight, frameBytes.Length / sourceStride);
                var copyBytes = Math.Min(sourceStride, destinationStride);
                for (var y = 0; y < rows; y++)
                {
                    var source = frameBytes.Slice(y * sourceStride, copyBytes);
                    var destination = new Span<byte>((void*)(locked.Address + (y * destinationStride)), copyBytes);
                    source.CopyTo(destination);
                }
            }
        }

        FrameUpdated?.Invoke();
    }

    public unsafe void ClearFrame()
    {
        EnsureSurface();
        if (Surface is null)
        {
            return;
        }

        using (var locked = Surface.Lock())
        {
            new Span<byte>((void*)locked.Address, locked.RowBytes * _settings.FrameHeight).Clear();
        }

        FrameUpdated?.Invoke();
    }
}
