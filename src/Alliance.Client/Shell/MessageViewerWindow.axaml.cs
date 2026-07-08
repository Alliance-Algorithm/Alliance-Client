using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Alliance.Client.Shell;

public partial class MessageViewerWindow : Window
{
    public MessageViewerWindow()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public MessageViewerWindow(MessageViewerWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
