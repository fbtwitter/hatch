using System.Collections.ObjectModel;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private string _searchQuery = string.Empty;

    public ObservableCollection<TodoItem> SearchResults { get; } = [];

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearchActive));
            RefreshSearchResults();
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_searchQuery);
    public bool IsSearchEmpty => IsSearchActive && SearchResults.Count == 0;

    private void RefreshSearchResults()
    {
        SearchResults.Clear();
        if (IsSearchActive)
        {
            var query = _searchQuery.Trim();
            foreach (var task in Tasks.Where(t => TaskSearchMatcher.Matches(t, query)).OrderByDescending(t => t.CreatedAt))
                SearchResults.Add(task);
        }
        OnPropertyChanged(nameof(IsSearchEmpty));
    }
}
