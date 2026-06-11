using System.ComponentModel;
using Alliance.Client.Features.Telemetry;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.Hud;

public sealed class HudOverlayViewModel : ObservableObject
{
    private readonly TelemetryStore _telemetryStore;
    private TelemetrySnapshot _snapshot;

    public HudOverlayViewModel(TelemetryStore telemetryStore)
    {
        _telemetryStore = telemetryStore;
        _snapshot = telemetryStore.CurrentSnapshot;
        _telemetryStore.PropertyChanged += HandleTelemetryChanged;
    }

    public TelemetrySnapshot Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    private void HandleTelemetryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(TelemetryStore.CurrentSnapshot))
        {
            Snapshot = _telemetryStore.CurrentSnapshot;
        }
    }
}
