using System.ComponentModel;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed class QuickAddViewModel : INotifyPropertyChanged
{
    private bool _isImportant;

    public bool IsImportant
    {
        get => _isImportant;
        set
        {
            if (_isImportant == value) return;
            _isImportant = value;
            PropertyChanged?.Invoke(this, new(nameof(IsImportant)));
            PropertyChanged?.Invoke(this, new(nameof(ImportantGlyph)));
            PropertyChanged?.Invoke(this, new(nameof(ImportantTooltip)));
        }
    }

    public string ImportantGlyph => IsImportant ? "\uE735" : "\uE734";
    public string ImportantTooltip => IsImportant ? Strings.Task_Tooltip_Star_Remove : Strings.Task_Tooltip_Star_Add;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddTask(MainViewModel mainVm, string title, TaskList list, int datePresetIndex)
    {
        var today = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        var task = new TodoItem
        {
            Title = title,
            ListId = list.Id,
            ListName = list.Name,
            IsStarred = IsImportant,
            DueDate = datePresetIndex switch
            {
                1 => new DateTimeOffset(DueDatePresets.GetToday(today), TimeSpan.Zero),
                2 => new DateTimeOffset(DueDatePresets.GetTomorrow(today), TimeSpan.Zero),
                3 => new DateTimeOffset(DueDatePresets.GetThisWeekend(today), TimeSpan.Zero),
                4 => new DateTimeOffset(DueDatePresets.GetNextWeek(today), TimeSpan.Zero),
                _ => null
            }
        };

        mainVm.Tasks.Insert(0, task);
        mainVm.AttachTaskPropertyChangedHandler(task);
        mainVm.SaveAsync();
        IsImportant = false;
    }
}
