using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Hatch.Models;

public sealed class TodoItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isCompleted;
    private bool _isStarred;
    private bool _isInMyDay;
    private DateTimeOffset? _dueDate;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set { _isCompleted = value; OnPropertyChanged(); }
    }

    public bool IsStarred
    {
        get => _isStarred;
        set { _isStarred = value; OnPropertyChanged(); }
    }

    public bool IsInMyDay
    {
        get => _isInMyDay;
        set { _isInMyDay = value; OnPropertyChanged(); }
    }

    public DateTimeOffset? DueDate
    {
        get => _dueDate;
        set { _dueDate = value; OnPropertyChanged(); }
    }

    public Guid ListId { get; set; } = Guid.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Re-raises PropertyChanged for DueDate so theme-sensitive converters
    /// re-evaluate without modifying the collection.
    /// </summary>
    public void RefreshDueDateBinding()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DueDate)));
}
