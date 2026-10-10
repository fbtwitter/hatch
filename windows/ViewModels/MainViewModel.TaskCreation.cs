using System.Windows.Input;
using System.Globalization;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private DateTimeOffset? _newTaskDueDate;
    private bool _newTaskImportant;
    private bool _hasNewTaskDueDateChoice;
    private bool _hasNewTaskImportantChoice;
    private bool _isTaskComposerVisible = true;
    private bool _newTaskCalendarOpen;
    private double _taskComposerScrollOffset;

    public int TaskComposerRow => IsTaskComposerFloating ? 3 : 2;
    public bool IsTaskComposerFloating => _settingsService.Current.TaskInputPosition == TaskInputPosition.Bottom;
    public bool IsTaskComposerVisible
    {
        get => _isTaskComposerVisible;
        private set
        {
            if (_isTaskComposerVisible == value) return;
            _isTaskComposerVisible = value;
            OnPropertyChanged();
        }
    }
    public ICommand ClearNewTaskDueDateCommand { get; }
    public bool CanChangeNewTaskImportant => _activeNavItem != "important";
    public bool IsNewTaskDueDateRequired => _activeNavItem == "planned";
    public bool CanClearNewTaskDueDate => HasNewTaskDueDate && !IsNewTaskDueDateRequired;

    public DateTimeOffset? NewTaskDueDate
    {
        get => _hasNewTaskDueDateChoice && _newTaskDueDate.HasValue ? _newTaskDueDate
            : IsNewTaskDueDateRequired
                ? new DateTimeOffset(DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified), TimeSpan.Zero)
                : null;
        set
        {
            if (IsNewTaskDueDateRequired && value == null) return;
            value = value is { } date ? new DateTimeOffset(date.Date, TimeSpan.Zero) : null;
            if (_hasNewTaskDueDateChoice && _newTaskDueDate == value) return;
            _newTaskDueDate = value;
            _hasNewTaskDueDateChoice = true;
            NotifyNewTaskDetailsChanged();
        }
    }

    public bool NewTaskImportant
    {
        get => !CanChangeNewTaskImportant || (_hasNewTaskImportantChoice && _newTaskImportant);
        set
        {
            if (!CanChangeNewTaskImportant) return;
            if (NewTaskImportant == value) return;
            _newTaskImportant = value;
            _hasNewTaskImportantChoice = true;
            NotifyNewTaskDetailsChanged();
        }
    }

    public bool HasNewTaskDueDate => NewTaskDueDate.HasValue;
    public string NewTaskDueDateLabel => NewTaskDueDate?.ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
    public string NewTaskDueDateTooltip => NewTaskDueDate is { } date
        ? IsNewTaskDueDateRequired
            ? Strings.NewTask_DueDateRequired(date.ToString("D", CultureInfo.CurrentCulture))
            : Strings.NewTask_DueDateSelected(date.ToString("D", CultureInfo.CurrentCulture))
        : Strings.NewTask_SetDueDate;
    public string NewTaskImportantGlyph => NewTaskImportant ? "\uE735" : "\uE734";
    public string NewTaskImportantTooltip => !CanChangeNewTaskImportant
        ? Strings.NewTask_ImportantRequired
        : NewTaskImportant ? Strings.Task_Tooltip_Star_Remove : Strings.Task_Tooltip_Star_Add;

    public string NewTaskPlaceholderText => _activeNavItem switch
    {
        "myday" => Strings.NewTask_Placeholder_MyDay,
        "important" => Strings.NewTask_Placeholder_Important,
        "planned" => Strings.NewTask_Placeholder_Planned,
        _ => Strings.NewTask_Placeholder
    };

    public string NewTaskDestinationText
    {
        get
        {
            string destination = _activeNavItem switch
            {
                "myday" => Strings.Header_MyDay,
                "important" => Strings.Header_Important,
                "planned" => Strings.Header_Planned,
                _ => CustomLists.FirstOrDefault(list => list.Id.ToString() == _activeNavItem)?.Name
                    ?? Strings.List_Default_Name
            };
            return Strings.NewTask_Destination(destination);
        }
    }

    public void NotifyTaskInputPositionChanged()
    {
        OnPropertyChanged(nameof(TaskComposerRow));
        OnPropertyChanged(nameof(IsTaskComposerFloating));
        ResetTaskComposerScroll();
    }

    public void ResetTaskComposerScroll(double offset = 0)
    {
        _taskComposerScrollOffset = offset;
        RevealTaskComposer();
    }

    public void RevealTaskComposer() => IsTaskComposerVisible = true;

    public void SetNewTaskCalendarOpen(bool open)
    {
        _newTaskCalendarOpen = open;
        if (open) RevealTaskComposer();
    }

    public void UpdateTaskComposerScroll(double offset)
    {
        double delta = offset - _taskComposerScrollOffset;
        if (!IsTaskComposerFloating || _newTaskCalendarOpen ||
            NewTaskText.Length > 0 || (_hasNewTaskDueDateChoice && _newTaskDueDate.HasValue) ||
            (_hasNewTaskImportantChoice && _newTaskImportant) || offset <= 2)
        {
            _taskComposerScrollOffset = offset;
            RevealTaskComposer();
            return;
        }

        if (Math.Abs(delta) > 2)
        {
            _taskComposerScrollOffset = offset;
            IsTaskComposerVisible = delta < 0;
        }
    }

    private void ClearNewTaskDueDate()
    {
        if (!CanClearNewTaskDueDate) return;
        _newTaskDueDate = null;
        _hasNewTaskDueDateChoice = true;
        NotifyNewTaskDetailsChanged();
    }

    private void ResetNewTaskDetails()
    {
        _newTaskDueDate = null;
        _newTaskImportant = false;
        _hasNewTaskDueDateChoice = false;
        _hasNewTaskImportantChoice = false;
        NotifyNewTaskDetailsChanged();
    }

    private void NotifyNewTaskDetailsChanged()
    {
        RevealTaskComposer();
        OnPropertyChanged(nameof(CanChangeNewTaskImportant));
        OnPropertyChanged(nameof(IsNewTaskDueDateRequired));
        OnPropertyChanged(nameof(CanClearNewTaskDueDate));
        OnPropertyChanged(nameof(NewTaskPlaceholderText));
        OnPropertyChanged(nameof(NewTaskDueDate));
        OnPropertyChanged(nameof(NewTaskImportant));
        OnPropertyChanged(nameof(NewTaskImportantGlyph));
        OnPropertyChanged(nameof(NewTaskImportantTooltip));
        OnPropertyChanged(nameof(HasNewTaskDueDate));
        OnPropertyChanged(nameof(NewTaskDueDateLabel));
        OnPropertyChanged(nameof(NewTaskDueDateTooltip));
        ((RelayCommand)ClearNewTaskDueDateCommand).RaiseCanExecuteChanged();
    }
}
