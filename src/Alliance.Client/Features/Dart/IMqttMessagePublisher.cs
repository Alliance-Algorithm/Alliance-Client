namespace Alliance.Client.Features.Dart;

public interface IMqttMessagePublisher
{
    Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken = default);
}
