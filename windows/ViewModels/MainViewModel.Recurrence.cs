using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Dispatching;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private TodoItem? _lastCompletedTask;
    private TodoItem? _lastSpawnedRecurrence;
    private DispatcherQueueTimer? _undoDismissTimer;
    private bool _isUndoBarVisible;

    public ICommand UndoLastCompletionCommand { get; private set; } = null!;

    public bool IsUndoBarVisible
    {
        get => _isUndoBarVisible;
        private set
        {
            if (_isUndoBarVisible == value) return;
            _isUndoBarVisible = value;
            OnPropertyChanged();
        }
    }

    private void TrySpawnNextRecurrence(TodoItem task)
    {
        _lastSpawnedRecurrence = null;
        if (task.Recurrence == TaskRecurrence.None || task.DueDate == null) return;

        var next = new TodoItem
        {
            Title      = task.Title,
            Notes      = task.Notes,
            Tags       = [.. task.Tags],
            ListId     = task.ListId,
            ListName   = task.ListName,
            IsStarred  = task.IsStarred,
            DueDate    = RecurrenceHelper.AdvanceDueDate(task.DueDate.Value, task.Recurrence),
            Recurrence = task.Recurrence,
            Priority   = task.Priority
        };

        AttachTaskPropertyChangedHandler(next);
        App.NotificationScheduler.ScheduleForTask(next);
        Tasks.Insert(0, next);
        _lastSpawnedRecurrence = next;
    }

    private void ShowUndoBar()
    {
        // Cancel any in-flight dismiss timer (e.g. rapid successive completions).
        _undoDismissTimer?.Stop();

        IsUndoBarVisible = true;

        _undoDismissTimer = _dispatcherQueue.CreateTimer();
        _undoDismissTimer.Interval = TimeSpan.FromSeconds(4);
        _undoDismissTimer.IsRepeating = false;
        _undoDismissTimer.Tick += (_, _) => DismissUndoBar();
        _undoDismissTimer.Start();
    }

    public void DismissUndoBar()
    {
        _undoDismissTimer?.Stop();
        _undoDismissTimer = null;
        _lastCompletedTask = null;
        _lastSpawnedRecurrence = null;
        IsUndoBarVisible = false;
    }

    private void UndoLastCompletion()
    {
        if (_lastCompletedTask is not { IsCompleted: true } task)
        {
            DismissUndoBar();
            return;
        }

        // Undoing a recurring task's completion also removes the occurrence it spawned —
        // otherwise the user would end up with a duplicate.
        if (_lastSpawnedRecurrence != null)
            DeleteTask(_lastSpawnedRecurrence);

        task.IsCompleted = false;
        DismissUndoBar();
    }
}
