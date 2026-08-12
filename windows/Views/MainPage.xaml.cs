using System.Collections.Specialized;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.UI.Core;
using Hatch.Models;
using Hatch.ViewModels;

namespace Hatch.Views;

public sealed partial class MainPage : Page
{
    private MainViewModel _viewModel = null!;
    private bool _suppressNavigation = false;

    private readonly Dictionary<Guid, NavigationViewItem> _customNavItems = new();
    private readonly Dictionary<Guid, TextBlock> _customNavItemNameBlocks = new();
    private readonly Dictionary<Guid, StackPanel> _innerContentPanels = new();

    // Static menu item count (Summary, Separator, My Day, Important, Planned, All Tasks, Separator)
    private const int StaticMenuItemCount = 7;

    public MainViewModel ViewModel => _viewModel;

    public MainPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        Loaded += (_, _) =>
        {
            ApplyContentBackdrop(App.Settings.Backdrop);
            if (NavView.SettingsItem is NavigationViewItem settingsItem)
                ToolTipService.SetPlacement(settingsItem, PlacementMode.Right);
        };

        // handledEventsToo so Ctrl+F reaches the search box even when focus is inside
        // the nav pane or another child control that already handled the key press.
        this.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnPageKeyDown), handledEventsToo: true);
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isCtrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                       & CoreVirtualKeyStates.Down) != 0;
        if (isCtrl && e.Key == VirtualKey.F)
        {
            TitleBarSearchBox.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    // Reason distinguishes real typing from the programmatic Text= set in
    // ViewModel_PropertyChanged below — without this check the two would feed back
    // into each other on every keystroke. Navigation to/from SearchPage is handled
    // centrally in ViewModel_PropertyChanged's SearchQuery case, since SearchQuery can
    // also change via Escape, a nav click, or NavigateToTask — not just typing here.
    private void TitleBarSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _viewModel.SearchQuery = sender.Text;
    }

    private void TitleBarSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        _viewModel.SearchQuery = string.Empty;
        e.Handled = true;
        this.Focus(FocusState.Programmatic);
    }

    // Applies SystemBackdropElement to the page content layer — Windows 11 only.
    // On Windows 10 the element stays Collapsed; the window-level backdrop already
    // handles the Acrylic fallback there.
    public void ApplyContentBackdrop(AppBackdrop backdrop)
    {
        if (!Helpers.OsVersionHelper.IsWindows11OrGreater || backdrop == AppBackdrop.None)
        {
            PageContentBackdrop.Visibility = Visibility.Collapsed;
            return;
        }
        PageContentBackdrop.SystemBackdrop = Helpers.OsVersionHelper.CreateBackdrop(backdrop);
        PageContentBackdrop.Visibility = Visibility.Visible;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm && _viewModel != vm)
        {
            if (_viewModel is not null)
            {
                _viewModel.CustomLists.CollectionChanged -= CustomLists_CollectionChanged;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            _viewModel = vm;
        }
        _viewModel ??= new MainViewModel();

        _viewModel.CustomLists.CollectionChanged += CustomLists_CollectionChanged;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        if (TitleBarSearchBox.Text != _viewModel.SearchQuery)
            TitleBarSearchBox.Text = _viewModel.SearchQuery;

        SyncCustomListNavItems();
        RefreshBadges();

        _suppressNavigation = true;
        SelectNavItem(_viewModel.ActiveNavItem);
        _suppressNavigation = false;

        // Cold start only — no transition on the very first page the user sees.
        NavigateToTaskList(new SuppressNavigationTransitionInfo());
    }

    // ── Custom list nav sync ─────────────────────────────────────────────────

    private void CustomLists_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncCustomListNavItems();
        RefreshBadges();

        _suppressNavigation = true;
        SelectNavItem(_viewModel.ActiveNavItem);
        _suppressNavigation = false;
    }

    private void SyncCustomListNavItems()
    {
        while (NavView.MenuItems.Count > StaticMenuItemCount)
            NavView.MenuItems.RemoveAt(NavView.MenuItems.Count - 1);

        _customNavItems.Clear();
        _customNavItemNameBlocks.Clear();
        _innerContentPanels.Clear();

        var pinned   = _viewModel.CustomLists.Where(l =>  l.IsPinned).ToList();
        var unpinned = _viewModel.CustomLists.Where(l => !l.IsPinned).ToList();

        if (pinned.Count > 0)
        {
            NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "Pinned" });
            foreach (var list in pinned)
                NavView.MenuItems.Add(BuildCustomNavItem(list));
        }

        if (unpinned.Count > 0)
        {
            NavView.MenuItems.Add(new NavigationViewItemHeader { Content = "Lists" });
            foreach (var list in unpinned)
                NavView.MenuItems.Add(BuildCustomNavItem(list));
        }
    }

    private NavigationViewItem BuildCustomNavItem(TaskList list)
    {
        var nameBlock = new TextBlock
        {
            Text = list.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        _customNavItemNameBlocks[list.Id] = nameBlock;

        var contentPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        contentPanel.Children.Add(nameBlock);
        _innerContentPanels[list.Id] = contentPanel;

        var item = new NavigationViewItem
        {
            Tag = list.Id.ToString(),
            Content = contentPanel,
            Icon = BuildListIcon(list)
        };
        ToolTipService.SetToolTip(item, list.Name);
        ToolTipService.SetPlacement(item, PlacementMode.Right);

        item.RightTapped += (s, e) =>
        {
            e.Handled = true;
            ShowListContextMenu(list, item);
        };

        _customNavItems[list.Id] = item;
        return item;
    }

    private static IconElement BuildListIcon(TaskList list)
    {
        if (!string.IsNullOrEmpty(list.CustomIcon))
            return new FontIcon
            {
                Glyph = list.CustomIcon,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 20
            };
        return new SymbolIcon(Symbol.List);
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void ShowListContextMenu(TaskList list, NavigationViewItem anchor)
    {
        var section = _viewModel.CustomLists
            .Where(l => l.IsPinned == list.IsPinned)
            .OrderBy(l => l.SortOrder)
            .ToList();
        int sectionIdx = section.IndexOf(list);

        var menu = new MenuFlyout();

        var pin = new MenuFlyoutItem
        {
            Text = list.IsPinned ? "Unpin" : "Pin",
            Icon = new SymbolIcon(list.IsPinned ? Symbol.UnPin : Symbol.Pin)
        };
        pin.Click += (s, e) => _viewModel.TogglePinList(list);

        var moveUp = new MenuFlyoutItem
        {
            Text = "Move up",
            Icon = new SymbolIcon(Symbol.Upload),
            IsEnabled = sectionIdx > 0
        };
        moveUp.Click += (s, e) => _viewModel.MoveListUp(list);

        var moveDown = new MenuFlyoutItem
        {
            Text = "Move down",
            Icon = new SymbolIcon(Symbol.Download),
            IsEnabled = sectionIdx >= 0 && sectionIdx < section.Count - 1
        };
        moveDown.Click += (s, e) => _viewModel.MoveListDown(list);

        var rename = new MenuFlyoutItem { Text = "Rename", Icon = new SymbolIcon(Symbol.Rename) };
        rename.Click += (s, e) => BeginInlineRename(list);

        var changeIcon = new MenuFlyoutItem { Text = "Change icon", Icon = new SymbolIcon(Symbol.Emoji) };
        changeIcon.Click += (s, e) => ShowIconPickerFlyout(list, anchor);

        var delete = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete) };
        delete.Click += async (s, e) =>
        {
            var taskCount = _viewModel.GetTaskCountForList(list);
            if (taskCount == 0)
            {
                _viewModel.DeleteList(list);
                return;
            }
            var dialog = new ContentDialog
            {
                Title = $"Delete \"{list.Name}\"?",
                Content = $"This will also delete {taskCount} task(s).",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                _viewModel.DeleteList(list);
        };

        menu.Items.Add(pin);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(moveUp);
        menu.Items.Add(moveDown);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(rename);
        menu.Items.Add(changeIcon);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(delete);

        menu.ShowAt(anchor);
    }

    // ── New list flyout ───────────────────────────────────────────────────────

    private void ShowNewListFlyout()
    {
        var textBox = new TextBox
        {
            PlaceholderText = "List name",
            MinWidth = 160,
            MaxLength = 18
        };

        Flyout? flyout = null;
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                _viewModel.AddList(textBox.Text);
                flyout?.Hide();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                flyout?.Hide();
            }
        };

        flyout = new Flyout { Content = textBox };
        flyout.Opened += (s, e) => textBox.Focus(FocusState.Programmatic);
        flyout.ShowAt(NewListFooterItem);
    }

    // ── Inline rename ─────────────────────────────────────────────────────────

    private void BeginInlineRename(TaskList list)
    {
        // Pane is collapsed — nav item content is hidden behind the icon; use a flyout instead.
        if (NavView.DisplayMode == NavigationViewDisplayMode.Compact && !NavView.IsPaneOpen)
        {
            ShowRenameFlyout(list);
            return;
        }

        if (!_customNavItemNameBlocks.TryGetValue(list.Id, out var nameBlock)) return;
        if (!_innerContentPanels.TryGetValue(list.Id, out var panel)) return;

        var idx = panel.Children.IndexOf(nameBlock);
        if (idx < 0) return;

        var textBox = new TextBox
        {
            Text = list.Name,
            Width = 130,
            MaxLength = 18
        };

        panel.Children.RemoveAt(idx);
        panel.Children.Insert(idx, textBox);

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        });

        bool done = false;

        void Commit()
        {
            if (done) return;
            done = true;
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                _viewModel.RenameList(list, textBox.Text);
                nameBlock.Text = list.Name;
            }
            Restore();
        }

        void Restore()
        {
            var i = panel.Children.IndexOf(textBox);
            if (i >= 0) { panel.Children.RemoveAt(i); panel.Children.Insert(i, nameBlock); }
        }

        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter) { e.Handled = true; Commit(); }
            else if (e.Key == VirtualKey.Escape) { e.Handled = true; done = true; Restore(); }
        };

        textBox.LostFocus += (s, e) => Commit();
    }

    private void ShowRenameFlyout(TaskList list)
    {
        if (!_customNavItems.TryGetValue(list.Id, out var navItem)) return;

        var textBox = new TextBox
        {
            Text = list.Name,
            MinWidth = 160,
            MaxLength = 18
        };

        Flyout? flyout = null;
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                _viewModel.RenameList(list, textBox.Text);
                if (_customNavItemNameBlocks.TryGetValue(list.Id, out var nb))
                    nb.Text = list.Name;
                if (_customNavItems.TryGetValue(list.Id, out var navItem))
                    ToolTipService.SetToolTip(navItem, list.Name);
                flyout?.Hide();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                flyout?.Hide();
            }
        };

        flyout = new Flyout { Content = textBox };
        flyout.Opened += (s, e) =>
        {
            textBox.Focus(FocusState.Programmatic);
            textBox.SelectAll();
        };
        flyout.ShowAt(navItem);
    }

    // ── Icon picker flyout ────────────────────────────────────────────────────

    private void ShowIconPickerFlyout(TaskList list, NavigationViewItem anchor)
    {
        var textBox = new TextBox
        {
            PlaceholderText = "Pick an emoji",
            Text = list.CustomIcon ?? string.Empty,
            MinWidth = 160,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            TextAlignment = TextAlignment.Center
        };

        var resetButton = new HyperlinkButton
        {
            Content = "Reset to default",
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4), MinWidth = 160 };
        panel.Children.Add(textBox);
        panel.Children.Add(resetButton);

        Flyout? flyout = null;

        textBox.TextChanged += (s, e) =>
        {
            if (string.IsNullOrEmpty(textBox.Text)) return;
            var first = System.Globalization.StringInfo.GetNextTextElement(textBox.Text);
            if (textBox.Text != first)
            {
                textBox.Text = first;
                textBox.SelectionStart = first.Length;
            }
        };

        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                CommitIcon(list, textBox.Text);
                flyout?.Hide();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                flyout?.Hide();
            }
        };

        resetButton.Click += (s, e) =>
        {
            CommitIcon(list, null);
            flyout?.Hide();
        };

        flyout = new Flyout { Content = panel };
        flyout.Opened += (s, e) =>
        {
            textBox.Focus(FocusState.Programmatic);
            DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                NativeMethods.OpenEmojiPanel);
        };
        flyout.ShowAt(anchor);
    }

    private void CommitIcon(TaskList list, string? icon)
    {
        _viewModel.SetListIcon(list, icon);
        if (_customNavItems.TryGetValue(list.Id, out var navItem))
            navItem.Icon = BuildListIcon(list);
    }

    // ── Badges ───────────────────────────────────────────────────────────────

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.BadgeVersion))
            RefreshBadges();
        else if (e.PropertyName == nameof(MainViewModel.SearchQuery))
        {
            if (TitleBarSearchBox.Text != _viewModel.SearchQuery)
                TitleBarSearchBox.Text = _viewModel.SearchQuery;

            // Single choke point for SearchPage navigation — SearchQuery can change via
            // typing, Escape, a nav click, or NavigateToTask, not just the box itself.
            bool isSearchActive = _viewModel.IsSearchActive;
            bool isOnSearchPage = ContentFrame.Content is SearchPage;

            if (isSearchActive && !isOnSearchPage)
            {
                ContentFrame.Navigate(typeof(SearchPage), _viewModel, new EntranceNavigationTransitionInfo());
            }
            else if (!isSearchActive && isOnSearchPage)
            {
                NavigateBackFromSearch();
            }
        }
    }

    // Restores whichever page/nav item was showing before the search started. Entering
    // SearchPage never touches NavView.SelectedItem, so it's still exactly what the user
    // last clicked — no separate "page before search" field needs to be tracked.
    private void NavigateBackFromSearch()
    {
        if (NavView.SelectedItem is NavigationViewItem item)
        {
            if (ReferenceEquals(item, NavView.SettingsItem))
            {
                ContentFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
                return;
            }
            if (item.Tag as string == "summary")
            {
                ContentFrame.Navigate(typeof(StatsPage), null, new EntranceNavigationTransitionInfo());
                return;
            }
        }

        // Any other nav item (My Day, Important, Planned, All Tasks, a custom list) —
        // ActiveNavItem was never touched by the search detour, so this lands correctly.
        NavigateToTaskList();
    }

    private void RefreshBadges()
    {
        var tasks = _viewModel.Tasks;
        SetBadge(MyDayItem,     tasks.Count(t => t.IsInMyDay  && !t.IsCompleted));
        SetBadge(ImportantItem, tasks.Count(t => t.IsStarred  && !t.IsCompleted));
        SetBadge(PlannedItem,   tasks.Count(t => t.DueDate != null && !t.IsCompleted));
        SetBadge(AllTasksItem,  tasks.Count(t => !t.IsCompleted));

        foreach (var (id, item) in _customNavItems)
            SetBadge(item, tasks.Count(t => t.ListId == id && !t.IsCompleted));
    }

    private static void SetBadge(NavigationViewItem navItem, int count)
    {
        if (count > 0)
        {
            if (navItem.InfoBadge is InfoBadge badge)
                badge.Value = count;
            else
                navItem.InfoBadge = new InfoBadge { Value = count };
        }
        else
        {
            navItem.InfoBadge = null;
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void SelectNavItem(string tag)
    {
        NavigationViewItem[] staticItems = [MyDayItem, ImportantItem, PlannedItem, AllTasksItem];
        foreach (var item in staticItems)
        {
            if (item.Tag?.ToString() == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }

        if (Guid.TryParse(tag, out var listId) && _customNavItems.TryGetValue(listId, out var customItem))
        {
            NavView.SelectedItem = customItem;
            return;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigation) return;

        // Explicitly picking a nav item exits search mode — otherwise a stale query
        // would keep overriding whatever view the user just asked to see.
        if (_viewModel.IsSearchActive)
            _viewModel.SearchQuery = string.Empty;

        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, args.RecommendedNavigationTransitionInfo);
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag || tag == "newlist")
            return;

        if (tag == "summary")
        {
            ContentFrame.Navigate(typeof(StatsPage), null, args.RecommendedNavigationTransitionInfo);
            return;
        }

        _viewModel.ActiveNavItem = tag;
        NavigateToTaskList(args.RecommendedNavigationTransitionInfo);
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked) return;

        if (args.InvokedItemContainer?.Tag?.ToString() == "newlist")
        {
            _suppressNavigation = true;
            SelectNavItem(_viewModel.ActiveNavItem);
            _suppressNavigation = false;

            ShowNewListFlyout();
        }
    }

    private void NavigateToTaskList(NavigationTransitionInfo? transition = null)
    {
        ContentFrame.Navigate(typeof(TaskListPage), _viewModel, transition ?? new EntranceNavigationTransitionInfo());
    }

    public void NavigateTo(string tag)
    {
        if (_viewModel.IsSearchActive)
            _viewModel.SearchQuery = string.Empty;

        _viewModel.ActiveNavItem = tag;
        _suppressNavigation = true;
        SelectNavItem(tag);
        _suppressNavigation = false;
        NavigateToTaskList();
    }

    // Called from SearchPage when a result is tapped. "All Tasks" is the one nav item
    // guaranteed to contain the task regardless of which list/state it actually belongs
    // to — the details pane itself opens from SelectedTask alone, independent of the
    // active filter, so this is just about landing somewhere coherent behind the pane.
    public void NavigateToTask(TodoItem task)
    {
        if (_viewModel.IsSearchActive)
            _viewModel.SearchQuery = string.Empty;

        _viewModel.ActiveNavItem = "alltasks";
        _suppressNavigation = true;
        SelectNavItem("alltasks");
        _suppressNavigation = false;

        NavigateToTaskList();
        _viewModel.SelectedTask = task;
    }

    public void NavigateToSettingsPage()
    {
        ContentFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
        _suppressNavigation = true;
        NavView.SelectedItem = NavView.SettingsItem;
        _suppressNavigation = false;
    }

    // Called by MainWindow when the window is hidden to the tray. Only forwards when
    // TaskListPage is the currently active ContentFrame content — if the user minimized
    // while on Settings/Stats/Sync, TaskListPage is sitting inertly in the Frame's
    // NavigationCacheMode page cache either way, off the interactive path already.
    public void ReleaseTaskListMemory() => (ContentFrame.Content as TaskListPage)?.ReleaseListBindings();

    public void RestoreTaskListMemory() => (ContentFrame.Content as TaskListPage)?.RestoreListBindings();

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        // NavigationView.IsPaneOpen throws E_FAIL (0x80004005) intermittently in Auto mode
        // when toggled near the Expanded/Compact display-mode threshold — WinUI 3 bug.
        try { NavView.IsPaneOpen = !NavView.IsPaneOpen; }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
