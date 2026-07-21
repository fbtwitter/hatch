using System.Collections.ObjectModel;
using System.Windows.Input;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<TodoItem> MySuggestions { get; } = [];

    public ICommand AddSuggestionToMyDayCommand { get; private set; } = null!;

    public bool HasSuggestions => MySuggestions.Count > 0;
    public bool SuggestionsVisible => _activeNavItem == "myday" && HasSuggestions;
    public bool ShowEmptyState => IsTaskListEmpty && !SuggestionsVisible;

    private void RefreshSuggestions()
    {
        var candidates = Tasks
            .Where(t => !t.IsCompleted && !t.IsInMyDay)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        MySuggestions.Clear();
        foreach (var s in candidates)
            MySuggestions.Add(s);

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(SuggestionsVisible));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
