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
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowAddDateHint));
        }
    }

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
