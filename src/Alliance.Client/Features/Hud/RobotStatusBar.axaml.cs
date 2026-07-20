using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace Alliance.Client.Features.Hud;

public partial class RobotStatusBar : UserControl
{
    public static readonly StyledProperty<double> TextScaleProperty =
        AvaloniaProperty.Register<RobotStatusBar, double>(nameof(TextScale), 1d);

    private RobotStatusBarViewModel? _viewModel;

    public double TextScale
    {
        get => GetValue(TextScaleProperty);
        set => SetValue(TextScaleProperty, value);
    }

    public RobotStatusBar()
    {
        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        }

        _viewModel = DataContext as RobotStatusBarViewModel;
        if (_viewModel is null)
        {
            return;
        }

        TextScale = _viewModel.RobotTextScale;
        _viewModel.PropertyChanged += HandleViewModelPropertyChanged;
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RobotStatusBarViewModel.RobotTextScale) &&
            sender is RobotStatusBarViewModel viewModel)
        {
            TextScale = viewModel.RobotTextScale;
        }
    }
}
