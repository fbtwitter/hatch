using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Dispatching;

namespace Hatch.ViewModels;

public sealed class FocusModeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TodoItem _task;
    private readonly DispatcherQueueTimer _tick;
    private FocusSession _session;
    private bool _exiting;

    public string Title => _task.Title;

    public string ElapsedText { get; private set; } = FocusTimer.Format(TimeSpan.Zero);

    // 0..1 — one lap a minute. See FocusTimer.MinuteFraction for why a count-up stopwatch
    // borrows a second hand rather than filling a bar.
    public double MinuteProgress { get; private set; }

    public bool IsPaused => _session.Paused;
    public string PauseResumeGlyph => _session.Paused ? "\uE768" : "\uE769";  // Play : Pause
    public string PauseResumeLabel => _session.Paused ? Strings.FocusMode_Resume : Strings.FocusMode_Pause;

    public ICommand MarkDoneCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand PauseResumeCommand { get; }

    public event Action? ExitRequested;

    public FocusModeViewModel(TodoItem task, FocusSession? restored = null)
    {
        _task = task;
        _session = restored ?? new FocusSession(
            task.Id, task.Title, DateTimeOffset.UtcNow, TimeSpan.Zero, Paused: false);
        _task.PropertyChanged += OnTaskPropertyChanged;

        // Completion drives the exit so the same path fires whether the user
        // clicks "Mark done" here or completes the task from the main window.
        MarkDoneCommand = new RelayCommand(_ => _task.IsCompleted = true);
        ExitCommand = new RelayCommand(_ => RequestExit());
        PauseResumeCommand = new RelayCommand(_ => TogglePause());

        // DispatcherQueueTimer, not PeriodicTimer: this tick only ever updates bound
        // properties, so firing on the dispatcher avoids a marshal per second.
        _tick = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick += OnTick;
        if (!IsPaused) _tick.Start();

        Refresh();
    }

    // The session as it should be written to settings.json — the mascot window persists it
    // through SettingsService rather than this ViewModel reaching for App.
    public FocusSession Session => _session;

    public event Action<FocusSession?>? SessionChanged;

    private void TogglePause()
    {
        var now = DateTimeOffset.UtcNow;
        _session = _session.Paused ? FocusTimer.Resume(_session, now) : FocusTimer.Pause(_session, now);
        if (IsPaused) _tick.Stop();
        else _tick.Start();
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseResumeGlyph));
        OnPropertyChanged(nameof(PauseResumeLabel));
        Refresh();
        Persist();
    }

    private void Refresh()
    {
        var elapsed = FocusTimer.Elapsed(_session, DateTimeOffset.UtcNow);

        var text = FocusTimer.Format(elapsed);
        if (text != ElapsedText)
        {
            ElapsedText = text;
            OnPropertyChanged(nameof(ElapsedText));
        }

        var progress = FocusTimer.MinuteFraction(elapsed);
        if (progress != MinuteProgress)
        {
            MinuteProgress = progress;
            OnPropertyChanged(nameof(MinuteProgress));
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => Refresh();

    private void Persist() => SessionChanged?.Invoke(_session);

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoItem.Title))
        {
            _session = _session with { TaskTitle = _task.Title };
            OnPropertyChanged(nameof(Title));
            Persist();
            return;
        }
        if (e.PropertyName == nameof(TodoItem.IsCompleted) && _task.IsCompleted)
            RequestExit();
    }

    private void RequestExit()
    {
        if (_exiting) return;
        _exiting = true;
        _tick.Stop();
        SessionChanged?.Invoke(null);
        ExitRequested?.Invoke();
    }

    public void Dispose()
    {
        _tick.Stop();
        _tick.Tick -= OnTick;
        _task.PropertyChanged -= OnTaskPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
