using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hatch.Helpers;

namespace Hatch.Models;

public sealed class CompletedTaskGroup : INotifyPropertyChanged
{
    private const int PreviewItemCount = 5;
    private string _name = string.Empty;
    private bool _hasItems;
    private bool _showEmptyState;
    private string? _countLabel;
    private bool _isExpanded = true;
    private bool _isCollapsible = true;
    private bool _canReorderItems;
    private bool _previewLimited;
    private bool _showAllItems;

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public bool HasItems
    {
        get => _hasItems;
        private set
        {
            if (_hasItems == value) return;
            _hasItems = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowExpander));
            OnPropertyChanged(nameof(ShowFlatList));
            ShowEmptyState = !value && EmptyMessage != null;
        }
    }

    public bool ShowEmptyState
    {
        get => _showEmptyState;
        private set
        {
            if (_showEmptyState == value) return;
            _showEmptyState = value;
            OnPropertyChanged();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    // Open tasks can render directly, without a header or chevron.
    public bool IsCollapsible
    {
        get => _isCollapsible;
        set
        {
            if (_isCollapsible == value) return;
            _isCollapsible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowExpander));
            OnPropertyChanged(nameof(ShowFlatList));
        }
    }

    // Both variants still respect HasItems — an empty group (e.g. Completed while
    // on Important, which can never gain items) renders nothing, not an empty shell.
    public bool ShowExpander => HasItems && IsCollapsible;
    public bool ShowFlatList => HasItems && !IsCollapsible;

    public bool CanReorderItems
    {
        get => _canReorderItems;
        set
        {
            if (_canReorderItems == value) return;
            _canReorderItems = value;
            OnPropertyChanged();
        }
    }

    // Non-null on the Completed group; updated as items are added/removed.
    // Null on the Open group so no count chip is rendered.
    public string? CountLabel
    {
        get => _countLabel;
        private set
        {
            if (_countLabel == value) return;
            _countLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCountLabel));
        }
    }

    public bool HasCountLabel => _countLabel != null;

    // Set on the Open group only; null on Completed group so it never shows a congrats message.
    public string? EmptyMessage { get; init; }

    // Set to true on the Completed group so the count updates reactively.
    public bool TrackCount { get; init; }

    public ObservableCollection<TodoItem> Items { get; } = [];
    public ObservableCollection<TodoItem> VisibleItems { get; } = [];

    public bool ShowMoreVisible => _previewLimited && !_showAllItems && Items.Count > PreviewItemCount;
    public string ShowMoreLabel => Strings.TaskList_ShowMoreCompleted(Math.Max(0, Items.Count - PreviewItemCount));
    public ICommand ShowMoreCommand { get; internal set; } = null!;

    public CompletedTaskGroup()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasItems = Items.Count > 0;
        if (TrackCount)
            CountLabel = $"{Items.Count} completed";

        UpdateVisibleItems(e);
        OnPropertyChanged(nameof(ShowMoreVisible));
        OnPropertyChanged(nameof(ShowMoreLabel));
    }

    public void ResetPreviewLimit()
    {
        _previewLimited = true;
        _showAllItems = false;
        UpdateVisibleItems();
        OnPropertyChanged(nameof(ShowMoreVisible));
        OnPropertyChanged(nameof(ShowMoreLabel));
    }

    public void ShowAllItems()
    {
        if (!_previewLimited || _showAllItems || Items.Count <= PreviewItemCount) return;
        _showAllItems = true;
        UpdateVisibleItems();
        OnPropertyChanged(nameof(ShowMoreVisible));
        OnPropertyChanged(nameof(ShowMoreLabel));
    }

    private int VisibleItemCount => _previewLimited && !_showAllItems
        ? Math.Min(PreviewItemCount, Items.Count)
        : Items.Count;

    private void UpdateVisibleItems(NotifyCollectionChangedEventArgs? change = null)
    {
        if (change?.Action == NotifyCollectionChangedAction.Add && change.NewItems != null && change.NewStartingIndex >= 0)
        {
            for (int offset = 0; offset < change.NewItems.Count; offset++)
            {
                int index = change.NewStartingIndex + offset;
                if (index < VisibleItemCount && index <= VisibleItems.Count)
                    VisibleItems.Insert(index, (TodoItem)change.NewItems[offset]!);
            }
        }
        else if (change?.Action == NotifyCollectionChangedAction.Remove && change.OldItems != null && change.OldStartingIndex >= 0)
        {
            for (int offset = 0; offset < change.OldItems.Count; offset++)
            {
                if (change.OldStartingIndex < VisibleItems.Count)
                    VisibleItems.RemoveAt(change.OldStartingIndex);
            }
        }
        else if (change?.Action == NotifyCollectionChangedAction.Replace && change.NewItems != null && change.NewStartingIndex >= 0)
        {
            for (int offset = 0; offset < change.NewItems.Count; offset++)
            {
                int index = change.NewStartingIndex + offset;
                if (index < VisibleItems.Count)
                    VisibleItems[index] = (TodoItem)change.NewItems[offset]!;
            }
        }
        else if (change?.Action == NotifyCollectionChangedAction.Move && change.NewItems != null &&
                 change.OldStartingIndex >= 0 && change.NewStartingIndex >= 0)
        {
            int oldIndex = change.OldStartingIndex;
            int newIndex = change.NewStartingIndex;
            if (oldIndex < VisibleItems.Count)
            {
                if (newIndex < VisibleItemCount && newIndex < VisibleItems.Count)
                    VisibleItems.Move(oldIndex, newIndex);
                else
                    VisibleItems.RemoveAt(oldIndex);
            }
            else if (newIndex < VisibleItemCount && newIndex <= VisibleItems.Count)
            {
                VisibleItems.Insert(newIndex, Items[newIndex]);
            }
        }
        else if (change?.Action == NotifyCollectionChangedAction.Reset || change != null)
            VisibleItems.Clear();

        while (VisibleItems.Count > VisibleItemCount)
            VisibleItems.RemoveAt(VisibleItems.Count - 1);
        while (VisibleItems.Count < VisibleItemCount)
            VisibleItems.Add(Items[VisibleItems.Count]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
