using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;
using Microsoft.UI.Dispatching;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private string _activeNavItem = "alltasks";
    private string? _activeTagFilter;
    private List<PlannedGroup>? _cachedPlannedGroups;
    private readonly Dictionary<string, bool> _completedGroupExpandedState = new();

    private readonly CompletedTaskGroup _openGroup = new() { Name = "Open" };
    private readonly CompletedTaskGroup _completedGroup = new() { Name = "Completed", TrackCount = true };
    private readonly IList<CompletedTaskGroup> _flatGroupedTasks;

    public ICommand ClearTagFilterCommand { get; private set; } = null!;

    public bool IsTaskListEmpty => ActiveTasks.Count == 0;
    public bool IsPlannedEmpty => !Tasks.Any(t => t.DueDate != null && !t.IsCompleted);

    public string? ActiveTagFilter
    {
        get => _activeTagFilter;
        set
        {
            if (_activeTagFilter == value) return;
            _activeTagFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTagFilterActive));
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, RefreshActiveTasks);
        }
    }

    public bool IsTagFilterActive => _activeTagFilter != null;

    public string ActiveNavItem
    {
        get => _activeNavItem;
        set
        {
            if (_activeNavItem == value) return;
            _activeNavItem = value;
            _activeTagFilter = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTagFilter));
            OnPropertyChanged(nameof(IsTagFilterActive));
            OnPropertyChanged(nameof(IsTaskListEmpty));
            OnPropertyChanged(nameof(ShowEmptyState));
            NotifyPlannedGroupsChanged();
            OnPropertyChanged(nameof(IsPlannedEmpty));
            OnPropertyChanged(nameof(EmptyStateGlyph));
            OnPropertyChanged(nameof(EmptyStateHeadline));
            OnPropertyChanged(nameof(EmptyStateSubtext));
            _completedGroup.IsExpanded = IsCompletedGroupExpanded(value);
            _openGroup.IsCollapsible = value != "important";
            App.Settings.ActiveNavItem = value;
            App.SettingsService.SaveDebounced();
            OnPropertyChanged(nameof(SuggestionsVisible));
            OnPropertyChanged(nameof(ShowEmptyState));
            // Defer one frame so the page shell renders before the list rebuilds,
            // without the visible pause that Low priority causes mid-navigation.
            _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                RefreshActiveTasks();
                RefreshSuggestions();
            });
        }
    }

    public string EmptyStateGlyph => _activeNavItem switch
    {
        "myday"     => "\uE706", // Sun
        "important" => "\uE735", // Star
        "planned"   => "\uED28", // Calendar
        _           => "\uE762"  // Document
    };
    public string EmptyStateHeadline => _activeNavItem switch
    {
        "myday"     => Strings.EmptyState_MyDay_Headline,
        "important" => Strings.EmptyState_Important_Headline,
        "planned"   => Strings.EmptyState_Planned_Headline,
        _           => Guid.TryParse(_activeNavItem, out _)
                        ? Strings.EmptyState_CustomList_Headline
                        : Strings.EmptyState_AllTasks_Headline
    };

    public string EmptyStateSubtext => _activeNavItem switch
    {
        "myday"     => Strings.EmptyState_MyDay_Subtext,
        "important" => Strings.EmptyState_Important_Subtext,
        "planned"   => Strings.EmptyState_Planned_Subtext,
        _           => Guid.TryParse(_activeNavItem, out _)
                        ? Strings.EmptyState_CustomList_Subtext
                        : Strings.EmptyState_AllTasks_Subtext
    };

    private bool MatchesFilter(TodoItem task)
    {
        bool matchesNav = _activeNavItem switch
        {
            "myday"     => task.IsInMyDay,
            "important" => task.IsStarred && !task.IsCompleted,
            "planned"   => task.DueDate != null && !task.IsCompleted,
            _           => !Guid.TryParse(_activeNavItem, out var listId) || task.ListId == listId
        };
        return matchesNav &&
               (_activeTagFilter == null ||
                task.Tags.Contains(_activeTagFilter, StringComparer.OrdinalIgnoreCase));
    }

    private void RefreshActiveTasks()
    {
        ActiveTasks.Clear();
        var filtered = _activeNavItem switch
        {
            "myday" => Tasks
                .Where(t => t.IsInMyDay)
                .OrderByDescending(TaskSorting.CreatedInstant)
                .ThenBy(t => t.IsCompleted),
            // Important excludes completed tasks entirely, like Planned — a done task
            // isn't something to act on anymore, even if it's still starred.
            "important" => TaskSorting.ForImportant(Tasks.Where(t => t.IsStarred && !t.IsCompleted)),
            "planned"   => Tasks.Where(t => t.DueDate != null && !t.IsCompleted).OrderBy(t => t.DueDate),
            _           => TaskSorting.NewestFirst(Tasks.Where(MatchesFilter))
        };

        foreach (var task in filtered)
            ActiveTasks.Add(task);

        RebuildFlatGroups();
        OnPropertyChanged(nameof(IsTaskListEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        BadgeVersion++;
        OnPropertyChanged(nameof(BadgeVersion));
    }

    private void RebuildFlatGroups()
    {
        _openGroup.Items.Clear();
        _completedGroup.Items.Clear();

        foreach (var task in ActiveTasks.Where(t => !t.IsCompleted))
            _openGroup.Items.Add(task);

        foreach (var task in ActiveTasks.Where(t => t.IsCompleted))
            _completedGroup.Items.Add(task);
    }

    private void MoveBetweenFlatGroups(TodoItem task)
    {
        if (task.IsCompleted)
        {
            _openGroup.Items.Remove(task);
            if (!_completedGroup.Items.Contains(task))
                _completedGroup.Items.Add(task);
        }
        else
        {
            _completedGroup.Items.Remove(task);
            if (!_openGroup.Items.Contains(task))
                _openGroup.Items.Insert(0, task);
        }
    }

    public IList<PlannedGroup> PlannedGroups => _cachedPlannedGroups ??= BuildPlannedGroups();

    private List<PlannedGroup> BuildPlannedGroups()
    {
        // Design decision: tasks without a due date are intentionally excluded from
        // Planned. Planned is a time-based planning view — undated tasks belong in
        // All Tasks. The empty-state subtext already guides users to set a due date.
        var groups = new List<PlannedGroup>();
        var today = DateTimeOffset.Now.Date;
        var tomorrow = today.AddDays(1);
        var weekEnd = today.AddDays(7 - (int)today.DayOfWeek);

        var tasksWithDue = Tasks.Where(t => t.DueDate != null && !t.IsCompleted).GroupBy(t =>
        {
            // The day as written, never through ToLocalTime — see TipEngine.
            var dueDate = t.DueDate!.Value.Date;
            if (dueDate < today)     return Strings.PlannedGroup_Overdue;
            if (dueDate == today)    return Strings.PlannedGroup_Today;
            if (dueDate == tomorrow) return Strings.PlannedGroup_Tomorrow;
            if (dueDate <= weekEnd)  return Strings.PlannedGroup_ThisWeek;
            return Strings.PlannedGroup_Later;
        }).OrderBy(g => Array.IndexOf(
            new[] {
                Strings.PlannedGroup_Overdue,
                Strings.PlannedGroup_Today,
                Strings.PlannedGroup_Tomorrow,
                Strings.PlannedGroup_ThisWeek,
                Strings.PlannedGroup_Later
            }, g.Key));

        foreach (var group in tasksWithDue)
        {
            groups.Add(new PlannedGroup
            {
                Name = group.Key,
                Items = new ObservableCollection<TodoItem>(group.OrderBy(t => t.DueDate))
            });
        }

        return groups;
    }

    private void NotifyPlannedGroupsChanged()
    {
        _cachedPlannedGroups = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlannedGroups)));
    }

    public IList<CompletedTaskGroup> FlatGroupedTasks => _flatGroupedTasks;

    public bool IsCompletedGroupExpanded(string navItem) =>
        _completedGroupExpandedState.TryGetValue(navItem, out var expanded) && expanded;

    public void SetCompletedGroupExpanded(string navItem, bool expanded) =>
        _completedGroupExpandedState[navItem] = expanded;
}

public sealed record PlannedGroup
{
    public required string Name { get; init; }
    public required ObservableCollection<TodoItem> Items { get; init; }
}
