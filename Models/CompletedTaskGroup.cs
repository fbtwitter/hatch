using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hatch.Models;

public sealed class CompletedTaskGroup : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _hasItems;
    private bool _showEmptyState;
    private string? _countLabel;

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

    public CompletedTaskGroup()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasItems = Items.Count > 0;
        if (TrackCount)
            CountLabel = Items.Count.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
