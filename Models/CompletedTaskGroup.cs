using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hatch.Models;

public sealed class CompletedTaskGroup : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _hasItems;

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
        }
    }

    public ObservableCollection<TodoItem> Items { get; } = [];

    public CompletedTaskGroup()
    {
        Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasItems = Items.Count > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
