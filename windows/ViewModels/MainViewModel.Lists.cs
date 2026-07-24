using System.Collections.ObjectModel;
using Hatch.Models;

namespace Hatch.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<TaskList> Lists { get; } = [];
    public ObservableCollection<TaskList> CustomLists { get; } = [];

    public void AddList(string name)
    {
        var list = new TaskList
        {
            Name = name.Trim(),
            AccentColor = "#0078D4",
            SortOrder = CustomLists.Count
        };
        CustomLists.Add(list);
        SaveAsync();
    }

    public void RenameList(TaskList list, string newName)
    {
        list.Name = newName.Trim();
        list.UpdatedAt = DateTimeOffset.UtcNow;
        RefreshListNames();
        SaveAsync();
    }

    public void SetListIcon(TaskList list, string? icon)
    {
        list.CustomIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        list.UpdatedAt = DateTimeOffset.UtcNow;
        SaveAsync();
    }

    public void TogglePinList(TaskList list)
    {
        list.IsPinned = !list.IsPinned;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        var sorted = CustomLists.OrderByDescending(l => l.IsPinned).ThenBy(l => l.SortOrder).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = CustomLists.IndexOf(sorted[i]);
            if (current != i) CustomLists.Move(current, i);
        }
        SaveAsync();
    }

    public void ReorderList(Guid id, int newSectionIndex, bool newIsPinned)
    {
        var list = CustomLists.FirstOrDefault(l => l.Id == id);
        if (list == null) return;

        bool oldIsPinned = list.IsPinned;
        list.IsPinned = newIsPinned;
        list.UpdatedAt = DateTimeOffset.UtcNow;

        var targetSection = CustomLists
            .Where(l => l.IsPinned == newIsPinned && l.Id != id)
            .OrderBy(l => l.SortOrder)
            .ToList();
        targetSection.Insert(Math.Clamp(newSectionIndex, 0, targetSection.Count), list);
        for (int i = 0; i < targetSection.Count; i++)
            targetSection[i].SortOrder = i;

        if (oldIsPinned != newIsPinned)
        {
            var oldSection = CustomLists
                .Where(l => l.IsPinned == oldIsPinned)
                .OrderBy(l => l.SortOrder)
                .ToList();
            for (int i = 0; i < oldSection.Count; i++)
                oldSection[i].SortOrder = i;
        }

        var sorted = CustomLists.OrderByDescending(l => l.IsPinned).ThenBy(l => l.SortOrder).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = CustomLists.IndexOf(sorted[i]);
            if (current != i) CustomLists.Move(current, i);
        }

        SaveAsync();
    }

    public void MoveListUp(TaskList list)
    {
        var section = CustomLists.Where(l => l.IsPinned == list.IsPinned).OrderBy(l => l.SortOrder).ToList();
        int idx = section.IndexOf(list);
        if (idx <= 0) return;
        ReorderList(list.Id, idx - 1, list.IsPinned);
    }

    public void MoveListDown(TaskList list)
    {
        var section = CustomLists.Where(l => l.IsPinned == list.IsPinned).OrderBy(l => l.SortOrder).ToList();
        int idx = section.IndexOf(list);
        if (idx < 0 || idx >= section.Count - 1) return;
        ReorderList(list.Id, idx + 1, list.IsPinned);
    }

    public int GetTaskCountForList(TaskList list) => Tasks.Count(t => t.ListId == list.Id);

    private void RefreshListNames()
    {
        var listMap = CustomLists.ToDictionary(l => l.Id, l => l.Name);
        foreach (var task in Tasks)
            task.ListName = task.ListId == Guid.Empty
                ? "Task"
                : listMap.TryGetValue(task.ListId, out var name) ? name : null;
    }

    public void DeleteList(TaskList list)
    {
        // Navigate away before removing tasks so the list view doesn't flicker.
        if (_activeNavItem == list.Id.ToString())
            ActiveNavItem = "alltasks";

        var tasksToRemove = Tasks.Where(t => t.ListId == list.Id).ToList();
        foreach (var task in tasksToRemove)
        {
            task.PropertyChanged -= TaskPropertyChanged;
            App.NotificationScheduler.UnscheduleForTask(task.Id);
            Tasks.Remove(task);
        }

        CustomLists.Remove(list);
        SaveAsync();
    }
}
