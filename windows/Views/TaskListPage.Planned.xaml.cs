using System.Globalization;
using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Views;

public sealed partial class TaskListPage
{
    private ScrollViewer? _plannedDayScrollViewer;
    private bool _updatingPlannedControls;
    private bool _syncingPlannedViewSelector;
    private SelectorBarItem? _plannedViewSelectorPointerOverItem;
    private List<PlannedAgendaGroup> _agendaGroups = [];
    private List<PlannedAgendaEntry> _agendaEntries = [];

    public Visibility EmptyStateVisibility(bool showEmptyState, bool isPlannedPage,
        bool isPlannedCalendar, bool hasAgendaEvents) =>
        showEmptyState && !(isPlannedPage && !isPlannedCalendar && hasAgendaEvents)
            ? Visibility.Visible : Visibility.Collapsed;

    private ScrollViewer? ActiveTaskScrollViewer => _vm?.ActiveNavItem == "planned"
        ? _vm.IsPlannedCalendar ? _plannedDayScrollViewer : _groupedTaskScrollViewer
        : FlatListView;

    private void SyncPlannedViewControls(bool showDate = false)
    {
        if (_vm == null) return;
        CalendarViewModel.SelectedDate = DateOnly.FromDateTime(_vm.SelectedPlannedDate.Date);
        if (_vm.IsPlannedPage) _ = CalendarViewModel.StartAsync();
        else CalendarViewModel.Stop();
        _syncingPlannedViewSelector = true;
        PlannedViewSelector.SelectedItem = _vm.IsPlannedCalendar ? PlannedCalendarItem : PlannedAgendaItem;
        _syncingPlannedViewSelector = false;
        UpdatePlannedViewSelectorBackgrounds();
        if (_vm.IsPlannedCalendarVisible)
        {
            SyncPlannedCalendarSelection(showDate);
            RefreshPlannedCalendarMarkers();
        }
        _vm.ResetTaskComposerScroll(ActiveTaskScrollViewer?.VerticalOffset ?? 0);
    }

    private void QueuePlannedViewControlSync()
    {
        var vm = _vm;
        if (vm == null) return;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(_vm, vm) || !vm.IsPlannedPage) return;

