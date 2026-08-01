using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Alliance.Client.Features.ScreenRecording;

public sealed class RecordingSettings : ObservableObject
{
    private readonly Settings.AppSettings _appSettings;
    private string _outputDirectory;
    private int _crf;
    private int _frameRate;
    private Key _recordKey;
    private KeyModifiers _recordModifiers;

    public RecordingSettings(Settings.AppSettings appSettings)
    {
        _appSettings = appSettings;
        var s = appSettings.Recording;
        _outputDirectory = s.OutputDirectory;
        _crf = Math.Clamp(s.Crf, 0, 51);
        _frameRate = Math.Clamp(s.FrameRate, 1, 60);
        _recordKey = Enum.TryParse<Key>(s.RecordKey, out var k) ? k : Key.R;
        _recordModifiers = Enum.TryParse<KeyModifiers>(s.RecordModifiers, out var m) ? m : KeyModifiers.Control;
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                _appSettings.Recording.OutputDirectory = value;
                OnPropertyChanged(nameof(KeyBindingText));
            }
        }
    }

    public int Crf
    {
        get => _crf;
        set
        {
            var clamped = Math.Clamp(value, 0, 51);
            if (SetProperty(ref _crf, clamped))
            {
                _appSettings.Recording.Crf = clamped;
            }
        }
    }

    public int FrameRate
    {
        get => _frameRate;
        set
        {
            var clamped = Math.Clamp(value, 1, 60);
            if (SetProperty(ref _frameRate, clamped))
            {
                _appSettings.Recording.FrameRate = clamped;
            }
        }
    }

    public Key RecordKey
    {
        get => _recordKey;
        set
        {
            if (SetProperty(ref _recordKey, value))
            {
                _appSettings.Recording.RecordKey = value.ToString();
                OnPropertyChanged(nameof(KeyBindingText));
            }
        }
    }

    public KeyModifiers RecordModifiers
    {
        get => _recordModifiers;
        set
        {
            if (SetProperty(ref _recordModifiers, value))
            {
                _appSettings.Recording.RecordModifiers = value.ToString();
                OnPropertyChanged(nameof(KeyBindingText));
            }
        }
    }

    public string KeyBindingText
    {
        get
        {
            var parts = new List<string>();
            if (_recordModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (_recordModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            if (_recordModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            parts.Add(_recordKey.ToString());
            return string.Join(" + ", parts);
        }
    }

    public bool KeyBindingPressed(KeyEventArgs e)
    {
        return e.Key == _recordKey
               && MatchModifier(e.KeyModifiers, KeyModifiers.Control, _recordModifiers)
               && MatchModifier(e.KeyModifiers, KeyModifiers.Alt, _recordModifiers)
               && MatchModifier(e.KeyModifiers, KeyModifiers.Shift, _recordModifiers)
               && !HasExtraModifiers(e.KeyModifiers, _recordModifiers);
    }

    private static bool MatchModifier(KeyModifiers pressed, KeyModifiers flag, KeyModifiers configured)
    {
        var expected = configured.HasFlag(flag);
        var actual = pressed.HasFlag(flag);
        return expected == actual;
    }

    private static bool HasExtraModifiers(KeyModifiers pressed, KeyModifiers configured)
    {
        var allModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;
        var pressedMods = pressed & allModifiers;
        var configuredMods = configured & allModifiers;
        return pressedMods != configuredMods;
    }
}
