using Hatch.Models;

namespace Hatch.ViewModels;

public sealed record UpcomingTaskInfo(TodoItem Task, string Title, string? Detail);
