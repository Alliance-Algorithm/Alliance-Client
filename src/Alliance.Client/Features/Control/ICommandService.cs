namespace Alliance.Client.Features.Control;

public interface ICommandService
{
    Task SendAsync(string commandName, CancellationToken cancellationToken = default);
}
