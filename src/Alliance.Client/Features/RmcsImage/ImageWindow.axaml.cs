using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Alliance.Client.Features.RmcsImage;

public partial class ImageWindow : Window
{
    public ImageWindow()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public ImageWindow(ImageWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
