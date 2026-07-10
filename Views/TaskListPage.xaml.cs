using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Dispatching;
using Windows.UI.Core;
using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.ViewModels;
using Windows.System;

namespace Hatch.Views;

public sealed partial class TaskListPage : Page
{
    private MainViewModel? _vm;
    internal MainViewModel ViewModel => (MainViewModel)DataContext;

    private UIElement? _savedFocusElement;
    private TodoItem? _paneTask;
    private bool _updatingPane;
    private Storyboard? _paneStoryboard;
    private bool _suppressSelectionChanged;
    private TodoItem? _preTapSelectedTask;
    private bool _suppressPaneFocusOnOpen;
    private List<ListView>? _cachedTaskListViews;

    // Cached date flyout controls — built once, reused on every chip tap.
    private Flyout? _dateFlyout;
    private CalendarDatePicker? _flyoutCalendar;
    private Button? _flyoutClearBtn;
    private Button[]? _flyoutPresetBtns;
    private TodoItem? _flyoutTask;
    private bool _updatingFlyout;

    private enum PaneLayoutMode { SideBySide, Overlay }
    private PaneLayoutMode _paneMode = PaneLayoutMode.SideBySide;

    private const double BreakpointWidth = 700;

    public TaskListPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        ActualThemeChanged += OnActualThemeChanged;
        SizeChanged += OnPageSizeChanged;
        Loaded += (_, _) => ApplyPaneLayout(ActualWidth);

