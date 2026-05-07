using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings = App.SettingsService;

    public bool MinimizeToTray
    {
        get => _settings.Current.MinimizeToTray;
        set
        {
            if (_settings.Current.MinimizeToTray == value) return;
            _settings.Current.MinimizeToTray = value;
            App.MainWindowInstance?.UpdateTrayBehavior(value);
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int ThemeIndex
    {
        get => (int)_settings.Current.Theme;
        set
        {
            if ((int)_settings.Current.Theme == value) return;
            _settings.Current.Theme = (AppTheme)value;
            App.MainWindowInstance?.ApplyTheme(_settings.Current.Theme);
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public int BackdropIndex
    {
        get => (int)_settings.Current.Backdrop;
        set
        {
            if ((int)_settings.Current.Backdrop == value) return;
            _settings.Current.Backdrop = (AppBackdrop)value;
            App.MainWindowInstance?.ApplyBackdrop(_settings.Current.Backdrop);
            _ = _settings.SaveAsync();
            OnPropertyChanged();
        }
    }

    public bool MuteAnimation
    {
        get => _settings.Current.MuteAnimation;
        set
        {
            if (_settings.Current.MuteAnimation == value) return;
            _settings.Current.MuteAnimation = value;
            _ = _settings.SaveAsync();
            App.MascotWindowInstance?.ViewModel.RaiseMuteChanged();
            OnPropertyChanged();
        }
    }

    public bool LockMascotPosition
    {
        get => _settings.Current.LockMascotPosition;
        set
        {
            if (_settings.Current.LockMascotPosition == value) return;
            _settings.Current.LockMascotPosition = value;
            _ = _settings.SaveAsync();
            App.MascotWindowInstance?.ViewModel.RaiseLockPositionChanged();
            OnPropertyChanged();
        }
    }

    public string? LottieFilePath
    {
        get => _settings.Current.LottieFilePath;
        private set
        {
            if (_settings.Current.LottieFilePath == value) return;
            _settings.Current.LottieFilePath = value;
            _ = _settings.SaveAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LottieFileDisplay));
            OnPropertyChanged(nameof(HasLottieFile));
        }
    }

    public string LottieFileDisplay =>
        string.IsNullOrEmpty(LottieFilePath) ? "(none selected)" : Path.GetFileName(LottieFilePath);

    public bool HasLottieFile => !string.IsNullOrEmpty(LottieFilePath);

    public void SetLottieFilePath(string? path)
    {
        LottieFilePath = path;
        App.MascotWindowInstance?.ViewModel.RaiseLottieFileChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
