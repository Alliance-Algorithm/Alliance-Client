using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Alliance.Client.Features.Hud;

public partial class TeamPanel : UserControl
{
    public static readonly StyledProperty<double> PanelBackgroundOpacityProperty =
        AvaloniaProperty.Register<TeamPanel, double>(nameof(PanelBackgroundOpacity), 0.8d);

    private Border _panelFrame = null!;

    public double PanelBackgroundOpacity
    {
        get => GetValue(PanelBackgroundOpacityProperty);
        set => SetValue(PanelBackgroundOpacityProperty, value);
    }

    public TeamPanel()
    {
        InitializeComponent();
        _panelFrame = this.FindControl<Border>("PanelFrame")!;
        ApplyPanelBackground();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PanelBackgroundOpacityProperty && _panelFrame is not null)
        {
            ApplyPanelBackground();
        }
    }

    private void ApplyPanelBackground()
    {
        var alpha = (byte)Math.Round(Math.Clamp(PanelBackgroundOpacity, 0d, 1d) * 255d);
        _panelFrame.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x0D, 0x11, 0x17));
    }
}
