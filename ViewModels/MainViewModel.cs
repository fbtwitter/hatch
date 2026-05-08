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

    public ObservableCollection<TodoItem> Tasks { get; } = [];

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
            OnPropertyChanged(nameof(ActiveTasks));
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(PlannedGroups));
            App.Settings.ActiveNavItem = value;
            _ = App.SettingsService.SaveAsync();
        }
    }

    public IList<TodoItem> ActiveTasks
    {
        get => _activeNavItem switch
        {
            "myday" => new List<TodoItem>(Tasks.Where(t => t.IsInMyDay).OrderByDescending(t => t.CreatedAt)),
            "important" => new List<TodoItem>(Tasks.Where(t => t.IsStarred).OrderByDescending(t => t.CreatedAt)),
            "planned" => new List<TodoItem>(Tasks.Where(t => t.DueDate != null).OrderBy(t => t.DueDate)),
            _ => new List<TodoItem>(Tasks.OrderByDescending(t => t.CreatedAt))
        };
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
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(ActiveTasks));
            OnPropertyChanged(nameof(PlannedGroups));
        };

        _activeNavItem = App.Settings.ActiveNavItem;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var tasks = await _storage.LoadTasksAsync();
            foreach (var task in tasks.OrderByDescending(t => t.CreatedAt))
                Tasks.Add(task);
        }
        catch { }
    }

    private void AddTask()
    {
        Tasks.Insert(0, new TodoItem { Title = NewTaskText.Trim() });
        NewTaskText = string.Empty;
        _ = SaveAsync();
    }

    public void DeleteTask(TodoItem task)
    {
        Tasks.Remove(task);
        _ = SaveAsync();
    }

    public void SetTaskCompleted(TodoItem task, bool completed)
    {
        task.IsCompleted = completed;
        _ = SaveAsync();
    }

    public void UpdateTaskTitle(TodoItem task, string newTitle)
    {
        task.Title = newTitle;
        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        try { await _storage.SaveTasksAsync(Tasks); }
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
