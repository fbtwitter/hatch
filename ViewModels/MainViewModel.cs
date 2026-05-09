using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskStorageService _storage;
    private string _newTaskText = string.Empty;
    private string _activeNavItem = "alltasks";
    private CancellationTokenSource? _saveCancelToken;

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

    public string ActiveNavItem
    {
        get => _activeNavItem;
        set
        {
            if (_activeNavItem == value) return;
            _activeNavItem = value;
            OnPropertyChanged();
            RefreshActiveTasks();
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(PlannedGroups));
            App.Settings.ActiveNavItem = value;
            _ = App.SettingsService.SaveAsync();
        }
    }

    private void RefreshActiveTasks()
    {
        ActiveTasks.Clear();
        var filtered = _activeNavItem switch
        {
            "myday" => Tasks.Where(t => t.IsInMyDay).OrderByDescending(t => t.CreatedAt),
            "important" => Tasks.Where(t => t.IsStarred).OrderByDescending(t => t.CreatedAt),
            "planned" => Tasks.Where(t => t.DueDate != null).OrderBy(t => t.DueDate),
            _ => Tasks.OrderByDescending(t => t.CreatedAt)
        };

        foreach (var task in filtered)
            ActiveTasks.Add(task);
    }

    public IList<PlannedGroup> PlannedGroups
    {
        get
        {
            var groups = new List<PlannedGroup>();
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var weekEnd = today.AddDays(7 - (int)today.DayOfWeek);

            var tasksWithDue = Tasks.Where(t => t.DueDate != null).GroupBy(t =>
            {
                var dueDate = t.DueDate!.Value.Date;
                if (dueDate == today) return "Today";
                if (dueDate == tomorrow) return "Tomorrow";
                if (dueDate <= weekEnd) return "This week";
                return "Later";
            }).OrderBy(g => Array.IndexOf(new[] { "Today", "Tomorrow", "This week", "Later" }, g.Key));

            foreach (var group in tasksWithDue)
            {
                groups.Add(new PlannedGroup
                {
                    Name = group.Key,
                    Items = new ObservableCollection<TodoItem>(group.OrderByDescending(t => t.CreatedAt))
                });
            }

            return groups;
        }
    }

    public ICommand AddTaskCommand { get; }

    public MainViewModel()
    {
        _storage = new TaskStorageService();

        AddTaskCommand = new RelayCommand(
            _ => AddTask(),
            _ => !string.IsNullOrWhiteSpace(NewTaskText));

        Tasks.CollectionChanged += (_, _) =>
        {
            RefreshActiveTasks();
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(PlannedGroups));
        };

        _activeNavItem = App.Settings.ActiveNavItem;

        // Initialize default list
        Lists.Add(new TaskList { Name = "All Tasks", Id = Guid.Empty });

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var tasks = await _storage.LoadTasksAsync();
            foreach (var task in tasks.OrderByDescending(t => t.CreatedAt))
            {
                AttachTaskPropertyChangedHandler(task);
                Tasks.Add(task);
            }
            RefreshActiveTasks();
        }
        catch { }
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
                task.DueDate = DateTimeOffset.Now;
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

        // Only update ActiveTasks if the property affects the current view's filter
        bool affectsCurrentView = _activeNavItem switch
        {
            "myday" => e.PropertyName == nameof(TodoItem.IsInMyDay),
            "important" => e.PropertyName == nameof(TodoItem.IsStarred),
            "planned" => e.PropertyName == nameof(TodoItem.DueDate),
            _ => false
        };

        if (affectsCurrentView)
        {
            bool matchesCurrentFilter = _activeNavItem switch
            {
                "myday" => task.IsInMyDay,
                "important" => task.IsStarred,
                "planned" => task.DueDate != null,
                _ => true
            };

            bool isInActiveView = ActiveTasks.Contains(task);

            // Add task if it now matches the filter
            if (matchesCurrentFilter && !isInActiveView)
                ActiveTasks.Add(task);
            // Remove task if it no longer matches the filter
            else if (!matchesCurrentFilter && isInActiveView)
                ActiveTasks.Remove(task);
        }

        SaveAsync();
    }

    public void DeleteTask(TodoItem task)
    {
        task.PropertyChanged -= TaskPropertyChanged;
        Tasks.Remove(task);
        SaveAsync();
    }


    public void UpdateTaskTitle(TodoItem task, string newTitle)
    {
        task.Title = newTitle;
        SaveAsync();
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
