using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.RmcsImage;

public sealed class RmcsPipelineProgress : ObservableObject
{
    private bool _backgroundReceived;
    private bool _backgroundDecoded;
    private bool _trajectoryReceived;
    private bool _trajectoryDecoded;
    private bool _composed;

    public bool BackgroundReceived
    {
        get => _backgroundReceived;
        set => SetProperty(ref _backgroundReceived, value);
    }

    public bool BackgroundDecoded
    {
        get => _backgroundDecoded;
        set => SetProperty(ref _backgroundDecoded, value);
    }

    public bool TrajectoryReceived
    {
        get => _trajectoryReceived;
        set => SetProperty(ref _trajectoryReceived, value);
    }

    public bool TrajectoryDecoded
    {
        get => _trajectoryDecoded;
        set => SetProperty(ref _trajectoryDecoded, value);
    }

    public bool Composed
    {
        get => _composed;
        set => SetProperty(ref _composed, value);
    }

    private string _imageSeqText = "-";

    public string ImageSeqText
    {
        get => _imageSeqText;
        set => SetProperty(ref _imageSeqText, value);
    }

    private string _bgLossRateText = "-";
    public string BgLossRateText
    {
        get => _bgLossRateText;
        set => SetProperty(ref _bgLossRateText, value);
    }

    private string _trajLossRateText = "-";
    public string TrajLossRateText
    {
        get => _trajLossRateText;
        set => SetProperty(ref _trajLossRateText, value);
    }

    private string _bgRecvText = "-";
    public string BgRecvText
    {
        get => _bgRecvText;
        set => SetProperty(ref _bgRecvText, value);
    }

    private string _bgAsmText = "-";
    public string BgAsmText
    {
        get => _bgAsmText;
        set => SetProperty(ref _bgAsmText, value);
    }

    private string _trajRecvText = "-";
    public string TrajRecvText
    {
        get => _trajRecvText;
        set => SetProperty(ref _trajRecvText, value);
    }

    private string _trajAsmText = "-";
    public string TrajAsmText
    {
        get => _trajAsmText;
        set => SetProperty(ref _trajAsmText, value);
    }

    private string _totalDurationText = "-";
    public string TotalDurationText
    {
        get => _totalDurationText;
        set => SetProperty(ref _totalDurationText, value);
    }

    public void SetImageSeq(byte seq)
    {
        ImageSeqText = $"Seq: {seq}";
    }

    public void Reset()
    {
        BackgroundReceived = false;
        BackgroundDecoded = false;
        TrajectoryReceived = false;
        TrajectoryDecoded = false;
        Composed = false;
    }
}
