using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alliance.Client.Features.Hud;

public partial class HudOverlay : UserControl
{
    public HudOverlay()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
