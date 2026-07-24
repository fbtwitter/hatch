using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed class FocusModeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TodoItem _task;
    private bool _exiting;

    public string Title => _task.Title;

    public ICommand MarkDoneCommand { get; }
    public ICommand ExitCommand { get; }

    public event Action? ExitRequested;

    public FocusModeViewModel(TodoItem task)
    {
        _task = task;
        _task.PropertyChanged += OnTaskPropertyChanged;

        // Completion drives the exit so the same path fires whether the user
        // clicks "Mark done" here or completes the task from the main window.
        MarkDoneCommand = new RelayCommand(_ => _task.IsCompleted = true);
        ExitCommand = new RelayCommand(_ => RequestExit());
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoItem.Title))
        {
            OnPropertyChanged(nameof(Title));
            return;
        }
        if (e.PropertyName == nameof(TodoItem.IsCompleted) && _task.IsCompleted)
            RequestExit();
    }

    private void RequestExit()
    {
        if (_exiting) return;
        _exiting = true;
        ExitRequested?.Invoke();
    }

    public void Dispose() => _task.PropertyChanged -= OnTaskPropertyChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
