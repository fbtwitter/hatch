using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
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

    // Static menu item count (My Day, Important, Planned, All Tasks, Separator)
    private const int StaticMenuItemCount = 5;

    public MainViewModel ViewModel => _viewModel;

    public MainPage()
    {
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is MainViewModel vm && _viewModel != vm)
        {
            if (_viewModel is not null)
                _viewModel.CustomLists.CollectionChanged -= CustomLists_CollectionChanged;
            _viewModel = vm;
        }
        _viewModel ??= new MainViewModel();

        _viewModel.CustomLists.CollectionChanged += CustomLists_CollectionChanged;

        SyncCustomListNavItems();

        _suppressNavigation = true;
        SelectNavItem(_viewModel.ActiveNavItem);
        _suppressNavigation = false;

        NavigateToTaskList();
    }

    // ── Custom list nav sync ─────────────────────────────────────────────────

    private void CustomLists_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncCustomListNavItems();

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

        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, new DrillInNavigationTransitionInfo());
            MainTitleBar.IsBackButtonVisible = true;
        }
        else if (args.SelectedItem is NavigationViewItem item &&
                 item.Tag is string tag &&
                 tag != "newlist")
        {
            _viewModel.ActiveNavItem = tag;
            NavigateToTaskList();
        }
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

    private void NavigateToTaskList()
    {
        ContentFrame.Navigate(typeof(TaskListPage), _viewModel, new SuppressNavigationTransitionInfo());
        ContentFrame.BackStack.Clear();
        MainTitleBar.IsBackButtonVisible = false;
    }

    public void NavigateTo(string tag)
    {
        _viewModel.ActiveNavItem = tag;
        _suppressNavigation = true;
        SelectNavItem(tag);
        _suppressNavigation = false;
        NavigateToTaskList();
    }

    public void NavigateToSettingsPage()
    {
        ContentFrame.Navigate(typeof(SettingsPage), null, new DrillInNavigationTransitionInfo());
        MainTitleBar.IsBackButtonVisible = true;
        _suppressNavigation = true;
        NavView.SelectedItem = NavView.SettingsItem;
        _suppressNavigation = false;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();

        MainTitleBar.IsBackButtonVisible = ContentFrame.CanGoBack;

        if (ContentFrame.Content is TaskListPage)
        {
            _suppressNavigation = true;
            SelectNavItem(_viewModel.ActiveNavItem);
            _suppressNavigation = false;
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        // NavigationView.IsPaneOpen throws E_FAIL (0x80004005) intermittently in Auto mode
        // when toggled near the Expanded/Compact display-mode threshold — WinUI 3 bug.
        try { NavView.IsPaneOpen = !NavView.IsPaneOpen; }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
