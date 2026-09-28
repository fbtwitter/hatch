using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hatch.Models;

// ADR-0010 — one ordered step (subtask / checklist item) within a TodoItem.
// Carried through load/save and sync as its own object; per-step Id/UpdatedAt are stored
// now even though the protocol merges at parent granularity (whole-record LWW on
// TodoItem.UpdatedAt), so a later ADR can move to per-step merge without another wire break.
// Title/IsCompleted raise PropertyChanged so the details-pane checklist updates in place
// without rebuilding the list on every toggle.
public sealed class Step : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    private bool _isCompleted;
    public bool IsCompleted
    {
        get => _isCompleted;
        set { _isCompleted = value; OnPropertyChanged(); }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
