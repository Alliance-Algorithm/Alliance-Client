using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Alliance.Client.Features.Video;

public partial class VideoView : UserControl
{
    public VideoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
