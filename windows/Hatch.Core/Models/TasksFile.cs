namespace Hatch.Models;

public sealed class TasksFile
{
    public List<TodoItem> Tasks { get; set; } = [];
    public List<TaskList> Lists { get; set; } = [];
}
