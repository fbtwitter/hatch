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

    public ObservableCollection<TodoItem> Tasks { get; } = [];
    public ObservableCollection<TodoItem> ActiveTasks { get; } = [];
    public ObservableCollection<TaskList> Lists { get; } = [];

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

    public bool IsTaskListEmpty => ActiveTasks.Count == 0;

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
        _           => Strings.EmptyState_AllTasks_Headline
    };

    public string EmptyStateSubtext => _activeNavItem switch
    {
        "myday"     => Strings.EmptyState_MyDay_Subtext,
        "important" => Strings.EmptyState_Important_Subtext,
        "planned"   => Strings.EmptyState_Planned_Subtext,
        _           => Strings.EmptyState_AllTasks_Subtext
    };

    private bool MatchesFilter(TodoItem task) => _activeNavItem switch
    {
        "myday"     => task.IsInMyDay,
        "important" => task.IsStarred,
        "planned"   => task.DueDate != null && !task.IsCompleted,
        _           => true
    };

    private void RefreshActiveTasks()
    {
        ActiveTasks.Clear();
        var today = DateTimeOffset.Now.Date;
        var filtered = _activeNavItem switch
        {
            // For My Day, Important, All Tasks: maintain insertion order, let grouping handle open/completed separation
            "myday" => Tasks
                .Where(t => t.IsInMyDay)
                .OrderByDescending(t => t.CreatedAt)
                .ThenBy(t => t.IsCompleted),
            "important" => Tasks.Where(t => t.IsStarred).OrderByDescending(t => t.CreatedAt),
            "planned"   => Tasks.Where(t => t.DueDate != null && !t.IsCompleted).OrderBy(t => t.DueDate),
            _           => Tasks.OrderByDescending(t => t.CreatedAt)
        };

        foreach (var task in filtered)
            ActiveTasks.Add(task);

        OnPropertyChanged(nameof(IsTaskListEmpty));
        OnPropertyChanged(nameof(FlatGroupedTasks));
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

    public IList<CompletedTaskGroup> FlatGroupedTasks
    {
        get
        {
            var groups = new List<CompletedTaskGroup>();

            var openTasks = new ObservableCollection<TodoItem>(
                ActiveTasks.Where(t => !t.IsCompleted)
            );
            groups.Add(new CompletedTaskGroup
            {
                Name = "Open",
                Items = openTasks
            });

            var completedTasks = new ObservableCollection<TodoItem>(
                ActiveTasks.Where(t => t.IsCompleted)
            );

            if (completedTasks.Count > 0)
            {
                groups.Add(new CompletedTaskGroup
                {
                    Name = $"Completed ({completedTasks.Count})",
                    Items = completedTasks
                });
            }

            return groups;
        }
    }

    public ICommand AddTaskCommand { get; }

    public MainViewModel()
    {
        _storage = new TaskStorageService();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AddTaskCommand = new RelayCommand(
            _ => AddTask(),
            _ => !string.IsNullOrWhiteSpace(NewTaskText));

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
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (TodoItem task in e.OldItems)
                    ActiveTasks.Remove(task);
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

        // Initialize default list
        Lists.Add(new TaskList { Name = Strings.List_AllTasks_Name, Id = Guid.Empty });

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var tasks = await _storage.LoadTasksAsync();
            _isBulkLoading = true;
            foreach (var task in tasks.OrderByDescending(t => t.CreatedAt))
            {
                AttachTaskPropertyChangedHandler(task);
                Tasks.Add(task);
            }
            _isBulkLoading = false;
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

        // Set appropriate properties based on which page user is adding from
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
        }

        AttachTaskPropertyChangedHandler(task);
        Tasks.Insert(0, task);
        NewTaskText = string.Empty;
        SaveAsync();
    }

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
                // For flat views with grouping: task stays in ActiveTasks but moves between
                // open/completed groups via FlatGroupedTasks computation.
                OnPropertyChanged(nameof(FlatGroupedTasks));
                OnPropertyChanged(nameof(IsTaskListEmpty));
                break;
        }
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
            await _storage.SaveTasksAsync(Tasks);
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
