using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alliance.Client.Features.Hud;

public partial class HudOverlay : UserControl
{
    private Grid _rootLayout = null!;
    private HudOverlayViewModel? _viewModel;

    public HudOverlay()
    {
        InitializeComponent();
        _rootLayout = this.FindControl<Grid>("RootLayout")!;
        DataContextChanged += HandleDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LayoutSettings.PropertyChanged -= HandleLayoutSettingsChanged;
        }

        _viewModel = DataContext as HudOverlayViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.LayoutSettings.PropertyChanged += HandleLayoutSettingsChanged;
        ApplyRobotPanelWidth();
    }

    private void HandleLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HudLayoutSettings.RobotWidthScale))
        {
            ApplyRobotPanelWidth();
        }
    }

    private void ApplyRobotPanelWidth()
    {
        if (_viewModel is null || _rootLayout.ColumnDefinitions.Count < 2)
        {
            return;
        }

        _rootLayout.ColumnDefinitions[0].Width = new GridLength(6, GridUnitType.Star);
        _rootLayout.ColumnDefinitions[1].Width = new GridLength(
            2 * _viewModel.LayoutSettings.RobotWidthScale,
            GridUnitType.Star);
    }
}
