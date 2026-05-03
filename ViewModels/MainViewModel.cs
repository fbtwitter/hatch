using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TodoWinUI3.Models;
using TodoWinUI3.Services;

namespace TodoWinUI3.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly TaskStorageService _storage;
    private string _newTaskText = string.Empty;

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

    public bool IsTaskListEmpty => Tasks.Count == 0;

    public ICommand AddTaskCommand { get; }

    public MainViewModel()
    {
        _storage = new TaskStorageService();

        AddTaskCommand = new RelayCommand(
            _ => AddTask(),
            _ => !string.IsNullOrWhiteSpace(NewTaskText));

        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsTaskListEmpty));

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
