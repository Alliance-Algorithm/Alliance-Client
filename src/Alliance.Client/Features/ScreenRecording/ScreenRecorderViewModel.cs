using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Alliance.Client.Features.ScreenRecording;

public sealed partial class ScreenRecorderViewModel : ObservableObject
{
    private readonly ScreenRecorderService _service;
    private readonly RecordingSettings _settings;
    private readonly ILogger<ScreenRecorderViewModel> _logger;
    private bool _isRecording;
    private string _durationText = "00:00";
    private string _fileSizeText = "0 MB";
    private string? _errorText;

    public ScreenRecorderViewModel(
        ScreenRecorderService service,
        RecordingSettings settings,
        ILogger<ScreenRecorderViewModel> logger)
    {
        _service = service;
        _settings = settings;
        _logger = logger;
        _service.StatusChanged += OnStatusChanged;
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    public string DurationText
    {
        get => _durationText;
        set => SetProperty(ref _durationText, value);
    }

    public string FileSizeText
    {
        get => _fileSizeText;
        set => SetProperty(ref _fileSizeText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value);
    }

    public string RecButtonText => _isRecording ? $"● REC {_durationText}" : "REC";

    public string KeyBindingText => _settings.KeyBindingText;

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        try
        {
            if (_isRecording)
            {
                _logger.LogInformation("[recording] User requested stop.");
                await _service.StopAsync();
            }
            else
            {
                _logger.LogInformation("[recording] User requested start.");
                await _service.StartAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[recording] Toggle failed.");
            ErrorText = ex.Message;
        }
    }

    private void OnStatusChanged()
    {
        var status = _service.GetStatus();
        var wasRecording = _isRecording;
        IsRecording = status.IsRecording;
        DurationText = FormatDuration(status.Duration);
        FileSizeText = FormatFileSize(status.FileSizeBytes);
        ErrorText = status.Error;

        if (wasRecording != status.IsRecording)
        {
            OnPropertyChanged(nameof(RecButtonText));
        }
        else if (status.IsRecording)
        {
            OnPropertyChanged(nameof(RecButtonText));
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
            >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
            >= 1_000 => $"{bytes / 1_000.0:F1} KB",
            _ => $"{bytes} B"
        };
    }
}
