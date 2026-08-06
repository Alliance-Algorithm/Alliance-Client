using System.ComponentModel;
using Alliance.Client.Protocol;
using Alliance.Client.Features.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.Dart;

public sealed class DartAutoService : ObservableObject
{
    private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromSeconds(60);

    private enum State { Idle, Launching, Cooldown }

    private readonly TelemetryStore _telemetryStore;
    private readonly IMqttMessagePublisher _publisher;
    private readonly ILogger<DartAutoService> _logger;
    private State _state = State.Idle;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isEnabled = true;

    public DartAutoService(
        TelemetryStore telemetryStore,
        IMqttMessagePublisher publisher,
        ILogger<DartAutoService> logger)
    {
        _telemetryStore = telemetryStore;
        _publisher = publisher;
        _logger = logger;
        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                if (value)
                {
                    EvaluateConditions();
                }
                else
                {
                    CancelLoop();
                    _state = State.Idle;
                }
            }
        }
    }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(TelemetryStore.CurrentSnapshot))
        {
            return;
        }

        if (!_isEnabled)
        {
            return;
        }

        EvaluateConditions();
    }

    private void EvaluateConditions()
    {
        var snapshot = _telemetryStore.CurrentSnapshot;

        if (_state == State.Cooldown)
        {
            return;
        }

        if (_state == State.Launching)
        {
            if (snapshot.DartGateStatus >= 1)
            {
                CancelLoop();
                _state = State.Cooldown;
                _logger.LogInformation("Dart gate opened, entering cooldown");
                _ = StartCooldownAsync();
            }

            return;
        }

        var currentStage = _telemetryStore.GameStatus?.CurrentStage;
        var enemyOutpostHealth = _telemetryStore.GlobalUnitStatus?.EnemyOutpostHealth;
        var enemyBaseStatus = _telemetryStore.GlobalUnitStatus?.EnemyBaseStatus;

        if (currentStage == 4 && enemyOutpostHealth == 0 && enemyBaseStatus != 0)
        {
            _logger.LogInformation(
                "Dart launch conditions met: stage={Stage}, outpost={Outpost}, baseStatus={BaseStatus}",
                currentStage,
                enemyOutpostHealth,
                enemyBaseStatus);
            StartLaunching();
        }
    }

    private void StartLaunching()
    {
        CancelLoop();
        _state = State.Launching;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LaunchLoopAsync(_loopCts.Token));
    }

    private async Task LaunchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(SendInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SendDartCommandAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dart launch loop error");
        }
    }

    private async Task SendDartCommandAsync(CancellationToken cancellationToken)
    {
        try
        {
            var command = new DartCommand
            {
                TargetId = 2,
                Open = true,
                LaunchConfirm = true
            };

            var payload = command.ToByteArray();

            await _publisher.PublishAsync(nameof(DartCommand), payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send DartCommand");
        }
    }

    private async Task StartCooldownAsync()
    {
        try
        {
            await Task.Delay(CooldownDuration);
            _state = State.Idle;
            _logger.LogInformation("Dart cooldown finished");
            EvaluateConditions();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dart cooldown error");
            _state = State.Idle;
        }
    }

    private void CancelLoop()
    {
        if (_loopCts is not null)
        {
            _loopCts.Cancel();
            _loopCts.Dispose();
            _loopCts = null;
            _loopTask = null;
        }
    }
}
