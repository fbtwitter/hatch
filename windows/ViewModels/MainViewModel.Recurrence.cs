using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Dispatching;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private Action? _pendingUndo;
    private DispatcherQueueTimer? _undoDismissTimer;
    private bool _isUndoBarVisible;
    private string _undoMessage = string.Empty;

    public ICommand UndoLastActionCommand { get; private set; } = null!;

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

    // What the InfoBar shows while IsUndoBarVisible is true — varies by action
    // (completion vs. deletion), so it can't be a static x:Uid resource.
    public string UndoMessage
    {
        get => _undoMessage;
        private set
        {
            if (_undoMessage == value) return;
            _undoMessage = value;
            OnPropertyChanged();
        }
    }

    private TodoItem? TrySpawnNextRecurrence(TodoItem task)
    {
        if (task.Recurrence == TaskRecurrence.None || task.DueDate == null) return null;

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
        return next;
    }

    // A newer action bumps whatever the bar was showing — only the most recent
    // undoable action is ever recoverable, matching a single-slot snackbar.
    private void ShowUndoBar(string message, Action undo)
    {
        _undoDismissTimer?.Stop();

        _pendingUndo = undo;
        UndoMessage = message;
        IsUndoBarVisible = true;

        _undoDismissTimer = _dispatcherQueue.CreateTimer();
        _undoDismissTimer.Interval = TimeSpan.FromSeconds(4);
        _undoDismissTimer.IsRepeating = false;
        _undoDismissTimer.Tick += (_, _) => DismissUndoBar();
        _undoDismissTimer.Start();
    }

    private void ShowCompletionUndoBar(TodoItem task, TodoItem? spawned)
    {
        ShowUndoBar(Strings.UndoMessage_TaskCompleted, () =>
        {
            // Undoing a recurring task's completion also removes the occurrence it
            // spawned — otherwise the user ends up with a duplicate. This is an
            // internal cleanup delete, not a user-facing one, so it must not itself
            // open a new "Task deleted" undo bar — hence TombstoneTask, not DeleteTask.
            if (spawned != null)
                TombstoneTask(spawned);
            task.IsCompleted = false;
        });
    }

    public void DismissUndoBar()
    {
        _undoDismissTimer?.Stop();
        _undoDismissTimer = null;
        _pendingUndo = null;
        IsUndoBarVisible = false;
    }

    private void UndoLastAction()
    {
        var undo = _pendingUndo;
        DismissUndoBar();
        undo?.Invoke();
    }
}
