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

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskStorageService _storage;
    private readonly DispatcherQueue _dispatcherQueue;
    private string _newTaskText = string.Empty;
    private string _activeNavItem = "alltasks";
    private CancellationTokenSource? _saveCancelToken;
    private bool _isBulkLoading = false;
    private int _themeVersion = 0;
    private readonly Dictionary<string, bool> _completedGroupExpandedState = new();
    private TodoItem? _lastCompletedTask;
    private TodoItem? _selectedTask;
    private DispatcherQueueTimer? _undoDismissTimer;
    private bool _isUndoBarVisible;

    private readonly CompletedTaskGroup _openGroup = new() { Name = "Open", EmptyMessage = "All done!" };
    private readonly CompletedTaskGroup _completedGroup = new() { Name = "Completed", TrackCount = true };
    private readonly IList<CompletedTaskGroup> _flatGroupedTasks;

    public ObservableCollection<TodoItem> Tasks { get; } = [];
    public ObservableCollection<TodoItem> ActiveTasks { get; } = [];
    public ObservableCollection<TaskList> Lists { get; } = [];
    public ObservableCollection<TaskList> CustomLists { get; } = [];

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

    public bool IsTaskListEmpty => ActiveTasks.Count == 0;

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

    public ICommand UndoLastCompletionCommand { get; private set; } = null!;

    public bool IsPlannedEmpty => !Tasks.Any(t => t.DueDate != null && !t.IsCompleted);

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

    public string ActiveNavItem
    {
        get => _activeNavItem;
        set
        {
            if (_activeNavItem == value) return;
            _activeNavItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(PlannedGroups));
            OnPropertyChanged(nameof(IsPlannedEmpty));
            OnPropertyChanged(nameof(EmptyStateGlyph));
            OnPropertyChanged(nameof(EmptyStateHeadline));
            OnPropertyChanged(nameof(EmptyStateSubtext));
            _completedGroup.IsExpanded = IsCompletedGroupExpanded(value);
            App.Settings.ActiveNavItem = value;
            _ = App.SettingsService.SaveAsync();
            // Defer one frame so the page shell renders before the list rebuilds,
            // without the visible pause that Low priority causes mid-navigation.
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshActiveTasks);
        }
    }

    public string EmptyStateGlyph => _activeNavItem switch
    {
        "myday"     => "\uE706", // Sun
        "important" => "\uE735", // Star
        "planned"   => "\uED28", // Calendar
        _           => "\uE762"  // Document
    };
    public string EmptyStateHeadline => _activeNavItem switch
    {
        "myday"     => Strings.EmptyState_MyDay_Headline,
        "important" => Strings.EmptyState_Important_Headline,
        "planned"   => Strings.EmptyState_Planned_Headline,
        _           => Guid.TryParse(_activeNavItem, out _)
                        ? Strings.EmptyState_CustomList_Headline
                        : Strings.EmptyState_AllTasks_Headline
    };

    public string EmptyStateSubtext => _activeNavItem switch
    {
        "myday"     => Strings.EmptyState_MyDay_Subtext,
        "important" => Strings.EmptyState_Important_Subtext,
        "planned"   => Strings.EmptyState_Planned_Subtext,
        _           => Guid.TryParse(_activeNavItem, out _)
                        ? Strings.EmptyState_CustomList_Subtext
                        : Strings.EmptyState_AllTasks_Subtext
    };

    private bool MatchesFilter(TodoItem task) => _activeNavItem switch
    {
        "myday"     => task.IsInMyDay,
        "important" => task.IsStarred,
        "planned"   => task.DueDate != null && !task.IsCompleted,
        _           => !Guid.TryParse(_activeNavItem, out var listId) || task.ListId == listId
    };

    private void RefreshActiveTasks()
    {
        ActiveTasks.Clear();
        var today = DateTimeOffset.Now.Date;
        var filtered = _activeNavItem switch
        {
            "myday" => Tasks
                .Where(t => t.IsInMyDay)
                .OrderByDescending(t => t.CreatedAt)
                .ThenBy(t => t.IsCompleted),
            "important" => Tasks.Where(t => t.IsStarred).OrderByDescending(t => t.CreatedAt),
            "planned"   => Tasks.Where(t => t.DueDate != null && !t.IsCompleted).OrderBy(t => t.DueDate),
            _           => Tasks.Where(MatchesFilter).OrderByDescending(t => t.CreatedAt)
        };

        foreach (var task in filtered)
            ActiveTasks.Add(task);

        RebuildFlatGroups();
        OnPropertyChanged(nameof(IsTaskListEmpty));
    }

    private void RebuildFlatGroups()
    {
        _openGroup.Items.Clear();
        _completedGroup.Items.Clear();

        foreach (var task in ActiveTasks.Where(t => !t.IsCompleted))
            _openGroup.Items.Add(task);

        foreach (var task in ActiveTasks.Where(t => t.IsCompleted))
            _completedGroup.Items.Add(task);
    }

    private void MoveBetweenFlatGroups(TodoItem task)
    {
        if (task.IsCompleted)
        {
            _openGroup.Items.Remove(task);
            if (!_completedGroup.Items.Contains(task))
                _completedGroup.Items.Add(task);
        }
        else
        {
            _completedGroup.Items.Remove(task);
            if (!_openGroup.Items.Contains(task))
                _openGroup.Items.Insert(0, task);
        }
    }

    public IList<PlannedGroup> PlannedGroups
    {
        get
        {
            // Design decision: tasks without a due date are intentionally excluded from
            // Planned. Planned is a time-based planning view — undated tasks belong in
            // All Tasks. The empty-state subtext already guides users to set a due date.
            var groups = new List<PlannedGroup>();
            var today = DateTimeOffset.Now.Date;
            var tomorrow = today.AddDays(1);
            var weekEnd = today.AddDays(7 - (int)today.DayOfWeek);

            var tasksWithDue = Tasks.Where(t => t.DueDate != null && !t.IsCompleted).GroupBy(t =>
            {
                var dueDate = t.DueDate!.Value.ToLocalTime().Date;
                if (dueDate < today)    return Strings.PlannedGroup_Overdue;
                if (dueDate == today)   return Strings.PlannedGroup_Today;
                if (dueDate == tomorrow) return Strings.PlannedGroup_Tomorrow;
                if (dueDate <= weekEnd) return Strings.PlannedGroup_ThisWeek;
                return Strings.PlannedGroup_Later;
            }).OrderBy(g => Array.IndexOf(
                new[] {
                    Strings.PlannedGroup_Overdue,
                    Strings.PlannedGroup_Today,
                    Strings.PlannedGroup_Tomorrow,
                    Strings.PlannedGroup_ThisWeek,
                    Strings.PlannedGroup_Later
                }, g.Key));

            foreach (var group in tasksWithDue)
            {
                groups.Add(new PlannedGroup
                {
                    Name = group.Key,
                    Items = new ObservableCollection<TodoItem>(group.OrderBy(t => t.DueDate))
                });
            }

            return groups;
        }
    }

    public IList<CompletedTaskGroup> FlatGroupedTasks => _flatGroupedTasks;

    public ICommand AddTaskCommand { get; }

    public MainViewModel()
    {
        _storage = new TaskStorageService();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AddTaskCommand = new RelayCommand(
            _ => AddTask(),
            _ => !string.IsNullOrWhiteSpace(NewTaskText));

        UndoLastCompletionCommand = new RelayCommand(_ => UndoLastCompletion());

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
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TodoItem task in e.OldItems)
                    ActiveTasks.Remove(task);
                RebuildFlatGroups();
            }
            else
            {
                RefreshActiveTasks();
            }

            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(PlannedGroups));
            OnPropertyChanged(nameof(IsPlannedEmpty));
            if (_activeNavItem == "planned")
                OnPropertyChanged(nameof(ActiveNavItem));
        };

        _ = LoadAsync();

        App.SyncService.TasksReceived += () =>
            _dispatcherQueue.TryEnqueue(async () => await ReloadAsync());
    }

    public async Task ReloadAsync()
    {
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
            _isBulkLoading = true;
            foreach (var task in data.Tasks.OrderByDescending(t => t.CreatedAt))
            {
                AttachTaskPropertyChangedHandler(task);
                Tasks.Add(task);
            }
            _isBulkLoading = false;

            // Load lists sorted: pinned first, then ascending SortOrder
            foreach (var list in data.Lists.OrderByDescending(l => l.IsPinned).ThenBy(l => l.SortOrder))
                CustomLists.Add(list);

            RefreshActiveTasks();
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(PlannedGroups));
            OnPropertyChanged(nameof(IsPlannedEmpty));
        }
        catch { _isBulkLoading = false; }
    }

    private void AddTask()
    {
        var task = new TodoItem { Title = NewTaskText.Trim() };

        switch (_activeNavItem)
        {
            case "myday":
                task.IsInMyDay = true;
                break;
            case "important":
                task.IsStarred = true;
                break;
            case "planned":
                task.DueDate = new DateTimeOffset(DateTime.Today);
                break;
            default:
                if (Guid.TryParse(_activeNavItem, out var listId))
                    task.ListId = listId;
                break;
        }

        AttachTaskPropertyChangedHandler(task);
        Tasks.Insert(0, task);
        NewTaskText = string.Empty;
        SaveAsync();
    }

    // ── List CRUD ────────────────────────────────────────────────────────────

    public void AddList(string name)
    {
        var list = new TaskList
        {
            Name = name.Trim(),
            AccentColor = "#0078D4",
            SortOrder = CustomLists.Count
        };
        CustomLists.Add(list);
        SaveAsync();
    }

    public void RenameList(TaskList list, string newName)
    {
        list.Name = newName.Trim();
        SaveAsync();
    }

    public void SetListIcon(TaskList list, string? icon)
    {
        list.CustomIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        SaveAsync();
    }

    public void TogglePinList(TaskList list)
    {
        list.IsPinned = !list.IsPinned;
        var sorted = CustomLists.OrderByDescending(l => l.IsPinned).ThenBy(l => l.SortOrder).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = CustomLists.IndexOf(sorted[i]);
            if (current != i) CustomLists.Move(current, i);
        }
        SaveAsync();
    }

    public void ReorderList(Guid id, int newSectionIndex, bool newIsPinned)
    {
        var list = CustomLists.FirstOrDefault(l => l.Id == id);
        if (list == null) return;

        bool oldIsPinned = list.IsPinned;
        list.IsPinned = newIsPinned;

        var targetSection = CustomLists
            .Where(l => l.IsPinned == newIsPinned && l.Id != id)
            .OrderBy(l => l.SortOrder)
            .ToList();
        targetSection.Insert(Math.Clamp(newSectionIndex, 0, targetSection.Count), list);
        for (int i = 0; i < targetSection.Count; i++)
            targetSection[i].SortOrder = i;

        if (oldIsPinned != newIsPinned)
        {
            var oldSection = CustomLists
                .Where(l => l.IsPinned == oldIsPinned)
                .OrderBy(l => l.SortOrder)
                .ToList();
            for (int i = 0; i < oldSection.Count; i++)
                oldSection[i].SortOrder = i;
        }

        var sorted = CustomLists.OrderByDescending(l => l.IsPinned).ThenBy(l => l.SortOrder).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = CustomLists.IndexOf(sorted[i]);
            if (current != i) CustomLists.Move(current, i);
        }

        SaveAsync();
    }

    public void MoveListUp(TaskList list)
    {
        var section = CustomLists.Where(l => l.IsPinned == list.IsPinned).OrderBy(l => l.SortOrder).ToList();
        int idx = section.IndexOf(list);
        if (idx <= 0) return;
        ReorderList(list.Id, idx - 1, list.IsPinned);
    }

    public void MoveListDown(TaskList list)
    {
        var section = CustomLists.Where(l => l.IsPinned == list.IsPinned).OrderBy(l => l.SortOrder).ToList();
        int idx = section.IndexOf(list);
        if (idx < 0 || idx >= section.Count - 1) return;
        ReorderList(list.Id, idx + 1, list.IsPinned);
    }

    public int GetTaskCountForList(TaskList list) => Tasks.Count(t => t.ListId == list.Id);

    public void DeleteList(TaskList list)
    {
        // Navigate away before removing tasks so the list view doesn't flicker.
        if (_activeNavItem == list.Id.ToString())
            ActiveNavItem = "alltasks";

        var tasksToRemove = Tasks.Where(t => t.ListId == list.Id).ToList();
        foreach (var task in tasksToRemove)
        {
            task.PropertyChanged -= TaskPropertyChanged;
            Tasks.Remove(task);
        }

        CustomLists.Remove(list);
        SaveAsync();
    }

    // ────────────────────────────────────────────────────────────────────────

    public void AttachTaskPropertyChangedHandler(TodoItem task)
    {
        task.PropertyChanged -= TaskPropertyChanged;
        task.PropertyChanged += TaskPropertyChanged;
    }

    private void TaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TodoItem task) return;

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
                    ApplyCompletedChange(task);
                else if (e.PropertyName == nameof(TodoItem.DueDate))
                    ApplyDueDateChange(task);
                else if (e.PropertyName == nameof(TodoItem.IsStarred))
                    ApplyStarredChange(task);
                else
                {
                    RefreshActiveTasks();
                    OnPropertyChanged(nameof(IsTaskListEmpty));
                    OnPropertyChanged(nameof(PlannedGroups));
                    OnPropertyChanged(nameof(IsPlannedEmpty));
                }
            });
        }

        SaveAsync();
    }

    // Surgical update for IsCompleted — avoids Clear()+rebuild which causes item
    // container destruction and the resulting checkbox blink / access violation.
    private void ApplyCompletedChange(TodoItem task)
    {
        switch (_activeNavItem)
        {
            case "planned":
                // Planned filters out completed tasks entirely.
                if (task.IsCompleted)
                    ActiveTasks.Remove(task);
                else if (!ActiveTasks.Contains(task) && task.DueDate != null)
                    ActiveTasks.Add(task);   // unchecked: re-insert (order refresh below)
                OnPropertyChanged(nameof(PlannedGroups));
                OnPropertyChanged(nameof(IsPlannedEmpty));
                OnPropertyChanged(nameof(IsTaskListEmpty));
                break;

            case "myday":
            case "important":
            case "alltasks":
            default:
                // Delay the group move so the strikethrough/fade animation is visible
                // before the task moves between groups.
                if (task.IsCompleted)
                    _lastCompletedTask = task;

                var timer = _dispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(250);
                timer.IsRepeating = false;
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    FlatGroupMoveStarting?.Invoke();
                    MoveBetweenFlatGroups(task);
                    FlatGroupMoveCompleted?.Invoke();
                    OnPropertyChanged(nameof(IsTaskListEmpty));

                    if (task.IsCompleted)
                        ShowUndoBar();
                };
                timer.Start();
                break;
        }
    }

    public event Action? FlatGroupMoveStarting;
    public event Action? FlatGroupMoveCompleted;

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
        IsUndoBarVisible = false;
    }

    private void UndoLastCompletion()
    {
        if (_lastCompletedTask is not { IsCompleted: true } task)
        {
            DismissUndoBar();
            return;
        }

        task.IsCompleted = false;
        DismissUndoBar();
    }

    // Surgical update for DueDate — avoids full Clear()+rebuild.
    // x:Bind already re-evaluates the date chip on the row; we only need to
    // touch ActiveTasks when the view's filter/membership actually changes.
    private void ApplyDueDateChange(TodoItem task)
    {
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
                OnPropertyChanged(nameof(PlannedGroups));
                OnPropertyChanged(nameof(IsPlannedEmpty));
                OnPropertyChanged(nameof(IsTaskListEmpty));
                break;

            case "alltasks":
            case "myday":
            case "important":
            default:
                // DueDate doesn't affect membership or sort order in these views.
                // x:Bind converters on the chip handle the visual update automatically.
                OnPropertyChanged(nameof(PlannedGroups));
                break;
        }
    }

    private void ApplyStarredChange(TodoItem task)
    {
        switch (_activeNavItem)
        {
            case "important":
                // Important view only shows starred tasks — add or remove accordingly.
                bool shouldBeInImportant = task.IsStarred;
                bool isInImportant = ActiveTasks.Contains(task);
                if (shouldBeInImportant && !isInImportant)
                    ActiveTasks.Insert(0, task);
                else if (!shouldBeInImportant && isInImportant)
                    ActiveTasks.Remove(task);
                OnPropertyChanged(nameof(IsTaskListEmpty));
                break;

            default:
                // All other views keep the task regardless — the star glyph updates
                // automatically via x:Bind. No list rebuild needed.
                break;
        }
    }

    public void DeleteTask(TodoItem task)
    {
        task.PropertyChanged -= TaskPropertyChanged;
        Tasks.Remove(task);
        SaveAsync();
    }

    public void UpdateTask(TodoItem task, string newTitle, DateTimeOffset? newDueDate, bool newStarred)
    {
        // Unsubscribe while applying all changes to avoid multiple intermediate
        // refreshes firing for each property — one single refresh at the end.
        task.PropertyChanged -= TaskPropertyChanged;

        task.Title     = newTitle;
        task.DueDate   = newDueDate;
        task.IsStarred = newStarred;

        task.PropertyChanged += TaskPropertyChanged;

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
            OnPropertyChanged(nameof(PlannedGroups));
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

    public bool IsCompletedGroupExpanded(string navItem)
    {
        return _completedGroupExpandedState.TryGetValue(navItem, out var expanded) && expanded;
    }

    public void SetCompletedGroupExpanded(string navItem, bool expanded)
    {
        _completedGroupExpandedState[navItem] = expanded;
    }

    public void SaveAsync()
    {
        _saveCancelToken?.Cancel();
        _saveCancelToken = new CancellationTokenSource();
        _ = DoSaveAsync(_saveCancelToken.Token);
    }

    private async Task DoSaveAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            var data = new TasksFile { Tasks = [.. Tasks], Lists = [.. CustomLists] };
            await _storage.SaveAsync(data);
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record PlannedGroup
{
    public required string Name { get; init; }
    public required ObservableCollection<TodoItem> Items { get; init; }
}
