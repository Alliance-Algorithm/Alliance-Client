using System.Buffers;
using System.Reflection;
using Avalonia.Threading;
using Alliance.Client.Features.Settings;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace Alliance.Client.Features.Telemetry;

public sealed class MqttTelemetryService : ITelemetryService
{
    private static readonly string[] Topics =
    [
        nameof(GameStatus),
        nameof(GlobalUnitStatus),
        nameof(GlobalLogisticsStatus),
        nameof(RobotStaticStatus),
        nameof(RobotDynamicStatus)
    ];

    private readonly AppSettings _settings;
    private readonly TelemetryStore _telemetryStore;
    private readonly ILogger<MqttTelemetryService> _logger;

    private CancellationTokenSource? _runtimeCts;
    private Task? _runTask;
    private Task? _monitorTask;
    private IMqttClient? _client;

    public MqttTelemetryService(
        AppSettings settings,
        TelemetryStore telemetryStore,
        ILogger<MqttTelemetryService> logger)
    {
        _settings = settings;
        _telemetryStore = telemetryStore;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is not null)
        {
            return Task.CompletedTask;
        }

        if (!PlayerIdentity.TryResolveRobotId(_settings.Mqtt.ClientId, out _))
        {
            _telemetryStore.SetMqttState(
                ConnectionState.NotConnected,
                $"Invalid MQTT client id '{_settings.Mqtt.ClientId}'");
            return Task.CompletedTask;
        }

        _runtimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunAsync(_runtimeCts.Token), _runtimeCts.Token);
        _monitorTask = Task.Run(() => MonitorAsync(_runtimeCts.Token), _runtimeCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is null)
        {
            return;
        }

        await _runtimeCts.CancelAsync();

        var tasks = new[] { _runTask, _monitorTask }.Where(task => task is not null).Cast<Task>().ToArray();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _runtimeCts.Dispose();
            _runtimeCts = null;
            _runTask = null;
            _monitorTask = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TaskCompletionSource<bool>? disconnectedSignal = null;

            try
            {
                await RunOnUiThreadAsync(() =>
                    _telemetryStore.SetMqttState(
                        ConnectionState.Connecting,
                        $"Connecting {_settings.Mqtt.Host}:{_settings.Mqtt.Port}"));

                var mqttFactory = new MqttClientFactory();
                var client = mqttFactory.CreateMqttClient();
                _client = client;
                disconnectedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                client.ConnectedAsync += HandleConnectedAsync;
                client.DisconnectedAsync += args =>
                {
                    disconnectedSignal.TrySetResult(true);
                    return HandleDisconnectedAsync(args);
                };
                client.ApplicationMessageReceivedAsync += HandleApplicationMessageReceivedAsync;

                var options = new MqttClientOptionsBuilder()
                    .WithClientId(_settings.Mqtt.ClientId)
                    .WithTcpServer(_settings.Mqtt.Host, _settings.Mqtt.Port)
                    .Build();

                await client.ConnectAsync(options, cancellationToken);
                await disconnectedSignal.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT telemetry loop failed.");
                await RunOnUiThreadAsync(() =>
                    _telemetryStore.SetMqttState(ConnectionState.NotConnected, $"MQTT error: {ex.Message}"));
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            finally
            {
                if (_client is not null)
                {
                    try
                    {
                        if (_client.IsConnected)
                        {
                            await _client.DisconnectAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Ignoring MQTT disconnect failure during shutdown.");
                    }

                    _client.Dispose();
                    _client = null;
                }
            }
        }

        await RunOnUiThreadAsync(() =>
            _telemetryStore.SetMqttState(ConnectionState.NotConnected, "MQTT stopped"));
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RunOnUiThreadAsync(() => _telemetryStore.RefreshStaleness(DateTimeOffset.UtcNow));
        }
    }

    private async Task HandleConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.LogInformation("Connected to MQTT {Host}:{Port}", _settings.Mqtt.Host, _settings.Mqtt.Port);

        if (_client is null)
        {
            return;
        }

        foreach (var topic in Topics)
        {
            await _client.SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce);
        }

        await RunOnUiThreadAsync(() =>
            _telemetryStore.SetMqttState(ConnectionState.Ready, $"Subscribed {Topics.Length} topics"));
    }

    private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_runtimeCts?.IsCancellationRequested == true)
        {
            return Task.CompletedTask;
        }

        return RunOnUiThreadAsync(() =>
            _telemetryStore.SetMqttState(ConnectionState.NotConnected, "MQTT disconnected"));
    }

    private Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var payload = ExtractPayloadBytes(args.ApplicationMessage);
            switch (args.ApplicationMessage.Topic)
            {
                case nameof(GameStatus):
                    return RunOnUiThreadAsync(() =>
                        _telemetryStore.ApplyGameStatus(GameStatus.Parser.ParseFrom(payload)));
                case nameof(GlobalUnitStatus):
                    return RunOnUiThreadAsync(() =>
                        _telemetryStore.ApplyGlobalUnitStatus(GlobalUnitStatus.Parser.ParseFrom(payload)));
                case nameof(GlobalLogisticsStatus):
                    return RunOnUiThreadAsync(() =>
                        _telemetryStore.ApplyGlobalLogisticsStatus(GlobalLogisticsStatus.Parser.ParseFrom(payload)));
                case nameof(RobotStaticStatus):
                    return RunOnUiThreadAsync(() =>
                        _telemetryStore.ApplyRobotStaticStatus(RobotStaticStatus.Parser.ParseFrom(payload)));
                case nameof(RobotDynamicStatus):
                    return RunOnUiThreadAsync(() =>
                        _telemetryStore.ApplyRobotDynamicStatus(RobotDynamicStatus.Parser.ParseFrom(payload)));
                default:
                    _logger.LogDebug("Ignoring unsupported MQTT topic {Topic}", args.ApplicationMessage.Topic);
                    return Task.CompletedTask;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process MQTT topic {Topic}", args.ApplicationMessage.Topic);
            return Task.CompletedTask;
        }
    }

    private static byte[] ExtractPayloadBytes(object applicationMessage)
    {
        foreach (var propertyName in new[] { "PayloadSegment", "Payload" })
        {
            var property = applicationMessage.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            if (property is null)
            {
                continue;
            }

            var value = property.GetValue(applicationMessage);
            switch (value)
            {
                case null:
                    continue;
                case byte[] bytes:
                    return bytes;
                case ArraySegment<byte> segment:
                    return segment.ToArray();
                case ReadOnlyMemory<byte> memory:
                    return memory.ToArray();
                case ReadOnlySequence<byte> sequence:
                    return sequence.ToArray();
            }
        }

        return [];
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        return RunOnUiThreadCoreAsync(action);
    }

    private static async Task RunOnUiThreadCoreAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
