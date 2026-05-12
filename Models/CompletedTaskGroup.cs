using System.Collections.ObjectModel;
using Hatch.Models;

namespace Hatch.Models;

public sealed record CompletedTaskGroup
{
    public required string Name { get; init; }
    public required ObservableCollection<TodoItem> Items { get; init; }
}
