using System.Collections.ObjectModel;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private const int SearchDebounceMs = 250;

    private string _searchQuery = string.Empty;
    private CancellationTokenSource? _searchDebounceToken;

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
            DebounceSearchResults();
        }
    }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_searchQuery);
    public bool IsSearchEmpty => IsSearchActive && SearchResults.Count == 0;

    public string SearchResultsSummary
    {
        get
        {
            int n = SearchResults.Count;
            return $"{n} result{(n == 1 ? "" : "s")} for “{_searchQuery.Trim()}”";
        }
    }

    // Debounced: only the typing path (SearchQuery setter) goes through here — data-change
    // paths (a task edited/added/removed while search is active) call RefreshSearchResults
    // directly, since those are rare compared to every keystroke and should reflect instantly.
    private void DebounceSearchResults()
    {
        var previous = _searchDebounceToken;
        previous?.Cancel();
        _searchDebounceToken = new CancellationTokenSource();
        _ = DebounceSearchResultsAsync(_searchDebounceToken.Token);
        previous?.Dispose();
    }

    private async Task DebounceSearchResultsAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, ct);
            RefreshSearchResults();
        }
        catch (OperationCanceledException) { }
    }

    private void RefreshSearchResults()
    {
        SearchResults.Clear();
        if (IsSearchActive)
        {
            var query = _searchQuery.Trim();
            foreach (var task in TaskSorting.NewestFirst(Tasks.Where(t => TaskSearchMatcher.Matches(t, query))))
                SearchResults.Add(task);
        }
        OnPropertyChanged(nameof(IsSearchEmpty));
        OnPropertyChanged(nameof(SearchResultsSummary));
    }
}
