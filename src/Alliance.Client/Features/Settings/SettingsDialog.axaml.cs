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
    private readonly Border _displayNav;
    private readonly Border _recordingsNav;

    public SettingsDialog()
    {
        InitializeComponent();
        _basicNav = this.FindControl<Border>("BasicNav")!;
        _messageNav = this.FindControl<Border>("MessageNav")!;
        _displayNav = this.FindControl<Border>("DisplayNav")!;
        _recordingsNav = this.FindControl<Border>("RecordingsNav")!;
        UpdateNavStyles();
    }

    public SettingsDialog(SettingsDialogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, _) => UpdateNavStyles();
        KeyDown += OnDialogKeyDown;
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

    private void OnDisplayNavPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.IsDisplayTab = true;
            e.Handled = true;
        }
    }

    private void OnRecordingsNavPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.IsRecordingsTab = true;
            e.Handled = true;
        }
    }

    private void OnKeyBindPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.StartKeyRebind();
            e.Handled = true;
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.OnKeyRebindCapture(e);
            if (vm.IsListeningForKey)
            {
                e.Handled = true;
            }
        }
    }

    private void UpdateNavStyles()
    {
        if (DataContext is not SettingsDialogViewModel vm) return;

        ApplyNav(_basicNav, vm.IsBasicTab);
        ApplyNav(_messageNav, vm.IsMessageTab);
        ApplyNav(_displayNav, vm.IsDisplayTab);
        ApplyNav(_recordingsNav, vm.IsRecordingsTab);
    }

    private static void ApplyNav(Border nav, bool active)
    {
        nav.Background = active ? new SolidColorBrush(Color.Parse("#1A2A3360")) : null;
        nav.BorderBrush = active ? new SolidColorBrush(Color.Parse("#57D7C7")) : null;
        nav.BorderThickness = active ? new Thickness(3, 0, 0, 0) : new Thickness(0);
    }
}
