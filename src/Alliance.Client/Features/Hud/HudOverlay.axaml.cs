using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alliance.Client.Features.Hud;

public partial class HudOverlay : UserControl
{
    private Grid _rootLayout = null!;
    private Avalonia.Controls.Control _videoRegion = null!;
    private Avalonia.Controls.Control _robotStatusRegion = null!;
    private HudOverlayViewModel? _viewModel;

    public HudOverlay()
    {
        InitializeComponent();
        _rootLayout = this.FindControl<Grid>("RootLayout")!;
        _videoRegion = this.FindControl<Avalonia.Controls.Control>("VideoRegion")!;
        _robotStatusRegion = this.FindControl<Avalonia.Controls.Control>("RobotStatusRegion")!;
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
        ApplyRobotStatusLayout();
    }

    private void HandleLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HudLayoutSettings.RobotWidthScale)
            or nameof(HudLayoutSettings.RobotStatusBarsOnLeft))
        {
            ApplyRobotStatusLayout();
        }
    }

    private void ApplyRobotStatusLayout()
    {
        if (_viewModel is null || _rootLayout.ColumnDefinitions.Count < 2)
        {
            return;
        }

        var robotWidth = 2 * _viewModel.LayoutSettings.RobotWidthScale;

        if (_viewModel.LayoutSettings.RobotStatusBarsOnLeft)
        {
            Grid.SetColumn(_robotStatusRegion, 0);
            Grid.SetColumn(_videoRegion, 1);

            _rootLayout.ColumnDefinitions[0].Width = new GridLength(robotWidth, GridUnitType.Star);
            _rootLayout.ColumnDefinitions[1].Width = new GridLength(6, GridUnitType.Star);
            return;
        }

        Grid.SetColumn(_videoRegion, 0);
        Grid.SetColumn(_robotStatusRegion, 1);

        _rootLayout.ColumnDefinitions[0].Width = new GridLength(6, GridUnitType.Star);
        _rootLayout.ColumnDefinitions[1].Width = new GridLength(robotWidth, GridUnitType.Star);
    }
}
