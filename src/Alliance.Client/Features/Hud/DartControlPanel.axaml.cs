using System.ComponentModel;
using Alliance.Client.Features.Dart;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alliance.Client.Features.Hud;

public partial class DartControlPanel : UserControl
{
    private static readonly SolidColorBrush TrackOn = new(Color.FromUInt32(0xFF57D7C7));
    private static readonly SolidColorBrush TrackOff = new(Color.FromUInt32(0xFF3A505B));

    private DartAutoService? _service;

    public DartControlPanel()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_service is not null)
        {
            _service.PropertyChanged -= OnServicePropertyChanged;
        }

        _service = DataContext as DartAutoService;

        if (_service is not null)
        {
            _service.PropertyChanged += OnServicePropertyChanged;
        }

        UpdateToggleVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_service is not null)
        {
            _service.PropertyChanged -= OnServicePropertyChanged;
            _service = null;
        }
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DartAutoService.IsEnabled))
        {
            UpdateToggleVisual();
        }
    }

    private void UpdateToggleVisual()
    {
        if (_service is null)
        {
            return;
        }

        if (_service.IsEnabled)
        {
            ToggleTrack.Background = TrackOn;
            ToggleKnob.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            ToggleKnob.Margin = new Thickness(0, 0, 3, 0);
        }
        else
        {
            ToggleTrack.Background = TrackOff;
            ToggleKnob.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            ToggleKnob.Margin = new Thickness(3, 0, 0, 0);
        }
    }
}
