using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isPlannedCalendar;
    private DateTimeOffset _selectedPlannedDate = PlannedDate(DateTime.Today);
    private readonly Dictionary<DateTime, int> _plannedDateCounts = [];
    private int _plannedOverdueCount;

    public ObservableCollection<TodoItem> PlannedDayTasks { get; } = [];
    public ICommand SelectPlannedViewCommand { get; }
    public bool IsPlannedPage => _activeNavItem == "planned";
    public bool IsPlannedCalendarVisible => IsPlannedPage && IsPlannedCalendar;
    public bool HasPlannedCalendarOverdue => IsPlannedCalendarVisible && _plannedOverdueCount > 0;
    public bool IsPlannedDayEmpty => IsLoaded && PlannedDayTasks.Count == 0;
    public string PlannedOverdueLabel => Strings.PlannedCalendar_Overdue(_plannedOverdueCount);
    public string PlannedSelectedDateLabel => SelectedPlannedDate.ToString("D", CultureInfo.CurrentCulture);
    public string PlannedDayCountLabel => Strings.PlannedCalendar_TaskCount(PlannedDayTasks.Count);
    public int PlannedCalendarVersion { get; private set; }

    public bool IsPlannedCalendar
    {
        get => _isPlannedCalendar;
        set
        {
            if (_isPlannedCalendar == value) return;
            PreserveTaskDraftDueDate();
            _isPlannedCalendar = value;
            SelectedTask = null;
            NotifyPlannedViewChanged();
            ResetTaskComposerScroll();
            NotifyNewTaskDetailsChanged();
        }
    }

    public DateTimeOffset SelectedPlannedDate
    {
        get => _selectedPlannedDate;
        set
        {
            value = PlannedDate(value.Date);
            if (_selectedPlannedDate == value) return;
            PreserveTaskDraftDueDate();
            _selectedPlannedDate = value;
            SelectedTask = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlannedSelectedDateLabel));
            RefreshPlannedCalendar(rebuildDateCounts: false);
            ResetTaskComposerScroll();
            NotifyNewTaskDetailsChanged();
        }
    }

    public void ResetPlannedView()
    {
        PreserveTaskDraftDueDate();
        _isPlannedCalendar = false;
        _selectedPlannedDate = PlannedDate(DateTime.Today);
        NotifyPlannedViewChanged();
        OnPropertyChanged(nameof(SelectedPlannedDate));
        OnPropertyChanged(nameof(PlannedSelectedDateLabel));
        RefreshPlannedCalendar();
        NotifyNewTaskDetailsChanged();
    }

    public void ShowPlannedOverdue() => IsPlannedCalendar = false;
    public void SelectPlannedToday() => SelectedPlannedDate = PlannedDate(DateTime.Today);

    public int GetPlannedTaskCount(DateTimeOffset date) => _plannedDateCounts.GetValueOrDefault(date.Date);

    private static DateTimeOffset PlannedDate(DateTime date) =>
        new(DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified), TimeSpan.Zero);

    private bool MatchesPlannedTask(TodoItem task) =>
        !task.IsCompleted && task.DueDate.HasValue &&
        (_activeTagFilter == null || task.Tags.Contains(_activeTagFilter, StringComparer.OrdinalIgnoreCase));

    private void NotifyPlannedViewChanged()
    {
        OnPropertyChanged(nameof(IsPlannedPage));
        OnPropertyChanged(nameof(IsPlannedCalendar));
        OnPropertyChanged(nameof(IsPlannedCalendarVisible));
        OnPropertyChanged(nameof(HasPlannedCalendarOverdue));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void RefreshPlannedCalendar(bool rebuildDateCounts = true)
    {
        if (rebuildDateCounts)
        {
            _plannedDateCounts.Clear();
            _plannedOverdueCount = 0;
        }
        var selectedDayTasks = new List<TodoItem>();
        if (IsPlannedPage)
        {
            foreach (var task in Tasks.Where(MatchesPlannedTask))
            {
                var date = task.DueDate!.Value.Date;
                if (rebuildDateCounts)
                {
                    _plannedDateCounts[date] = _plannedDateCounts.GetValueOrDefault(date) + 1;
                    if (date < DateTime.Today) _plannedOverdueCount++;
                }
                if (date == SelectedPlannedDate.Date) selectedDayTasks.Add(task);
            }
        }

        for (int i = PlannedDayTasks.Count - 1; i >= 0; i--)
            if (!selectedDayTasks.Contains(PlannedDayTasks[i])) PlannedDayTasks.RemoveAt(i);
        for (int i = 0; i < selectedDayTasks.Count; i++)
        {
            var task = selectedDayTasks[i];
            int current = PlannedDayTasks.IndexOf(task);
            if (current < 0) PlannedDayTasks.Insert(i, task);
            else if (current != i) PlannedDayTasks.Move(current, i);
        }

        if (rebuildDateCounts)
        {
            PlannedCalendarVersion++;
            OnPropertyChanged(nameof(PlannedCalendarVersion));
            OnPropertyChanged(nameof(PlannedOverdueLabel));
            OnPropertyChanged(nameof(HasPlannedCalendarOverdue));
        }
        OnPropertyChanged(nameof(PlannedDayCountLabel));
        OnPropertyChanged(nameof(IsPlannedDayEmpty));
    }
}
