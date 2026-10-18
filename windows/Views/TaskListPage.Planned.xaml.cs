using System.Globalization;
using Hatch.Converters;
using Hatch.Helpers;
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

    private ScrollViewer? ActiveTaskScrollViewer => _vm?.ActiveNavItem == "planned"
        ? _vm.IsPlannedCalendar ? _plannedDayScrollViewer : _groupedTaskScrollViewer
        : FlatListView;

    private void SyncPlannedViewControls(bool showDate = false)
    {
        if (_vm == null) return;
        PlannedAgendaItem.IsChecked = !_vm.IsPlannedCalendar;
        PlannedCalendarItem.IsChecked = _vm.IsPlannedCalendar;
        PlannedViewSelector.Content = (_vm.IsPlannedCalendar ? PlannedCalendarItem : PlannedAgendaItem).Text;
        GroupedListView.Visibility = _vm.IsPlannedPage && !_vm.IsPlannedCalendar && !_vm.IsPlannedEmpty
            ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.IsPlannedCalendarVisible)
        {
            SyncPlannedCalendarSelection(showDate);
            RefreshPlannedCalendarMarkers();
        }
        _vm.ResetTaskComposerScroll(ActiveTaskScrollViewer?.VerticalOffset ?? 0);
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
        var colour = ThemeResourceHelper.GetBrush("AccentTextFillColorPrimaryBrush") as SolidColorBrush;
        // Color arrays require boxed IReferenceArray support unavailable in Native AOT.
        day.SetDensityColors(count > 0 && colour != null
            ? new List<Windows.UI.Color> { colour.Color }
            : null);
        var description = Strings.PlannedCalendar_DateTasks(date.ToString("D", CultureInfo.CurrentCulture), count);
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
        Grid.SetColumn(PlannedDayEmptyState, wide ? 1 : 0);
        Grid.SetColumnSpan(PlannedDayEmptyState, wide ? 1 : 2);
    }

    private bool IsPlannedCalendarControlFocused()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused != null)
        {
            if (ReferenceEquals(focused, PlannedCalendar) || ReferenceEquals(focused, PlannedViewSelector)
                || ReferenceEquals(focused, PlannedDateButton)) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }
}