            SyncPlannedViewControls();
            _syncingPlannedViewSelector = true;
            try
            {
                PlannedViewSelector.SelectedItem = null;
                PlannedViewSelector.SelectedItem = vm.IsPlannedCalendar ? PlannedCalendarItem : PlannedAgendaItem;
            }
            finally { _syncingPlannedViewSelector = false; }
            UpdatePlannedViewSelectorBackgrounds();
        });
    }

    private void PlannedViewSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        UpdatePlannedViewSelectorBackgrounds();
        if (_syncingPlannedViewSelector || sender.SelectedItem is not SelectorBarItem item) return;
        ApplyPlannedViewSelection(item);
    }

    private void PlannedViewSelectorItem_Tapped(object sender, TappedRoutedEventArgs args)
    {
        if (sender is SelectorBarItem item)
            ApplyPlannedViewSelection(item);
    }

    private void ApplyPlannedViewSelection(SelectorBarItem item)
    {
        if (_vm == null) return;

        if (!ReferenceEquals(PlannedViewSelector.SelectedItem, item))
        {
            _syncingPlannedViewSelector = true;
            try { PlannedViewSelector.SelectedItem = item; }
            finally { _syncingPlannedViewSelector = false; }
        }

        _vm.SelectPlannedViewCommand.Execute(ReferenceEquals(item, PlannedCalendarItem) ? "calendar" : "agenda");
        PlannedCalendarLayout.Visibility = _vm.IsPlannedCalendarVisible ? Visibility.Visible : Visibility.Collapsed;
        RefreshPlannedGroups();
        UpdatePlannedViewSelectorBackgrounds();
    }

    private void PlannedViewSelectorItem_PointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not SelectorBarItem item) return;
        _plannedViewSelectorPointerOverItem = item;
        UpdatePlannedViewSelectorBackgrounds();
    }

    private void PlannedViewSelectorItem_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(_plannedViewSelectorPointerOverItem, sender))
            _plannedViewSelectorPointerOverItem = null;
        UpdatePlannedViewSelectorBackgrounds();
    }

    private void UpdatePlannedViewSelectorBackgrounds()
    {
        SetPlannedViewSelectorBackground(PlannedAgendaItem);
        SetPlannedViewSelectorBackground(PlannedCalendarItem);
    }

    private void SetPlannedViewSelectorBackground(SelectorBarItem item)
    {
        var resourceKey = !ReferenceEquals(PlannedViewSelector.SelectedItem, item)
            ? "SelectorBarItemBackground"
            : ReferenceEquals(_plannedViewSelectorPointerOverItem, item)
                ? "SelectorBarItemBackgroundSelectedPointerOver"
                : "SelectorBarItemBackgroundSelected";
        item.Background = (Brush)PlannedViewSelector.Resources[resourceKey];
    }

    private void PlannedCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_updatingPlannedControls || _vm?.IsPlannedCalendarVisible != true) return;
        if (args.AddedDates.Count > 0)
        {
            _vm.SelectedPlannedDate = args.AddedDates[0].ToLocalTime();
            PlannedDateFlyout.Hide();
        }
        else DispatcherQueue.TryEnqueue(() => SyncPlannedCalendarSelection(false));
    }

    private void PlannedDateFlyout_Opened(object sender, object args)
    {
        SyncPlannedCalendarSelection(true);
        RefreshPlannedCalendarMarkers();
    }

    private void SyncPlannedCalendarSelection(bool showDate)
    {
        if (_vm == null) return;
        _updatingPlannedControls = true;
        var date = ToNativeCalendarDate(_vm.SelectedPlannedDate);
        if (PlannedCalendar.SelectedDates.Count != 1 || PlannedCalendar.SelectedDates[0].ToLocalTime().Date != date.Date)
        {
            PlannedCalendar.SelectedDates.Clear();
            PlannedCalendar.SelectedDates.Add(date);
        }
        if (showDate) PlannedCalendar.SetDisplayDate(date);
        _updatingPlannedControls = false;
    }

    // CalendarView compares local instants; persisted due dates represent calendar days.
    private static DateTimeOffset ToNativeCalendarDate(DateTimeOffset date) =>
        new(DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Local));

    private void PlannedCalendar_Loaded(object sender, RoutedEventArgs args)
    {
        SyncPlannedCalendarSelection(false);
        RefreshPlannedCalendarMarkers();
    }

    private void PlannedCalendar_DayItemChanging(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
        => UpdatePlannedDayMarker(args.Item);

    private void PlannedCalendar_ActualThemeChanged(FrameworkElement sender, object args)
        => RefreshPlannedCalendarMarkers();

    private void RefreshPlannedCalendarMarkers()
    {
        if (_vm?.IsPlannedCalendarVisible != true) return;
        RefreshPlannedCalendarMarkers(PlannedCalendar);
    }

    private void RefreshPlannedCalendarMarkers(DependencyObject element)
    {
        if (element is CalendarViewDayItem day) UpdatePlannedDayMarker(day);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            RefreshPlannedCalendarMarkers(VisualTreeHelper.GetChild(element, i));
    }

    private void UpdatePlannedDayMarker(CalendarViewDayItem day)
    {
        if (_vm == null) return;
        var date = day.Date.ToLocalTime();
        int count = _vm.GetPlannedTaskCount(date);
        int events = CalendarViewModel.GetEventCount(date);
        var taskColour = ThemeResourceHelper.GetBrush("AccentTextFillColorPrimaryBrush") as SolidColorBrush;
        var eventColour = ThemeResourceHelper.GetBrush("TextFillColorSecondaryBrush") as SolidColorBrush;
        var colours = new List<Windows.UI.Color>();
        if (count > 0 && taskColour != null) colours.Add(taskColour.Color);
        if (events > 0 && eventColour != null) colours.Add(eventColour.Color);
        day.SetDensityColors(colours.Count > 0 ? colours : null);
        var description = Strings.PlannedCalendar_DateTasks(date.ToString("D", CultureInfo.CurrentCulture), count);
        if (events > 0) description += " " + string.Format(Strings.Get("Calendar_DateEvents"), events);
        AutomationProperties.SetHelpText(day, description);
        ToolTipService.SetToolTip(day, description);
    }

    private void PlannedToday_Click(object sender, RoutedEventArgs args)
    {
        _vm?.SelectPlannedToday();
        SyncPlannedCalendarSelection(true);
        _plannedDayScrollViewer?.ChangeView(null, 0, null, true);
    }

    private void PlannedOverdue_Click(object sender, RoutedEventArgs args)
    {
        _vm?.ShowPlannedOverdue();
        DispatcherQueue.TryEnqueue(() => _groupedTaskScrollViewer?.ChangeView(null, 0, null, true));
    }

    private void PlannedCalendarLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool wide = args.NewSize.Width >= 680;
        var desiredHost = wide ? PlannedWideCalendarHost : PlannedCompactCalendarHost;
        if (!ReferenceEquals(desiredHost.Content, PlannedCalendar))
        {
            PlannedDateFlyout.Hide();
            PlannedWideCalendarHost.Content = null;
            PlannedCompactCalendarHost.Content = null;
            desiredHost.Content = PlannedCalendar;
        }
        PlannedCalendarSidebar.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        PlannedDateButton.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        PlannedWideDateLabel.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        PlannedCalendarLayout.ColumnSpacing = wide ? 16 : 0;
        Grid.SetColumn(PlannedDayHeader, wide ? 1 : 0);
        Grid.SetColumnSpan(PlannedDayHeader, wide ? 1 : 2);
        Grid.SetColumn(PlannedDayList, wide ? 1 : 0);
        Grid.SetColumnSpan(PlannedDayList, wide ? 1 : 2);
    }

    private bool IsPlannedCalendarControlFocused()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused != null)
        {
            if (ReferenceEquals(focused, PlannedCalendar) || ReferenceEquals(focused, PlannedViewSelector)
                || ReferenceEquals(focused, PlannedDateButton) || ReferenceEquals(focused, PlannedMeetingsHeader)
                || focused is FrameworkElement { DataContext: Hatch.ViewModels.CalendarEventRow }) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    private void BuildPlannedAgendaGroups()
    {
        if (_vm == null) return;

        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var tasks = _vm.PlannedGroups.SelectMany(group => group.Items).ToArray();
        var overdue = tasks.Where(task => DateOnly.FromDateTime(task.DueDate!.Value.Date) < today).ToArray();
        var tasksByDay = tasks.Where(task => DateOnly.FromDateTime(task.DueDate!.Value.Date) >= today)
            .GroupBy(task => DateOnly.FromDateTime(task.DueDate!.Value.Date))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var meetingsByDay = CalendarViewModel.AgendaEvents
            .GroupBy(row => CalendarAgenda.AgendaDate(row.Event, now, TimeZoneInfo.Local))
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.SortStart).ToArray());

        var groups = new List<PlannedAgendaGroup>();
        AddGroup(Strings.PlannedGroup_Overdue, [], overdue);
        foreach (var date in tasksByDay.Keys.Concat(meetingsByDay.Keys).Distinct().Order())
        {
            tasksByDay.TryGetValue(date, out var dayTasks);
            meetingsByDay.TryGetValue(date, out var dayMeetings);
            AddGroup(AgendaDateLabel(date, today), dayMeetings ?? [], dayTasks ?? []);
        }
        _agendaGroups = groups;
        return;

        void AddGroup(string name, CalendarEventRow[] meetings, TodoItem[] dueTasks)
        {
            if (meetings.Length == 0 && dueTasks.Length == 0) return;
            var entries = new List<PlannedAgendaEntry>();
            bool showSections = meetings.Length > 0 && dueTasks.Length > 0;
            if (meetings.Length > 0)
            {
                if (showSections) entries.Add(PlannedAgendaEntry.ForSection(Strings.Get("PlannedAgenda_Meetings")));
                foreach (var meeting in meetings) entries.Add(PlannedAgendaEntry.ForMeeting(meeting));
            }
            if (dueTasks.Length > 0)
            {
                if (showSections) entries.Add(PlannedAgendaEntry.ForSection(Strings.Get("PlannedAgenda_TasksDue")));
                foreach (var task in dueTasks) entries.Add(PlannedAgendaEntry.ForTask(task));
            }
            groups.Add(new PlannedAgendaGroup(name, entries));
        }
    }

    private static string AgendaDateLabel(DateOnly date, DateOnly today)
    {
        if (date == today) return Strings.PlannedGroup_Today;
        if (date == today.AddDays(1)) return Strings.PlannedGroup_Tomorrow;
        var format = date.Year == today.Year ? "dddd, MMMM d" : "dddd, MMMM d, yyyy";
        return date.ToDateTime(TimeOnly.MinValue).ToString(format, CultureInfo.CurrentCulture);
    }

}
