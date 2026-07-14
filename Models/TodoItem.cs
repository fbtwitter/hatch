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
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            CompletedAt = value ? DateTimeOffset.Now : null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowAddDateHint));
        }
    }

    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsStarred
    {
        get => _isStarred;
        set
        {
            if (_isStarred == value) return;
            _isStarred = value;
            OnPropertyChanged();
        }
    }

    public bool IsInMyDay
    {
        get => _isInMyDay;
        set
        {
            if (_isInMyDay == value) return;
            _isInMyDay = value;
            OnPropertyChanged();
        }
    }

    private DateOnly? _myDayDate;
    public DateOnly? MyDayDate
    {
        get => _myDayDate;
        set { _myDayDate = value; OnPropertyChanged(); }
    }

    // The one place the My Day membership/date pairing rule lives: adding stamps
    // today, removing clears the date. Callers must not set the two independently.
    public void SetMyDay(bool on)
    {
        IsInMyDay = on;
        MyDayDate = on ? DateOnly.FromDateTime(DateTime.Today) : null;
    }

    // Used only by daily reset — clears IsInMyDay without touching MyDayDate,
    // so yesterday's date is preserved for suggestion tracking.
    internal void ResetMyDayForNewDay()
    {
        _isInMyDay = false;
        OnPropertyChanged(nameof(IsInMyDay));
    }

    public DateTimeOffset? DueDate
    {
        get => _dueDate;
        set { _dueDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMetaSeparator)); OnPropertyChanged(nameof(ShowAddDateHint)); OnPropertyChanged(nameof(HasListNameToDateSeparator)); }
    }

    public Guid ListId { get; set; } = Guid.Empty;

    private TaskRecurrence _recurrence = TaskRecurrence.None;
    public TaskRecurrence Recurrence
    {
        get => _recurrence;
        set { _recurrence = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRecurrence)); }
    }

    public bool HasRecurrence => _recurrence != TaskRecurrence.None;

    private TaskPriority _priority = TaskPriority.None;
    public TaskPriority Priority
    {
        get => _priority;
        set { _priority = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPriority)); }
    }

    public bool HasPriority => _priority != TaskPriority.None;

    private List<string> _tags = [];
    public List<string> Tags
    {
        get => _tags;
        set { _tags = value ?? []; OnPropertyChanged(); OnPropertyChanged(nameof(HasMetaSeparator)); }
    }

    private string? _listName;

    [JsonIgnore]
    public string? ListName
    {
        get => _listName;
        set
        {
            _listName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasListName));
            OnPropertyChanged(nameof(HasListNameToDateSeparator));
            OnPropertyChanged(nameof(HasMetaSeparator));
        }
    }

    public bool HasListName => _listName != null;
    public bool HasListNameToDateSeparator => _listName != null && _dueDate != null;
    public bool HasMetaSeparator => (_dueDate != null || _listName != null) && _tags.Count > 0;
    public bool ShowAddDateHint => !IsCompleted && DueDate == null;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Stamped explicitly by MainViewModel on real edits (not by property setters here) —
    // JSON deserialization must restore the persisted value untouched, and cosmetic
    // OnPropertyChanged re-raises (e.g. RefreshDueDateBinding) must not count as edits.
    // Used by SyncMerge to resolve which side wins when the same task changed on both.
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

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
