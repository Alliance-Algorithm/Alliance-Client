using System.Buffers;
using Avalonia.Threading;
using Alliance.Client.Features.Settings;
using Alliance.Client.Protocol;
using Alliance.Client.Shared.Models;
using Alliance.Client.Shared.Utils;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
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
    private TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);

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
                _logger.LogInformation(
                    "Attempting MQTT connection to {Host}:{Port}, ClientId={ClientId}, Protocol=MQTTv5",
                    _settings.Mqtt.Host,
                    _settings.Mqtt.Port,
                    _settings.Mqtt.ClientId);

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
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                    .WithCleanStart(true)
                    .WithSessionExpiryInterval(0)
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                    .Build();

                var connectResult = await client.ConnectAsync(options, cancellationToken);
                _logger.LogInformation(
                    "MQTT connect result: {ResultCode}, Reason={ReasonString}",
                    connectResult.ResultCode,
                    connectResult.ReasonString);

                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    _logger.LogWarning(
                        "MQTT connection rejected by broker: {ResultCode}, Reason={ReasonString}",
                        connectResult.ResultCode,
                        connectResult.ReasonString);
                    await RunOnUiThreadAsync(() =>
                        _telemetryStore.SetMqttState(ConnectionState.NotConnected,
                            $"Rejected: {connectResult.ResultCode}"));
                    await Task.Delay(_retryDelay, cancellationToken);
                    _retryDelay = _retryDelay + _retryDelay > MaxRetryDelay
                        ? MaxRetryDelay
                        : _retryDelay + _retryDelay;
                    continue;
                }

                _retryDelay = InitialRetryDelay;

                await disconnectedSignal.Task.WaitAsync(cancellationToken);
                _retryDelay = _retryDelay + _retryDelay > MaxRetryDelay
                    ? MaxRetryDelay
                    : _retryDelay + _retryDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (MQTTnet.Exceptions.MqttCommunicationException comEx)
            {
                _logger.LogWarning(comEx,
                    "MQTT communication failed (retry in {Delay}s): {Message}",
                    _retryDelay.TotalSeconds,
                    comEx.Message);
                await RunOnUiThreadAsync(() =>
                    _telemetryStore.SetMqttState(ConnectionState.NotConnected, $"MQTT error: {comEx.Message}"));
                await Task.Delay(_retryDelay, cancellationToken);
                _retryDelay = _retryDelay + _retryDelay > MaxRetryDelay
                    ? MaxRetryDelay
                    : _retryDelay + _retryDelay;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MQTT telemetry loop failed (retry in {Delay}s): {Message}",
                    _retryDelay.TotalSeconds,
                    ex.Message);
                await RunOnUiThreadAsync(() =>
                    _telemetryStore.SetMqttState(ConnectionState.NotConnected, $"MQTT error: {ex.Message}"));
                await Task.Delay(_retryDelay, cancellationToken);
                _retryDelay = _retryDelay + _retryDelay > MaxRetryDelay
                    ? MaxRetryDelay
                    : _retryDelay + _retryDelay;
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
        _logger.LogInformation(
            "MQTT connected to {Host}:{Port}. ConnectResult={ResultCode}",
            _settings.Mqtt.Host,
            _settings.Mqtt.Port,
            args.ConnectResult.ResultCode);

        if (_client is null)
        {
            return;
        }

        var subscribedCount = 0;
        foreach (var topic in Topics)
        {
            try
            {
                await _client.SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce);
                _logger.LogInformation("MQTT subscribed to topic '{Topic}'", topic);
                subscribedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT failed to subscribe to topic '{Topic}'", topic);
            }
        }

        var note = subscribedCount == Topics.Length
            ? $"Subscribed {Topics.Length}/{Topics.Length} topics"
            : $"Subscribed {subscribedCount}/{Topics.Length} topics (some failed)";

        await RunOnUiThreadAsync(() =>
            _telemetryStore.SetMqttState(ConnectionState.Ready, note));
    }

    private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _logger.LogWarning(
            "MQTT disconnected. Reason={Reason}, Message={ReasonString}",
            args.Reason,
            args.ReasonString);

        if (args.Exception is not null)
        {
            _logger.LogWarning(args.Exception, "MQTT disconnect caused by exception");
        }

        if (_runtimeCts?.IsCancellationRequested == true)
        {
            return Task.CompletedTask;
        }

        return RunOnUiThreadAsync(() =>
            _telemetryStore.SetMqttState(ConnectionState.NotConnected, $"MQTT disconnected: {args.ReasonString}"));
    }

    private Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var payload = args.ApplicationMessage.Payload.ToArray();
            _logger.LogDebug(
                "MQTT message received on topic '{Topic}' ({Bytes} bytes)",
                args.ApplicationMessage.Topic,
                payload.Length);
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
                    _logger.LogWarning("Ignoring unsupported MQTT topic '{Topic}'", args.ApplicationMessage.Topic);
                    return Task.CompletedTask;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process MQTT topic '{Topic}'", args.ApplicationMessage.Topic);
            return Task.CompletedTask;
        }
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