        // Fires for every pointer press on the page, even those handled by child controls,
        // so we can detect clicks outside the details pane.
        this.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(OnPagePointerPressed),
            handledEventsToo: true);

        // IsTabStop = true lets the page accept programmatic focus, which is used to
        // keep keyboard focus on the page during ↑/↓ task navigation so the details
        // pane updates live without stealing focus into PaneTitleBox.
        IsTabStop = true;
        this.KeyDown += OnPageKeyDown;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_vm == null) return;
        foreach (var item in _vm.ActiveTasks)
            item.RefreshDueDateBinding();
        if (_vm.ActiveNavItem == "planned")
            RefreshPlannedGroups();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyPaneLayout(e.NewSize.Width);

    private void ApplyPaneLayout(double pageWidth)
    {
        var newMode = pageWidth < BreakpointWidth ? PaneLayoutMode.Overlay : PaneLayoutMode.SideBySide;

        double paneWidth = newMode == PaneLayoutMode.SideBySide
            ? Math.Clamp(pageWidth * 0.30, 280, 360)   // scales 280→360 between ~934px and ~1200px
            : Math.Clamp(pageWidth - 48, 280, 360);     // leaves 48px of list visible behind scrim

        DetailsPaneRoot.Width = paneWidth;

        if (newMode == PaneLayoutMode.SideBySide)
        {
            Grid.SetColumn(DetailsPaneRoot, 1);
            DetailsPaneRoot.HorizontalAlignment = HorizontalAlignment.Stretch;
            PaneScrim.Visibility = Visibility.Collapsed;
        }
        else
        {
            Grid.SetColumn(DetailsPaneRoot, 0);
            DetailsPaneRoot.HorizontalAlignment = HorizontalAlignment.Right;
            PaneScrim.Visibility = DetailsPaneRoot.Visibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        _paneMode = newMode;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MainViewModel vm) return;

        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.FlatGroupMoveStarting -= OnFlatGroupMoveStarting;
            _vm.FlatGroupMoveCompleted -= OnFlatGroupMoveCompleted;
        }

        _vm = vm;
        DataContext = vm;
        UpdateSearchVisibility();
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.FlatGroupMoveStarting += OnFlatGroupMoveStarting;
        vm.FlatGroupMoveCompleted += OnFlatGroupMoveCompleted;

        // Restore pane if a task was selected before navigation
        if (vm.SelectedTask != null)
            OpenPane(vm.SelectedTask);
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.FlatGroupMoveStarting -= OnFlatGroupMoveStarting;
            _vm.FlatGroupMoveCompleted -= OnFlatGroupMoveCompleted;
            // Close pane silently when navigating away — don't animate
            _vm.SelectedTask = null;
            DetailsPaneRoot.Visibility = Visibility.Collapsed;
        }
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_vm == null) return;

        switch (args.PropertyName)
        {
            case nameof(MainViewModel.ActiveNavItem):
                // Close pane when switching lists — selected task may leave the view.
                // Always sync ListViews explicitly: if SelectedTask was already null the
                // setter is a no-op and SyncListViewSelection would never be called.
                _vm.SelectedTask = null;
                SyncListViewSelection(null);
                // While a search is active, the search results view stays in place
                // regardless of nav selection — it spans all lists.
                if (!_vm.IsSearchActive)
                    UpdateView(_vm.ActiveNavItem);
                break;

            case nameof(MainViewModel.IsSearchActive):
                UpdateSearchVisibility();
                break;

            case nameof(MainViewModel.IsSearchEmpty):
                SearchEmptyState.Visibility = _vm.IsSearchEmpty ? Visibility.Visible : Visibility.Collapsed;
                break;

            case nameof(MainViewModel.PlannedGroups) or nameof(MainViewModel.IsPlannedEmpty)
                when _vm.ActiveNavItem == "planned":
                GroupedListView.Visibility = _vm.IsPlannedEmpty ? Visibility.Collapsed : Visibility.Visible;
                RefreshPlannedGroups();
                break;

            case nameof(MainViewModel.SelectedTask):
                SyncListViewSelection(_vm.SelectedTask);
                if (_vm.SelectedTask != null)
                    OpenPane(_vm.SelectedTask);
                else
                    ClosePane();
                break;
        }
    }

    // ── Pane open / close ────────────────────────────────────────────────────

    private void OpenPane(TodoItem task)
    {
        bool wasVisible = DetailsPaneRoot.Visibility == Visibility.Visible;

        _paneTask = task;
        PopulatePaneFields(task);

        if (!wasVisible)
        {
            DetailsPaneRoot.Visibility = Visibility.Visible;
            if (_paneMode == PaneLayoutMode.Overlay)
                PaneScrim.Visibility = Visibility.Visible;
            AnimatePane(from: DetailsPaneRoot.Width, to: 0, durationMs: 200, easeOut: true);
        }
        else
        {
            // Cancel any in-flight close animation (e.g. triggered by OnPagePointerPressed
            // before TaskCard_Tapped fires) and keep the pane open at full position.
            _paneStoryboard?.Stop();
            DetailsPaneTranslate.X = 0;
        }

        if (!_suppressPaneFocusOnOpen)
            PaneTitleBox.Focus(FocusState.Programmatic);
    }

    private void ClosePane()
    {
        if (DetailsPaneRoot.Visibility != Visibility.Visible) return;

        AnimatePane(from: 0, to: DetailsPaneRoot.Width, durationMs: 200, easeOut: false, onComplete: () =>
        {
            DetailsPaneRoot.Visibility = Visibility.Collapsed;
            PaneScrim.Visibility = Visibility.Collapsed;
            _paneTask = null;
        });
    }

    private void PaneScrim_Tapped(object sender, TappedRoutedEventArgs e)
        => ViewModel.SelectedTask = null;

    private void AnimatePane(double from, double to, int durationMs, bool easeOut, Action? onComplete = null)
    {
        _paneStoryboard?.Stop();
        DetailsPaneTranslate.X = from;

        _paneStoryboard = new Storyboard();
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = easeOut ? EasingMode.EaseOut : EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, DetailsPaneTranslate);
        Storyboard.SetTargetProperty(anim, "X");
        _paneStoryboard.Children.Add(anim);
        if (onComplete != null)
            _paneStoryboard.Completed += (_, _) => onComplete();
        _paneStoryboard.Begin();
    }

    private void PopulatePaneFields(TodoItem task)
    {
        _updatingPane = true;
        PaneTitleBox.Text = task.Title;
        PaneNotesBox.Text = task.Notes ?? string.Empty;
        PaneMyDayToggle.IsOn = task.IsInMyDay;
        PaneDueDatePicker.Date = task.DueDate.HasValue
            ? (DateTimeOffset?)new DateTimeOffset(task.DueDate.Value.ToLocalTime().Date, TimeSpan.Zero)
            : null;
        PaneCreatedAtText.Text = task.CreatedAt.ToLocalTime().ToString("ddd, MMM d, yyyy");
        PaneTagInput.Text = string.Empty;
        PaneTagChips.ItemsSource = task.Tags;
        _updatingPane = false;
    }

    private void PaneTagChipRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && _paneTask != null)
        {
            _paneTask.Tags = _paneTask.Tags.Where(t => t != tag).ToList();
            PaneTagChips.ItemsSource = _paneTask.Tags;
        }
    }


    private void PaneTagInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _paneTask == null || _updatingPane) return;
        var input = PaneTagInput.Text.Trim();
        if (string.IsNullOrEmpty(input)) return;
        if (!_paneTask.Tags.Contains(input, StringComparer.OrdinalIgnoreCase))
        {
            _paneTask.Tags = [.._paneTask.Tags, input];
            PaneTagChips.ItemsSource = _paneTask.Tags;
        }
        PaneTagInput.Text = string.Empty;
        e.Handled = true;
    }

    // ── Pane field handlers ──────────────────────────────────────────────────

    private void PaneCloseButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.SelectedTask = null;

    private void PaneTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingPane || _paneTask == null) return;
        _paneTask.Title = PaneTitleBox.Text;
    }

    private void PaneNotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingPane || _paneTask == null) return;
        _paneTask.Notes = PaneNotesBox.Text.Length > 0 ? PaneNotesBox.Text : null;
    }

    private void PaneMyDayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingPane || _paneTask == null) return;
        _paneTask.IsInMyDay = PaneMyDayToggle.IsOn;
        _paneTask.MyDayDate = PaneMyDayToggle.IsOn
            ? DateOnly.FromDateTime(DateTime.Today)
            : null;
    }

    private void SuggestionCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Don't open pane when the tap originated from the Add button.
        var source = e.OriginalSource as DependencyObject;
        while (source != null && !ReferenceEquals(source, sender))
        {
            if (source is ButtonBase) return;
            source = VisualTreeHelper.GetParent(source);
        }
        if (sender is Grid { Tag: TodoItem task })
            ViewModel.SelectedTask = task;
    }

    private void SuggestionAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TodoItem task })
            ViewModel.AddSuggestionToMyDayCommand.Execute(task);
    }

    private void PaneDueDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_updatingPane || _paneTask == null) return;
        _paneTask.DueDate = args.NewDate.HasValue
            ? (DateTimeOffset?)new DateTimeOffset(args.NewDate.Value.ToLocalTime().Date, TimeSpan.Zero)
            : null;
    }

    // ── Keyboard / pointer close triggers ───────────────────────────────────

    private void DetailsPaneRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.SelectedTask = null;
            e.Handled = true;
            // Return focus to the page so ↑/↓ can continue task navigation immediately.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                () => this.Focus(FocusState.Programmatic));
        }
    }

    private void OnPagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        if (DetailsPaneRoot.Visibility != Visibility.Visible) return;
        // In side-by-side mode task card taps switch pane content via TaskCard_Tapped —
        // don't close here. Overlay mode uses the scrim (PaneScrim_Tapped) instead.
        if (_paneMode == PaneLayoutMode.SideBySide) return;

        var pt = e.GetCurrentPoint(DetailsPaneRoot).Position;
        bool insidePane = pt.X >= 0 && pt.Y >= 0
            && pt.X <= DetailsPaneRoot.ActualWidth
            && pt.Y <= DetailsPaneRoot.ActualHeight;

        if (!insidePane)
            ViewModel.SelectedTask = null;
    }

    // ── Task card interaction ────────────────────────────────────────────────

    private void TaskCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Capture which task was selected before this press so TaskCard_Tapped
        // can detect a toggle-close (tapping the already-selected task).
        _preTapSelectedTask = ViewModel.SelectedTask;
    }

    private void TaskCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Don't intercept taps that originate from interactive child controls.
        var source = e.OriginalSource as DependencyObject;
        while (source != null && !ReferenceEquals(source, sender))
        {
            if (source is ButtonBase or CalendarDatePicker) return;
            source = VisualTreeHelper.GetParent(source);
        }

        var task = (TodoItem)((FrameworkElement)sender).Tag;
        if (_preTapSelectedTask == task)
        {
            // Tapping the already-selected task: deselect and close pane.
            // SelectionChanged won't fire here because ListView SelectedItem didn't change.
            ViewModel.SelectedTask = null;
            e.Handled = true;
        }
        // For a newly selected task, ListView.SelectionChanged already fired and
        // updated ViewModel.SelectedTask — nothing to do here.
    }

    private void TaskListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is TodoItem task)
        {
            // Deselect all other task ListViews so only one row highlights at a time.
            _suppressSelectionChanged = true;
            foreach (var lv in FindTaskListViews())
            {
                if (!ReferenceEquals(lv, sender))
                    lv.SelectedItem = null;
            }
            _suppressSelectionChanged = false;
            ViewModel.SelectedTask = task;
        }
    }

    private void SyncListViewSelection(TodoItem? task)
    {
        _suppressSelectionChanged = true;
        foreach (var lv in FindTaskListViews())
            lv.SelectedItem = task != null && lv.Items.Contains(task) ? task : null;
        _suppressSelectionChanged = false;
    }

    private IEnumerable<ListView> FindTaskListViews()
    {
        if (_cachedTaskListViews != null) return _cachedTaskListViews;
        var template = (DataTemplate)Resources["TaskItemTemplate"];
        _cachedTaskListViews = FindDescendants<ListView>(this)
            .Where(lv => lv.ItemTemplate == template)
            .ToList();
        return _cachedTaskListViews;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var desc in FindDescendants<T>(child))
                yield return desc;
        }
    }

    // ── Existing handlers ────────────────────────────────────────────────────

    private void UpdateView(string navItem)
    {
        _cachedTaskListViews = null;
        HeaderText.Text = navItem switch
        {
            "myday"     => Strings.Header_MyDay,
            "important" => Strings.Header_Important,
            "planned"   => Strings.Header_Planned,
            _           => Guid.TryParse(navItem, out var listId)
                            ? (_vm?.CustomLists.FirstOrDefault(l => l.Id == listId)?.Name ?? Strings.Header_AllTasks)
                            : Strings.Header_AllTasks
        };

        var isPlanned = navItem == "planned";
        FlatListView.Visibility    = isPlanned ? Visibility.Collapsed : Visibility.Visible;
        GroupedListView.Visibility = isPlanned && !(_vm?.IsPlannedEmpty ?? true) ? Visibility.Visible : Visibility.Collapsed;

        if (isPlanned)
            RefreshPlannedGroups();
    }

    private void UpdateSearchVisibility()
    {
        if (_vm == null) return;
        _cachedTaskListViews = null;

        if (_vm.IsSearchActive)
        {
            FlatListView.Visibility = Visibility.Collapsed;
            GroupedListView.Visibility = Visibility.Collapsed;
            SearchListView.Visibility = Visibility.Visible;
            SearchEmptyState.Visibility = _vm.IsSearchEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            SearchListView.Visibility = Visibility.Collapsed;
            SearchEmptyState.Visibility = Visibility.Collapsed;
            UpdateView(_vm.ActiveNavItem);
        }
    }

    public void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        ViewModel.SearchQuery = string.Empty;
        e.Handled = true;
        this.Focus(FocusState.Programmatic);
    }

    private void RefreshPlannedGroups()
    {
        if (_vm == null) return;
        var cvs = (CollectionViewSource)Resources["PlannedGroupsSource"];
        _suppressSelectionChanged = true;
        cvs.Source = _vm.PlannedGroups;
        // ICollectionView.CurrentItem auto-positions to the first item and WinUI fires
        // SelectionChanged on a deferred frame — keep suppression active until after
        // that frame, then clear any phantom selection.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            if (ViewModel.SelectedTask == null)
                GroupedListView.SelectedItem = null;
            _suppressSelectionChanged = false;
        });
    }

    private void OnFlatGroupMoveStarting()
    {
        _savedFocusElement = FocusManager.GetFocusedElement(XamlRoot) as UIElement;
    }

    private void OnFlatGroupMoveCompleted()
    {
        if (_savedFocusElement == null) return;
        _savedFocusElement.Focus(FocusState.Programmatic);
        _savedFocusElement = null;
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                       & CoreVirtualKeyStates.Down) != 0;

        switch (e.Key)
        {
            case VirtualKey.Delete when !isCtrl && !IsTextInputFocused():
                if (ViewModel.SelectedTask is { } delTask)
                {
                    ViewModel.SelectedTask = null;
                    ViewModel.DeleteTask(delTask);
                    e.Handled = true;
                }
                break;

            case VirtualKey.D when isCtrl && !IsTextInputFocused():
                if (ViewModel.SelectedTask != null
                    && DetailsPaneRoot.Visibility == Visibility.Visible)
                {
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
                        () => PaneDueDatePicker.IsCalendarOpen = true);
                    e.Handled = true;
                }
                break;

            case VirtualKey.M when isCtrl && !IsTextInputFocused():
                if (ViewModel.SelectedTask is { } mTask)
                {
                    bool myDayOn = !mTask.IsInMyDay;
                    mTask.IsInMyDay = myDayOn;
                    mTask.MyDayDate = myDayOn ? DateOnly.FromDateTime(DateTime.Today) : null;
                    if (_paneTask == mTask)
                    {
                        _updatingPane = true;
                        PaneMyDayToggle.IsOn = mTask.IsInMyDay;
                        _updatingPane = false;
                    }
                    e.Handled = true;
                }
                break;

            case VirtualKey.Up when !isCtrl && !IsTextInputFocused():
                _suppressPaneFocusOnOpen = true;
                MoveSelection(-1);
                _suppressPaneFocusOnOpen = false;
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;

            case VirtualKey.Down when !isCtrl && !IsTextInputFocused():
                _suppressPaneFocusOnOpen = true;
                MoveSelection(1);
                _suppressPaneFocusOnOpen = false;
                this.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;
        }
    }

    private void PaneNotesBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                       & CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.Enter && isCtrl)
        {
            PaneTitleBox.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private bool IsTextInputFocused()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        return focused is TextBox or PasswordBox or RichEditBox;
    }

    private void MoveSelection(int delta)
    {
        var current = ViewModel.SelectedTask;
        if (current == null) return;
        var tasks = GetVisibleTasksInOrder();
        int idx = tasks.IndexOf(current);
        if (idx < 0) return;
        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= tasks.Count) return;
        ViewModel.SelectedTask = tasks[newIdx];
    }

    private List<TodoItem> GetVisibleTasksInOrder()
    {
        if (_vm?.IsSearchActive == true)
            return ViewModel.SearchResults.ToList();
        if (_vm?.ActiveNavItem == "planned")
            return ViewModel.PlannedGroups.SelectMany(g => g.Items).ToList();
        var groups = ViewModel.FlatGroupedTasks;
        var result = new List<TodoItem>(groups[0].Items);
        if (groups.Count > 1 && groups[1].IsExpanded)
            result.AddRange(groups[1].Items);
        return result;
    }

    private void TagChipInRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            ViewModel.ActiveTagFilter = tag;
    }

    private void TagOverflowButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((FrameworkElement)sender).Tag;
        ViewModel.SelectedTask = task;
    }

    private void OverflowButton_Click(object sender, RoutedEventArgs e) { }

    private void FocusMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((MenuFlyoutItem)sender).Tag;
        App.MascotWindowInstance?.ShowFocusMode(task);
    }

    private void UndoInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        ViewModel.DismissUndoBar();
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddTaskCommand.CanExecute(null))
            ViewModel.AddTaskCommand.Execute(null);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((FrameworkElement)sender).Tag;
        if (ViewModel.SelectedTask == task)
            ViewModel.SelectedTask = null;
        ViewModel.DeleteTask(task);
    }

    private void StarButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((Button)sender).Tag;
        if (task is null) return;
        task.IsStarred = !task.IsStarred;
    }

    private void ChipDateButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((Button)sender).Tag;
        if (task is null) return;

        EnsureDateFlyoutBuilt();

        _flyoutTask = task;
        UpdateFlyoutState(task.DueDate);
        _dateFlyout!.ShowAt((FrameworkElement)sender);
    }

    private void EnsureDateFlyoutBuilt()
    {
        if (_dateFlyout != null) return;

        var presetDefs = new (string Label, Func<DateTime> GetDate)[]
        {
            (Strings.DatePreset_Today,    () => DueDatePresets.GetToday(DateTime.Today)),
            (Strings.DatePreset_Tomorrow, () => DueDatePresets.GetTomorrow(DateTime.Today)),
            (Strings.DatePreset_Weekend,  () => DueDatePresets.GetThisWeekend(DateTime.Today)),
            (Strings.DatePreset_NextWeek, () => DueDatePresets.GetNextWeek(DateTime.Today)),
        };

        _flyoutCalendar = new CalendarDatePicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText     = Strings.EditTask_DatePlaceholder,
            Margin              = new Thickness(0, 8, 0, 0)
        };
        _flyoutCalendar.DateChanged += (_, args) =>
        {
            if (!_updatingFlyout && args.NewDate.HasValue)
                FlyoutCommitAndClose(new DateTimeOffset(args.NewDate.Value.ToLocalTime().Date, TimeSpan.Zero));
        };

        _flyoutClearBtn = new Button
        {
            Content                    = Strings.EditTask_ClearDate,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin                     = new Thickness(0, 8, 0, 0),
            Foreground                 = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush")
        };
        _flyoutClearBtn.Click += (_, _) => FlyoutCommitAndClose(null);

        var presetGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _flyoutPresetBtns = new Button[presetDefs.Length];
        for (int i = 0; i < presetDefs.Length; i++)
        {
            var (label, getDate) = presetDefs[i];
            var btn = new Button
            {
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding                    = new Thickness(10, 8, 10, 8),
                CornerRadius               = new CornerRadius(6)
            };
            btn.Content = new TextBlock { Text = label, Style = ThemeResourceHelper.GetStyle("CaptionTextBlockStyle") };
            Grid.SetColumn(btn, i % 2);
            Grid.SetRow(btn, i / 2);
            presetGrid.Children.Add(btn);

            var capturedGetDate = getDate;
            btn.Click += (_, _) =>
            {
                var date = capturedGetDate();
                if (_flyoutTask?.DueDate?.ToLocalTime().Date == date)
                    FlyoutCommitAndClose(null);
                else
                    FlyoutCommitAndClose(new DateTimeOffset(date));
            };

            _flyoutPresetBtns[i] = btn;
        }

        var panel = new StackPanel { Spacing = 0, MinWidth = 220 };
        panel.Children.Add(presetGrid);
        panel.Children.Add(_flyoutCalendar);
        panel.Children.Add(_flyoutClearBtn);

        _dateFlyout = new Flyout { Content = panel };
    }

    private void UpdateFlyoutState(DateTimeOffset? dueDate)
    {
        var accentStyle  = ThemeResourceHelper.GetStyle("AccentButtonStyle");
        var defaultStyle = ThemeResourceHelper.GetStyle("DefaultButtonStyle");
        var selected     = dueDate?.ToLocalTime().Date;

        var presetGetters = new Func<DateTime>[]
        {
            () => DueDatePresets.GetToday(DateTime.Today),
            () => DueDatePresets.GetTomorrow(DateTime.Today),
            () => DueDatePresets.GetThisWeekend(DateTime.Today),
            () => DueDatePresets.GetNextWeek(DateTime.Today),
        };

        for (int i = 0; i < _flyoutPresetBtns!.Length; i++)
            _flyoutPresetBtns[i].Style = (selected == presetGetters[i]()) ? accentStyle : defaultStyle;

        _flyoutClearBtn!.Visibility = dueDate.HasValue ? Visibility.Visible : Visibility.Collapsed;

        _updatingFlyout = true;
        _flyoutCalendar!.Date = dueDate.HasValue
            ? (DateTimeOffset?)new DateTimeOffset(dueDate.Value.ToLocalTime().Date, TimeSpan.Zero)
            : null;
        _updatingFlyout = false;
    }

    private void FlyoutCommitAndClose(DateTimeOffset? date)
    {
        _dateFlyout!.Hide();
        if (_flyoutTask == null) return;
        _flyoutTask.DueDate = date;
        if (_paneTask == _flyoutTask)
        {
            _updatingPane = true;
            PaneDueDatePicker.Date = date.HasValue
                ? (DateTimeOffset?)new DateTimeOffset(date.Value.ToLocalTime().Date, TimeSpan.Zero)
                : null;
            _updatingPane = false;
        }
    }
}
