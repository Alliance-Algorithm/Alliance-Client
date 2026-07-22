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
    private static readonly TimeSpan TelemetryFlushInterval = TimeSpan.FromMilliseconds(50);

    private static readonly string[] Topics =
    [
        nameof(GameStatus),
        nameof(GlobalUnitStatus),
        nameof(GlobalLogisticsStatus),
        nameof(GlobalSpecialMechanism),
        nameof(Event),
        nameof(RobotStaticStatus),
        nameof(RobotDynamicStatus),
        nameof(Buff),
        nameof(RadarInfoToClient),
        nameof(CustomByteBlock)
    ];

    private readonly AppSettings _settings;
    private readonly TelemetryStore _telemetryStore;
    private readonly ILogger<MqttTelemetryService> _logger;
    private readonly object _batchGate = new();
    private readonly List<Event> _pendingEvents = [];
    private readonly List<Buff> _pendingBuffs = [];

    private CancellationTokenSource? _runtimeCts;
    private Task? _runTask;
    private Task? _monitorTask;
    private Task? _batchTask;
    private IMqttClient? _client;
    private TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
    private DateTimeOffset? _pendingReceivedAt;
    private GameStatus? _pendingGameStatus;
    private GlobalUnitStatus? _pendingGlobalUnitStatus;
    private GlobalLogisticsStatus? _pendingGlobalLogisticsStatus;
    private GlobalSpecialMechanism? _pendingGlobalSpecialMechanism;
    private RobotStaticStatus? _pendingRobotStaticStatus;
    private RobotDynamicStatus? _pendingRobotDynamicStatus;
    private RadarInfoToClient? _pendingRadarInfoToClient;
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
        _batchTask = Task.Run(() => TelemetryBatchLoopAsync(_runtimeCts.Token), _runtimeCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeCts is null)
        {
            return;
        }

        await _runtimeCts.CancelAsync();

        var tasks = new[] { _runTask, _monitorTask, _batchTask }.Where(task => task is not null).Cast<Task>().ToArray();
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
            _batchTask = null;
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

    private async Task TelemetryBatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TelemetryFlushInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await FlushPendingTelemetryAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telemetry batch flush failed: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushPendingTelemetryAsync()
    {
        var batch = TakePendingTelemetryBatch();
        if (!batch.HasUpdates)
        {
            return;
        }

        await RunOnUiThreadAsync(() => _telemetryStore.ApplyBatch(batch), DispatcherPriority.Background);
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
            var rawTopic = args.ApplicationMessage.Topic;
            var topic = ResolveTopicName(rawTopic);
            var payload = args.ApplicationMessage.Payload.ToArray();
            _logger.LogDebug(
                "MQTT message received on topic '{Topic}' as '{ResolvedTopic}' ({Bytes} bytes)",
                rawTopic,
                topic,
                payload.Length);
            switch (topic)
            {
                case nameof(GameStatus):
                    EnqueueGameStatus(GameStatus.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(GlobalUnitStatus):
                    EnqueueGlobalUnitStatus(GlobalUnitStatus.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(GlobalLogisticsStatus):
                    EnqueueGlobalLogisticsStatus(GlobalLogisticsStatus.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(GlobalSpecialMechanism):
                    EnqueueGlobalSpecialMechanism(GlobalSpecialMechanism.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(Event):
                    EnqueueEvent(Event.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(RobotStaticStatus):
                    EnqueueRobotStaticStatus(RobotStaticStatus.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(RobotDynamicStatus):
                    EnqueueRobotDynamicStatus(RobotDynamicStatus.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(Buff):
                    EnqueueBuff(Buff.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(RadarInfoToClient):
                    EnqueueRadarInfoToClient(RadarInfoToClient.Parser.ParseFrom(payload));
                    return Task.CompletedTask;
                case nameof(CustomByteBlock):
                {
                    var data = CustomByteBlock.Parser.ParseFrom(payload).Data.ToByteArray();
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            _telemetryStore.ProcessCustomByteBlockImage(data);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "CustomByteBlock processing failed");
                        }
                    });
                    return RunOnUiThreadAsync(() =>
                        _telemetryStore.ApplyCustomByteBlockData(data));
                }
                default:
                    _logger.LogWarning(
                        "Ignoring unsupported MQTT topic '{Topic}' resolved as '{ResolvedTopic}'",
                        rawTopic,
                        topic);
                    break;
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process MQTT topic '{Topic}'", args.ApplicationMessage.Topic);
            return Task.CompletedTask;
        }
    }

    private static string ResolveTopicName(string topic)
    {
        var segments = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? topic : segments[^1];
    }

    private void EnqueueGameStatus(GameStatus status)
    {
        lock (_batchGate)
        {
            _pendingGameStatus = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueGlobalUnitStatus(GlobalUnitStatus status)
    {
        lock (_batchGate)
        {
            _pendingGlobalUnitStatus = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueGlobalLogisticsStatus(GlobalLogisticsStatus status)
    {
        lock (_batchGate)
        {
            _pendingGlobalLogisticsStatus = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueGlobalSpecialMechanism(GlobalSpecialMechanism status)
    {
        lock (_batchGate)
        {
            _pendingGlobalSpecialMechanism = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueEvent(Event status)
    {
        lock (_batchGate)
        {
            _pendingEvents.Add(status);
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueRobotStaticStatus(RobotStaticStatus status)
    {
        lock (_batchGate)
        {
            _pendingRobotStaticStatus = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueRobotDynamicStatus(RobotDynamicStatus status)
    {
        lock (_batchGate)
        {
            _pendingRobotDynamicStatus = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueBuff(Buff status)
    {
        lock (_batchGate)
        {
            _pendingBuffs.Add(status);
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private void EnqueueRadarInfoToClient(RadarInfoToClient status)
    {
        lock (_batchGate)
        {
            _pendingRadarInfoToClient = status;
            _pendingReceivedAt = DateTimeOffset.UtcNow;
        }
    }

    private TelemetryUpdateBatch TakePendingTelemetryBatch()
    {
        lock (_batchGate)
        {
            var batch = new TelemetryUpdateBatch
            {
                ReceivedAt = _pendingReceivedAt ?? DateTimeOffset.UtcNow,
                GameStatus = _pendingGameStatus,
                GlobalUnitStatus = _pendingGlobalUnitStatus,
                GlobalLogisticsStatus = _pendingGlobalLogisticsStatus,
                GlobalSpecialMechanism = _pendingGlobalSpecialMechanism,
                RobotStaticStatus = _pendingRobotStaticStatus,
                RobotDynamicStatus = _pendingRobotDynamicStatus,
                RadarInfoToClient = _pendingRadarInfoToClient,
                Events = _pendingEvents.ToArray(),
                Buffs = _pendingBuffs.ToArray()
            };

            _pendingReceivedAt = null;
            _pendingGameStatus = null;
            _pendingGlobalUnitStatus = null;
            _pendingGlobalLogisticsStatus = null;
            _pendingGlobalSpecialMechanism = null;
            _pendingRobotStaticStatus = null;
            _pendingRobotDynamicStatus = null;
            _pendingRadarInfoToClient = null;
            _pendingEvents.Clear();
            _pendingBuffs.Clear();

            return batch;
        }
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        return RunOnUiThreadCoreAsync(action, priority: null);
    }

    private Task RunOnUiThreadAsync(Action action, DispatcherPriority priority)
    {
        return RunOnUiThreadCoreAsync(action, priority);
    }

    private async Task RunOnUiThreadCoreAsync(Action action, DispatcherPriority? priority)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        if (priority.HasValue)
        {
            await Dispatcher.UIThread.InvokeAsync(action, priority.Value);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }
}
