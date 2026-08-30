using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.Services;
using Microsoft.UI.Dispatching;

namespace Hatch.ViewModels;

// Split into partial files by concern — see MainViewModel.Filtering.cs, .Search.cs,
// .Suggestions.cs, .Lists.cs, .Recurrence.cs. This file owns construction, load/save,
// and task CRUD (the paths every other concern depends on).
public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskStorageService _storage;
    private readonly DispatcherQueue _dispatcherQueue;
    private string _newTaskText = string.Empty;
    private CancellationTokenSource? _saveCancelToken;
    private bool _isBulkLoading = false;
    private int _themeVersion = 0;
    private TodoItem? _selectedTask;

    public ObservableCollection<TodoItem> Tasks { get; } = [];
    public ObservableCollection<TodoItem> ActiveTasks { get; } = [];

    // Tombstones — out of the bound collections, but written back on every save.
    private readonly List<TodoItem> _deletedTasks = [];
    private readonly List<TaskList> _deletedLists = [];

    public string NewTaskText
    {
        get => _newTaskText;
        set
        {
            if (_newTaskText == value) return;
            _newTaskText = value;
            OnPropertyChanged();
            ((RelayCommand)AddTaskCommand).RaiseCanExecuteChanged();
        }
    }

    public TodoItem? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (_selectedTask == value) return;
            _selectedTask = value;
            OnPropertyChanged();
        }
    }

    public int BadgeVersion { get; private set; }

    public int ThemeVersion
    {
        get => _themeVersion;
        private set
        {
            if (_themeVersion == value) return;
            _themeVersion = value;
            OnPropertyChanged();
        }
    }

    public void NotifyThemeChanged()
    {
        ThemeVersion = _themeVersion + 1;
        RefreshActiveTasks();
    }

    public ICommand AddTaskCommand { get; }

    public MainViewModel()
    {
        _storage = new TaskStorageService();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AddTaskCommand = new RelayCommand(
            _ => AddTask(),
            _ => !string.IsNullOrWhiteSpace(NewTaskText));

        UndoLastActionCommand = new RelayCommand(_ => UndoLastAction());
        ClearTagFilterCommand = new RelayCommand(_ => ActiveTagFilter = null);
        AddSuggestionToMyDayCommand = new RelayCommand(param =>
        {
            if (param is not TodoItem task) return;
            task.SetMyDay(true);
            RefreshSuggestions();
            SaveAsync();
        });

        _flatGroupedTasks = [_openGroup, _completedGroup];

        // Open group is always expanded; never persisted.
        _openGroup.IsExpanded = true;

        // Persist Completed group expand/collapse state per nav tab.
        _completedGroup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CompletedTaskGroup.IsExpanded))
                _completedGroupExpandedState[_activeNavItem] = _completedGroup.IsExpanded;
        };

        Tasks.CollectionChanged += (_, e) =>
        {
            // Skip during bulk load — LoadAsync calls RefreshActiveTasks once at the end.
            if (_isBulkLoading) return;

            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                // Incremental insert keeps the UI fast for normal single-task adds.
                // Insert at 0 is correct because AddTask() prepends and all sorted
                // views put the newest task first.
                foreach (TodoItem task in e.NewItems)
                    if (MatchesFilter(task))
                        ActiveTasks.Insert(0, task);
                RebuildFlatGroups();
                RefreshSuggestions();
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TodoItem task in e.OldItems)
                    ActiveTasks.Remove(task);
                RebuildFlatGroups();
                RefreshSuggestions();
            }
            else
            {
                RefreshActiveTasks();
            }

            if (IsSearchActive)
                RefreshSearchResults();

            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(ShowEmptyState));
            NotifyPlannedGroupsChanged();
            OnPropertyChanged(nameof(IsPlannedEmpty));
            BadgeVersion++;
            OnPropertyChanged(nameof(BadgeVersion));
            if (_activeNavItem == "planned")
                OnPropertyChanged(nameof(ActiveNavItem));
        };

        _ = LoadAsync();

        App.SyncService.TasksReceived += () =>
            _dispatcherQueue.TryEnqueue(async () => await ReloadAsync());
    }

    public async Task ReloadAsync()
    {
        DismissUndoBar();
        _isBulkLoading = true;
        Tasks.Clear();
        CustomLists.Clear();
        _isBulkLoading = false;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var data = await _storage.LoadAsync();

            _deletedTasks.Clear();
            _deletedTasks.AddRange(data.Tasks.Where(t => t.IsDeleted));
            _deletedLists.Clear();
            _deletedLists.AddRange(data.Lists.Where(l => l.IsDeleted));

            _isBulkLoading = true;
            foreach (var task in TaskSorting.NewestFirst(data.Tasks.Where(t => !t.IsDeleted)))
            {
                AttachTaskPropertyChangedHandler(task);
                Tasks.Add(task);
            }
            _isBulkLoading = false;

            // Load lists sorted: pinned first, then ascending SortOrder
            foreach (var list in data.Lists.Where(l => !l.IsDeleted)
                                           .OrderByDescending(l => l.IsPinned).ThenBy(l => l.SortOrder))
                CustomLists.Add(list);

            RefreshListNames();
            RefreshActiveTasks();
            RefreshSuggestions();
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(ShowEmptyState));
            NotifyPlannedGroupsChanged();
            OnPropertyChanged(nameof(IsPlannedEmpty));

            App.NotificationScheduler.RescheduleAll(Tasks);
        }
        catch { _isBulkLoading = false; }
    }

    private void AddTask()
    {
        var task = new TodoItem { Title = NewTaskText.Trim(), ListName = "Task" };

        switch (_activeNavItem)
        {
            case "myday":
                task.SetMyDay(true);
                break;
            case "important":
                task.IsStarred = true;
                break;
            case "planned":
                task.DueDate = new DateTimeOffset(DateTime.Today);
                break;
            default:
                if (Guid.TryParse(_activeNavItem, out var listId))
                {
                    task.ListId = listId;
                    task.ListName = CustomLists.FirstOrDefault(l => l.Id == listId)?.Name;
                }
                break;
        }

        AttachTaskPropertyChangedHandler(task);
        App.NotificationScheduler.ScheduleForTask(task);
        Tasks.Insert(0, task);
        NewTaskText = string.Empty;
        SaveAsync();
    }

    public void AttachTaskPropertyChangedHandler(TodoItem task)
    {
        task.PropertyChanged -= TaskPropertyChanged;
        task.PropertyChanged += TaskPropertyChanged;
    }

    // Properties whose edits count as "real" for sync-merge purposes (see TodoItem.UpdatedAt).
    private static readonly HashSet<string?> RealEditProperties =
    [
        nameof(TodoItem.Title), nameof(TodoItem.Notes), nameof(TodoItem.Tags),
        nameof(TodoItem.IsInMyDay), nameof(TodoItem.IsStarred), nameof(TodoItem.DueDate),
        nameof(TodoItem.IsCompleted), nameof(TodoItem.Recurrence), nameof(TodoItem.Priority),
        nameof(TodoItem.Steps)
    ];

    private void TaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TodoItem task) return;

        if (RealEditProperties.Contains(e.PropertyName))
            task.UpdatedAt = DateTimeOffset.UtcNow;

        bool filterProp = e.PropertyName is
            nameof(TodoItem.IsInMyDay) or
            nameof(TodoItem.IsStarred) or
            nameof(TodoItem.DueDate)   or
            nameof(TodoItem.IsCompleted);

        if (filterProp)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (e.PropertyName == nameof(TodoItem.IsCompleted))
                {
                    ApplyCompletedChange(task);
                    RefreshSuggestions();
                }
                else if (e.PropertyName == nameof(TodoItem.DueDate))
                    ApplyDueDateChange(task);
                else if (e.PropertyName == nameof(TodoItem.IsStarred))
                    ApplyStarredChange(task);
                else
                {
                    // IsInMyDay changed
                    RefreshActiveTasks();
                    RefreshSuggestions();
                    OnPropertyChanged(nameof(IsTaskListEmpty));
                    OnPropertyChanged(nameof(ShowEmptyState));
                    NotifyPlannedGroupsChanged();
                    OnPropertyChanged(nameof(IsPlannedEmpty));
                    BadgeVersion++;
                    OnPropertyChanged(nameof(BadgeVersion));
                }
            });
        }

        if (IsSearchActive && e.PropertyName is nameof(TodoItem.Title) or nameof(TodoItem.Notes) or nameof(TodoItem.Tags))
            _dispatcherQueue.TryEnqueue(RefreshSearchResults);

        SaveAsync();
    }

    // Surgical update for IsCompleted — avoids Clear()+rebuild which causes item
    // container destruction and the resulting checkbox blink / access violation.
    private void ApplyCompletedChange(TodoItem task)
    {
        var spawned = task.IsCompleted ? TrySpawnNextRecurrence(task) : null;

        switch (_activeNavItem)
        {
            case "planned":
                // Planned filters out completed tasks entirely.
                if (task.IsCompleted)
                {
                    ActiveTasks.Remove(task);
                    App.NotificationScheduler.UnscheduleForTask(task.Id);
                    ShowCompletionUndoBar(task, spawned);
                }
                else
                {
                    App.NotificationScheduler.ScheduleForTask(task);
                    if (!ActiveTasks.Contains(task) && task.DueDate != null)
                        ActiveTasks.Add(task);   // unchecked: re-insert (order refresh below)
                }
                NotifyPlannedGroupsChanged();
                OnPropertyChanged(nameof(IsPlannedEmpty));
                OnPropertyChanged(nameof(IsTaskListEmpty));
                OnPropertyChanged(nameof(ShowEmptyState));
                BadgeVersion++;
                OnPropertyChanged(nameof(BadgeVersion));
                break;

            case "important":
                // Important filters out completed tasks entirely, like Planned — a
                // done task isn't something to act on anymore, even if still starred.
                if (task.IsCompleted)
                {
                    ActiveTasks.Remove(task);
                    _openGroup.Items.Remove(task);
                    App.NotificationScheduler.UnscheduleForTask(task.Id);
                    ShowCompletionUndoBar(task, spawned);
                }
                else
                {
                    App.NotificationScheduler.ScheduleForTask(task);
                    if (!ActiveTasks.Contains(task) && task.IsStarred)
                    {
                        ActiveTasks.Add(task);
                        _openGroup.Items.Insert(0, task);
                    }
                }
                OnPropertyChanged(nameof(IsTaskListEmpty));
                OnPropertyChanged(nameof(ShowEmptyState));
                BadgeVersion++;
                OnPropertyChanged(nameof(BadgeVersion));
                break;

            case "myday":
            case "alltasks":
            default:
                // Delay the group move so the strikethrough/fade animation is visible
                // before the task moves between groups.
                var timer = _dispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(250);
                timer.IsRepeating = false;
                Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? onTick = null;
                onTick = (_, _) =>
                {
                    timer.Stop();
                    timer.Tick -= onTick;
                    FlatGroupMoveStarting?.Invoke();
                    MoveBetweenFlatGroups(task);
                    FlatGroupMoveCompleted?.Invoke();
                    OnPropertyChanged(nameof(IsTaskListEmpty));
                    OnPropertyChanged(nameof(ShowEmptyState));
                    BadgeVersion++;
                    OnPropertyChanged(nameof(BadgeVersion));

                    if (task.IsCompleted && Tasks.Contains(task))
                    {
                        App.NotificationScheduler.UnscheduleForTask(task.Id);
                        ShowCompletionUndoBar(task, spawned);
                    }
                    else
                    {
                        App.NotificationScheduler.ScheduleForTask(task);
                    }
                };
                timer.Tick += onTick;
                timer.Start();
                break;
        }
    }

    public event Action? FlatGroupMoveStarting;
    public event Action? FlatGroupMoveCompleted;

    // Surgical update for DueDate — avoids full Clear()+rebuild.
    // x:Bind already re-evaluates the date chip on the row; we only need to
    // touch ActiveTasks when the view's filter/membership actually changes.
    private void ApplyDueDateChange(TodoItem task)
    {
        App.NotificationScheduler.ScheduleForTask(task);
        switch (_activeNavItem)
        {
            case "planned":
                // Planned shows tasks with a date and not completed.
                bool shouldBeInPlanned = task.DueDate != null && !task.IsCompleted;
                bool isInPlanned = ActiveTasks.Contains(task);
                if (shouldBeInPlanned && !isInPlanned)
                    ActiveTasks.Add(task);
                else if (!shouldBeInPlanned && isInPlanned)
                    ActiveTasks.Remove(task);
                NotifyPlannedGroupsChanged();
                OnPropertyChanged(nameof(IsPlannedEmpty));
                OnPropertyChanged(nameof(IsTaskListEmpty));
                OnPropertyChanged(nameof(ShowEmptyState));
                break;

            case "alltasks":
            case "myday":
            case "important":
            default:
                // DueDate doesn't affect membership or sort order in these views.
                // x:Bind converters on the chip handle the visual update automatically.
                NotifyPlannedGroupsChanged();
                break;
        }

        BadgeVersion++;
        OnPropertyChanged(nameof(BadgeVersion));
    }

    private void ApplyStarredChange(TodoItem task)
    {
        switch (_activeNavItem)
        {
            case "important":
                // Important view only shows starred, uncompleted tasks — add or remove accordingly.
                bool shouldBeInImportant = task.IsStarred && !task.IsCompleted;
                bool isInImportant = ActiveTasks.Contains(task);
                if (shouldBeInImportant && !isInImportant)
                {
                    ActiveTasks.Insert(0, task);
                    _openGroup.Items.Insert(0, task);
                }
                else if (!shouldBeInImportant && isInImportant)
                {
                    ActiveTasks.Remove(task);
                    _openGroup.Items.Remove(task);
                }
                OnPropertyChanged(nameof(IsTaskListEmpty));
                OnPropertyChanged(nameof(ShowEmptyState));
                break;

            default:
                // All other views keep the task regardless — the star glyph updates
                // automatically via x:Bind. No list rebuild needed.
                break;
        }

        BadgeVersion++;
        OnPropertyChanged(nameof(BadgeVersion));
    }

    // Seeded by the "add a sample task" tip action (mascot proactive tip).
    public void AddSampleTask()
    {
        var firstList = Lists.Count > 0 ? Lists[0] : null;
        var task = new TodoItem
        {
            Title = Helpers.Strings.Tip_SampleTaskTitle,
            ListId = firstList?.Id ?? Guid.Empty,
            ListName = firstList?.Name
        };

        AttachTaskPropertyChangedHandler(task);
        Tasks.Insert(0, task);
        SaveAsync();
    }

    public void AddTagToTask(TodoItem task, string tag)
    {
        var trimmed = tag.Trim();
        if (trimmed.Length == 0) return;
        if (task.Tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;
        task.Tags = [.. task.Tags, trimmed];
    }

    public void RemoveTagFromTask(TodoItem task, string tag)
        => task.Tags = task.Tags.Where(t => t != tag).ToList();

    // ── Steps (ADR-0010) ────────────────────────────────────────────────────
    // Add/remove reassign task.Steps; tick/rename mutate the Step (it raises its own
    // PropertyChanged for the checklist bindings) and call NotifyStepsChanged(). Either
    // way TaskPropertyChanged sees Steps — a RealEditProperty — and stamps UpdatedAt
    // (steps travel with the parent) and debounces the save.
    public const int MaxStepsPerTask = 20; // soft cap, ADR-0010 §3

    public void AddStepToTask(TodoItem task, string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0 || task.Steps.Count >= MaxStepsPerTask) return;
        task.Steps = [.. task.Steps, new Step { Title = trimmed }];
    }

    public void RemoveStepFromTask(TodoItem task, Step step)
        => task.Steps = task.Steps.Where(s => s.Id != step.Id).ToList();

    public void SetStepCompleted(TodoItem task, Step step, bool completed)
    {
        if (step.IsCompleted == completed) return;
        step.IsCompleted = completed;
        step.UpdatedAt = DateTimeOffset.UtcNow;
        task.NotifyStepsChanged();
    }

    public void RenameStep(TodoItem task, Step step, string title)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0 || step.Title == trimmed) return;
        step.Title = trimmed;
        step.UpdatedAt = DateTimeOffset.UtcNow;
        task.NotifyStepsChanged();
    }

    public void DeleteTask(TodoItem task)
    {
        TombstoneTask(task);
        ShowUndoBar(Strings.UndoMessage_TaskDeleted, () => RestoreTask(task));
    }

    // The mechanics of removal, with no undo bar — used both by the user-facing
    // DeleteTask above and by ShowCompletionUndoBar's cleanup of a spawned
    // recurrence, which must not open a second, unrelated undo bar of its own.
    private void TombstoneTask(TodoItem task)
    {
        task.PropertyChanged -= TaskPropertyChanged;
        App.NotificationScheduler.UnscheduleForTask(task.Id);
        Tombstone(task);
        Tasks.Remove(task);
        SaveAsync();
    }

    private void RestoreTask(TodoItem task)
    {
        task.IsDeleted = false;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        _deletedTasks.Remove(task);
        AttachTaskPropertyChangedHandler(task);
        Tasks.Insert(0, task);
        App.NotificationScheduler.ScheduleForTask(task);
        SaveAsync();
    }

    // UpdatedAt decides the delete against a concurrent edit on another device.
    private void Tombstone(TodoItem task)
    {
        task.IsDeleted = true;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        _deletedTasks.Add(task);
    }

    private void TombstoneList(TaskList list)
    {
        list.IsDeleted = true;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        _deletedLists.Add(list);
    }

    public void UpdateTask(TodoItem task, string newTitle, DateTimeOffset? newDueDate, bool newStarred)
    {
        // Unsubscribe while applying all changes to avoid multiple intermediate
        // refreshes firing for each property — one single refresh at the end.
        task.PropertyChanged -= TaskPropertyChanged;

        task.Title     = newTitle;
        task.DueDate   = newDueDate;
        task.IsStarred = newStarred;
        task.UpdatedAt = DateTimeOffset.UtcNow; // handler is detached above — stamp explicitly

        task.PropertyChanged += TaskPropertyChanged;
        App.NotificationScheduler.ScheduleForTask(task);

        // Apply in-place: keep scroll position by only removing/adding when membership
        // actually changes. x:Bind handles the visual update for title/date/star glyph.
        _dispatcherQueue.TryEnqueue(() =>
        {
            bool inList = ActiveTasks.Contains(task);
            bool shouldBeInList = MatchesFilter(task);

            if (!shouldBeInList && inList)
                ActiveTasks.Remove(task);
            else if (shouldBeInList && !inList)
                ActiveTasks.Insert(0, task);

            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(ShowEmptyState));
            NotifyPlannedGroupsChanged();
            OnPropertyChanged(nameof(IsPlannedEmpty));
        });

        SaveAsync();
    }

    public void UpdateTaskTitle(TodoItem task, string newTitle)
    {
        task.Title = newTitle;
        SaveAsync();
    }

    public void UpdateTaskDueDate(TodoItem task, DateTimeOffset? newDueDate)
    {
        task.DueDate = newDueDate;
    }

    public TodoItem? FindTaskById(Guid id) => Tasks.FirstOrDefault(t => t.Id == id);

    public void CompleteTaskById(Guid id)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == id);
        if (task != null && !task.IsCompleted)
            task.IsCompleted = true;
    }

    public void SaveAsync()
    {
        var previous = _saveCancelToken;
        previous?.Cancel();
        _saveCancelToken = new CancellationTokenSource();
        _ = DoSaveAsync(_saveCancelToken.Token);
        previous?.Dispose();
    }

    private async Task DoSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            var data = new TasksFile
            {
                Tasks = [.. Tasks, .. _deletedTasks],
                Lists = [.. CustomLists, .. _deletedLists]
            };
            await _storage.SaveAsync(data);
            App.SyncService.SchedulePush(data);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // A failed save must not crash the UI, but it must leave a trace —
            // this is the user's only copy of their data.
            try
            {
                var log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Hatch", "errors.log");
                await File.AppendAllTextAsync(log, $"{DateTime.Now:O} tasks.json save failed: {ex}{Environment.NewLine}");
            }
            catch { }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
