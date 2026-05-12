using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Hatch.Converters;
using Hatch.Helpers;
using Hatch.Models;
using Hatch.ViewModels;
using Windows.System;

namespace Hatch.Views;

public sealed partial class TaskListPage : Page
{
    private MainViewModel? _vm;
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public TaskListPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        // Re-evaluate due date chip colors (background + foreground) and planned group
        // header foreground — all resolved via ThemeResourceHelper which reads ActualTheme.
        // Re-raising PropertyChanged("DueDate") on each item is enough to trigger the
        // x:Bind converters without rebuilding any item containers.
        if (_vm == null) return;

        foreach (var item in _vm.ActiveTasks)
            item.RefreshDueDateBinding();

        if (_vm.ActiveNavItem == "planned")
            RefreshPlannedGroups();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MainViewModel vm) return;

        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = vm;
        DataContext = vm;
        UpdateView(vm.ActiveNavItem);
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_vm == null) return;
        if (args.PropertyName == nameof(MainViewModel.ActiveNavItem))
            UpdateView(_vm.ActiveNavItem);
        else if (args.PropertyName is nameof(MainViewModel.PlannedGroups) or nameof(MainViewModel.IsPlannedEmpty)
                 && _vm.ActiveNavItem == "planned")
        {
            GroupedListView.Visibility = _vm.IsPlannedEmpty ? Visibility.Collapsed : Visibility.Visible;
            RefreshPlannedGroups();
        }
    }

    private void UpdateView(string navItem)
    {
        HeaderText.Text = navItem switch
        {
            "myday"     => Strings.Header_MyDay,
            "important" => Strings.Header_Important,
            "planned"   => Strings.Header_Planned,
            _           => Strings.Header_AllTasks
        };

        var isPlanned = navItem == "planned";
        FlatListView.Visibility    = isPlanned ? Visibility.Collapsed : Visibility.Visible;
        GroupedListView.Visibility = isPlanned && !(_vm?.IsPlannedEmpty ?? true) ? Visibility.Visible : Visibility.Collapsed;

        if (isPlanned)
            RefreshPlannedGroups();
        else
            RestoreExpanderState(navItem);
    }

    private void RestoreExpanderState(string navItem)
    {
        if (FlatListView.ItemsSource is not IEnumerable<CompletedTaskGroup> groups) return;

        foreach (var group in groups)
        {
            var container = FlatListView.ContainerFromItem(group);
            if (container == null) continue;

            var expander = FindVisualChild<Expander>(container);
            if (expander != null)
            {
                // Open (first) group is always expanded; Completed group respects saved state
                if (group.Name == "Open")
                {
                    expander.IsExpanded = true;
                }
                else if (group.Name.StartsWith("Completed"))
                {
                    bool savedState = _vm?.IsCompletedGroupExpanded(navItem) ?? false;
                    expander.IsExpanded = savedState;

                    // Monitor IsExpanded property changes to persist state
                    expander.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (d, p) =>
                    {
                        if (d is Expander exp && _vm != null)
                            _vm.SetCompletedGroupExpanded(navItem, exp.IsExpanded);
                    });
                }
            }
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }

    private void RefreshPlannedGroups()
    {
        if (_vm == null) return;
        var cvs = (CollectionViewSource)Resources["PlannedGroupsSource"];
        cvs.Source = _vm.PlannedGroups;
    }

    // Overflow button just opens its flyout — no extra logic needed here.
    private void OverflowButton_Click(object sender, RoutedEventArgs e) { }

    private void NewTaskTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddTaskCommand.CanExecute(null))
            ViewModel.AddTaskCommand.Execute(null);
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((Button)sender).Tag;
        DateTimeOffset? pendingDate = task.DueDate;
        bool pendingStarred = task.IsStarred;
        var today = DateTime.Today;

        // ── Shared factory helpers ───────────────────────────────────────────────
        Border SectionCard(UIElement child) => new Border
        {
            CornerRadius    = new CornerRadius(8),
            Background      = ThemeResourceHelper.GetBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush     = ThemeResourceHelper.GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(12),
            Child           = child
        };

        TextBlock SectionLabel(string text) => new TextBlock
        {
            Text       = text,
            Style      = ThemeResourceHelper.GetStyle("CaptionTextBlockStyle"),
            Foreground = ThemeResourceHelper.GetBrush("TextFillColorSecondaryBrush"),
            Margin     = new Thickness(2, 0, 0, 6)
        };

        // ── Section 1: Title ─────────────────────────────────────────────────────
        var titleBox = new TextBox
        {
            Text                = task.Title,
            PlaceholderText     = Strings.EditTask_TitlePlaceholder,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        titleBox.Loaded += (_, _) => titleBox.SelectionStart = titleBox.Text.Length;

        // ── Section 2: Due date ──────────────────────────────────────────────────
        // Preset buttons act as the selection UI — accent style = selected.
        // CalendarDatePicker reflects / sets custom dates.
        // No separate chip needed; the button state IS the indicator.

        var accentStyle  = ThemeResourceHelper.GetStyle("AccentButtonStyle");
        var defaultStyle = ThemeResourceHelper.GetStyle("DefaultButtonStyle");

        // Preset definitions: (label, glyph, normalized date)
        var presets = new (string Label, string Glyph, DateTime Date)[]
        {
            ("Today",     "\uE787", DueDatePresets.GetToday(today)),
            ("Tomorrow",  "\uE816", DueDatePresets.GetTomorrow(today)),
            ("Weekend",   "\uE8F1", DueDatePresets.GetThisWeekend(today)),
            ("Next week", "\uE8BF", DueDatePresets.GetNextWeek(today)),
        };

        var calendarPicker = new CalendarDatePicker
        {
            Date                = pendingDate,
            PlaceholderText     = Strings.EditTask_DatePlaceholder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 8, 0, 0)
        };

        // Tracks the four preset buttons so UpdatePresets can restyle them all.
        var presetButtons = new List<(Button Btn, DateTime Date)>();

        // "Clear date" button — only visible when a date is set
        var clearDateBtn = new Button
        {
            Content                    = Strings.EditTask_ClearDate,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding                    = new Thickness(10, 7, 10, 7),
            Margin                     = new Thickness(0, 8, 0, 0),
            Foreground                 = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush"),
            Visibility                 = pendingDate.HasValue ? Visibility.Visible : Visibility.Collapsed
        };

        void UpdatePresets()
        {
            var selectedDate = pendingDate?.ToLocalTime().Date;
            foreach (var (btn, date) in presetButtons)
                btn.Style = (selectedDate == date) ? accentStyle : defaultStyle;

            clearDateBtn.Visibility = pendingDate.HasValue ? Visibility.Visible : Visibility.Collapsed;

            // Sync CalendarDatePicker — suppress re-entrancy by checking first.
            if (calendarPicker.Date?.Date != pendingDate?.ToLocalTime().Date)
                calendarPicker.Date = pendingDate.HasValue
                    ? (DateTimeOffset?)new DateTimeOffset(pendingDate.Value.ToLocalTime().Date)
                    : null;
        }

        Button MakePresetButton(string label, string glyph, DateTime date)
        {
            var icon  = new FontIcon { Glyph = glyph, FontSize = 13 };
            var txt   = new TextBlock
            {
                Text  = label,
                Style = ThemeResourceHelper.GetStyle("CaptionTextBlockStyle")
            };
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            inner.Children.Add(icon);
            inner.Children.Add(txt);

            var btn = new Button
            {
                Content                    = inner,
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding                    = new Thickness(10, 8, 10, 8),
                CornerRadius               = new CornerRadius(6)
            };

            btn.Click += (_, _) =>
            {
                // Tap selected preset again → deselect (clear date)
                if (pendingDate.HasValue && pendingDate.Value.ToLocalTime().Date == date)
                    pendingDate = null;
                else
                    pendingDate = new DateTimeOffset(date);
                UpdatePresets();
            };

            return btn;
        }

        foreach (var (label, glyph, date) in presets)
        {
            var btn = MakePresetButton(label, glyph, date);
            presetButtons.Add((btn, date));
        }

        calendarPicker.DateChanged += (_, args) =>
        {
            if (args.NewDate.HasValue)
            {
                pendingDate = new DateTimeOffset(args.NewDate.Value.ToLocalTime().Date);
                UpdatePresets();
            }
            else
            {
                pendingDate = null;
                UpdatePresets();
            }
        };

        // 2-column grid for the four preset buttons
        var presetGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < presetButtons.Count; i++)
        {
            Grid.SetColumn(presetButtons[i].Btn, i % 2);
            Grid.SetRow(presetButtons[i].Btn, i / 2);
            presetGrid.Children.Add(presetButtons[i].Btn);
        }

        clearDateBtn.Click += (_, _) => { pendingDate = null; UpdatePresets(); };

        // Apply initial accent state
        UpdatePresets();

        var dateSection = new StackPanel { Spacing = 0 };
        dateSection.Children.Add(presetGrid);
        dateSection.Children.Add(calendarPicker);
        dateSection.Children.Add(clearDateBtn);

        // ── Section 3: Important ─────────────────────────────────────────────────
        var starCheck = new CheckBox
        {
            Content             = Strings.EditTask_MarkImportant,
            IsChecked           = pendingStarred,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0)
        };
        starCheck.Checked   += (_, _) => pendingStarred = true;
        starCheck.Unchecked += (_, _) => pendingStarred = false;

        var importanceRow = starCheck;

        // ── Assemble dialog content ──────────────────────────────────────────────
        var root = new StackPanel { Spacing = 8, MinWidth = 340 };

        root.Children.Add(SectionLabel(Strings.EditTask_Section_Title));
        root.Children.Add(titleBox);

        root.Children.Add(SectionLabel(Strings.EditTask_Section_DueDate));
        root.Children.Add(SectionCard(dateSection));

        root.Children.Add(SectionCard(importanceRow));

        var dialog = new ContentDialog
        {
            Title               = Strings.EditTask_Title,
            Content             = root,
            PrimaryButtonText   = Strings.EditTask_Save,
            SecondaryButtonText = Strings.EditTask_Cancel,
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = this.XamlRoot,
            RequestedTheme      = this.ActualTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var newTitle = titleBox.Text.Trim();
        ViewModel.UpdateTask(
            task,
            string.IsNullOrWhiteSpace(newTitle) ? task.Title : newTitle,
            pendingDate,
            pendingStarred);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var task = (TodoItem)((FrameworkElement)sender).Tag;
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

        var today = DateTime.Today;
        var flyout = new Flyout();

        DateTimeOffset? pendingDate = task.DueDate;

        var accentStyle  = ThemeResourceHelper.GetStyle("AccentButtonStyle");
        var defaultStyle = ThemeResourceHelper.GetStyle("DefaultButtonStyle");

        var presets = new (string Label, string Glyph, DateTime Date)[]
        {
            ("Today",     "\uE787", DueDatePresets.GetToday(today)),
            ("Tomorrow",  "\uE816", DueDatePresets.GetTomorrow(today)),
            ("Weekend",   "\uE8F1", DueDatePresets.GetThisWeekend(today)),
            ("Next week", "\uE8BF", DueDatePresets.GetNextWeek(today)),
        };

        var calendarPicker = new CalendarDatePicker
        {
            Date                = pendingDate,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText     = Strings.EditTask_DatePlaceholder,
            Margin              = new Thickness(0, 8, 0, 0)
        };

        var clearBtn = new Button
        {
            Content                    = Strings.EditTask_ClearDate,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin                     = new Thickness(0, 8, 0, 0),
            Foreground                 = ThemeResourceHelper.GetBrush("SystemFillColorCriticalBrush"),
            Visibility                 = pendingDate.HasValue ? Visibility.Visible : Visibility.Collapsed
        };

        var presetButtons = new List<(Button Btn, DateTime Date)>();

        void CommitAndClose(DateTimeOffset? date)
        {
            flyout.Hide();
            ViewModel.UpdateTaskDueDate(task, date);
        }

        void UpdatePresets()
        {
            var selected = pendingDate?.ToLocalTime().Date;
            foreach (var (btn, date) in presetButtons)
                btn.Style = (selected == date) ? accentStyle : defaultStyle;

            clearBtn.Visibility = pendingDate.HasValue ? Visibility.Visible : Visibility.Collapsed;

            if (calendarPicker.Date?.Date != pendingDate?.ToLocalTime().Date)
                calendarPicker.Date = pendingDate.HasValue
                    ? (DateTimeOffset?)new DateTimeOffset(pendingDate.Value.ToLocalTime().Date)
                    : null;
        }

        // 2-column preset grid
        var presetGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var (label, glyph, date) in presets)
        {
            var icon  = new FontIcon { Glyph = glyph, FontSize = 13 };
            var txt   = new TextBlock { Text = label, Style = ThemeResourceHelper.GetStyle("CaptionTextBlockStyle") };
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            inner.Children.Add(icon);
            inner.Children.Add(txt);

            var btn = new Button
            {
                Content                    = inner,
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding                    = new Thickness(10, 8, 10, 8),
                CornerRadius               = new CornerRadius(6)
            };
            int idx = presetButtons.Count;
            Grid.SetColumn(btn, idx % 2);
            Grid.SetRow(btn, idx / 2);
            presetGrid.Children.Add(btn);

            btn.Click += (_, _) =>
            {
                if (pendingDate.HasValue && pendingDate.Value.ToLocalTime().Date == date)
                    CommitAndClose(null);
                else
                    CommitAndClose(new DateTimeOffset(date));
            };

            presetButtons.Add((btn, date));
        }

        calendarPicker.DateChanged += (_, args) =>
        {
            if (args.NewDate.HasValue)
                CommitAndClose(new DateTimeOffset(args.NewDate.Value.ToLocalTime().Date));
        };

        clearBtn.Click += (_, _) => CommitAndClose(null);

        UpdatePresets();

        var panel = new StackPanel { Spacing = 0, MinWidth = 220 };
        panel.Children.Add(presetGrid);
        panel.Children.Add(calendarPicker);
        panel.Children.Add(clearBtn);

        flyout.Content = panel;
        flyout.ShowAt((FrameworkElement)sender);
    }
}
