using Hatch.Models;

namespace Hatch.Services;

internal sealed class TaskTombstoneStore
{
    private readonly List<TodoItem> _deletedTasks = [];
    private readonly List<TaskList> _deletedLists = [];

    public TasksFile ResetFrom(TasksFile data)
    {
        _deletedTasks.Clear();
        _deletedTasks.AddRange(data.Tasks.Where(task => task.IsDeleted));
        _deletedLists.Clear();
        _deletedLists.AddRange(data.Lists.Where(list => list.IsDeleted));

        return new TasksFile
        {
            Tasks = data.Tasks.Where(task => !task.IsDeleted).ToList(),
            Lists = data.Lists.Where(list => !list.IsDeleted).ToList()
        };
    }

    public void MarkDeleted(TodoItem task, DateTimeOffset now)
    {
        task.IsDeleted = true;
        task.UpdatedAt = now;
        _deletedTasks.Add(task);
    }

    public void MarkDeleted(TaskList list, DateTimeOffset now)
    {
        list.IsDeleted = true;
        list.UpdatedAt = now;
        _deletedLists.Add(list);
    }

    public void Restore(TodoItem task, DateTimeOffset now)
    {
        task.IsDeleted = false;
        task.UpdatedAt = now;
        _deletedTasks.Remove(task);
    }

    public TasksFile CreateSnapshot(IEnumerable<TodoItem> tasks, IEnumerable<TaskList> lists)
        => new()
        {
            Tasks = [.. tasks, .. _deletedTasks],
            Lists = [.. lists, .. _deletedLists]
        };
}
