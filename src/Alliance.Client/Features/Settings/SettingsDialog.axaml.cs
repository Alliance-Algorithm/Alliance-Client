using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Alliance.Client.Features.Settings;

public partial class SettingsDialog : Window
{
    private readonly Border _basicNav;
    private readonly Border _messageNav;

    public SettingsDialog()
    {
        InitializeComponent();
        _basicNav = this.FindControl<Border>("BasicNav")!;
        _messageNav = this.FindControl<Border>("MessageNav")!;
        UpdateNavStyles();
    }

    public SettingsDialog(SettingsDialogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, _) => UpdateNavStyles();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnBasicNavPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.IsBasicTab = true;
            e.Handled = true;
        }
    }

    private void OnMessageNavPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.IsMessageTab = true;
            e.Handled = true;
        }
    }

    private void UpdateNavStyles()
    {
        if (DataContext is not SettingsDialogViewModel vm) return;

        if (vm.IsBasicTab)
        {
            _basicNav.Background = new SolidColorBrush(Color.Parse("#1A2A3360"));
            _basicNav.BorderBrush = new SolidColorBrush(Color.Parse("#57D7C7"));
            _basicNav.BorderThickness = new Thickness(3, 0, 0, 0);
            _messageNav.Background = null;
            _messageNav.BorderBrush = null;
            _messageNav.BorderThickness = new Thickness(0);
        }
        else
        {
            _messageNav.Background = new SolidColorBrush(Color.Parse("#1A2A3360"));
            _messageNav.BorderBrush = new SolidColorBrush(Color.Parse("#57D7C7"));
            _messageNav.BorderThickness = new Thickness(3, 0, 0, 0);
            _basicNav.Background = null;
            _basicNav.BorderBrush = null;
            _basicNav.BorderThickness = new Thickness(0);
        }
    }
}
