using System.Collections.ObjectModel;
using System.Windows.Input;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    private const int SuggestionsPreviewLimit = 5;
    private bool _showAllSuggestions;

    public ObservableCollection<TodoItem> MySuggestions { get; } = [];

    public ICommand AddSuggestionToMyDayCommand { get; private set; } = null!;
    public ICommand ToggleSuggestionsCommand { get; private set; } = null!;

    public bool HasSuggestions => SuggestionsCount > 0;
    public int SuggestionsCount { get; private set; }
    public bool CanExpandSuggestions => SuggestionsCount > SuggestionsPreviewLimit;
    public string SuggestionsToggleLabel => _showAllSuggestions ? Strings.Suggestions_ShowFewer : Strings.Suggestions_ShowAll;
    public string SuggestionsSummaryText => Strings.Suggestions_Summary(MySuggestions.Count, SuggestionsCount);
    public bool SuggestionsVisible => _activeNavItem == "myday" && HasSuggestions;
    public bool ShowEmptyState => IsLoaded && IsTaskListEmpty && !IsPlannedCalendarVisible;

    public void ResetSuggestionsPreview()
    {
        _showAllSuggestions = false;
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        var candidates = TaskSorting.ForMyDaySuggestions(Tasks, DateTime.Today).ToList();
        SuggestionsCount = candidates.Count;
        if (!CanExpandSuggestions) _showAllSuggestions = false;

        MySuggestions.Clear();
        foreach (var s in candidates.Take(_showAllSuggestions ? candidates.Count : SuggestionsPreviewLimit))
            MySuggestions.Add(s);

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(SuggestionsCount));
        OnPropertyChanged(nameof(CanExpandSuggestions));
        OnPropertyChanged(nameof(SuggestionsToggleLabel));
        OnPropertyChanged(nameof(SuggestionsSummaryText));
        OnPropertyChanged(nameof(SuggestionsVisible));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
