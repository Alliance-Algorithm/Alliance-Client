namespace Alliance.Client.Features.Control;

public sealed class NoOpCommandService : ICommandService
{
    public Task SendAsync(string commandName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
