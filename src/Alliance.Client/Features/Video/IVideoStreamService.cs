using System.ComponentModel;
using Avalonia.Media;
using Alliance.Client.Shared.Models;

namespace Alliance.Client.Features.Video;

public interface IVideoStreamService : INotifyPropertyChanged
{
    ConnectionState State { get; }

    string StatusText { get; }

    string TransportDescription { get; }

    string FrameCounterLabel { get; }

    IImage? CurrentFrame { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
