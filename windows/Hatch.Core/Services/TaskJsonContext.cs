using System.Text.Json.Serialization;
using Hatch.Models;

namespace Hatch.Services;

[JsonSerializable(typeof(TasksFile))]
[JsonSerializable(typeof(List<TodoItem>))]
public partial class TaskJsonContext : JsonSerializerContext;
