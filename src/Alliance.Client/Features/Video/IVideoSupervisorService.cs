namespace Alliance.Client.Features.Video;

public interface IVideoSupervisorService
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
