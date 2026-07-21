using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsImageStore : ObservableObject
{
    private Bitmap? _backgroundImage;
    private Bitmap? _trajectoryImage;
    private string _backgroundStatsText = "";
    private string _trajectoryStatsText = "";

    public Bitmap? BackgroundImage
    {
        get => _backgroundImage;
        set => SetProperty(ref _backgroundImage, value);
    }

    public Bitmap? TrajectoryImage
    {
        get => _trajectoryImage;
        set => SetProperty(ref _trajectoryImage, value);
    }

    public string BackgroundStatsText
    {
        get => _backgroundStatsText;
        set => SetProperty(ref _backgroundStatsText, value);
    }

    public string TrajectoryStatsText
    {
        get => _trajectoryStatsText;
        set => SetProperty(ref _trajectoryStatsText, value);
    }
}
