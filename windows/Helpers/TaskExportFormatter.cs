using System.Text;
using System.Text.Json;
using Hatch.Models;

namespace Hatch.Helpers;

// Pure formatting — no file I/O here (that's SettingsViewModel.ExportAsync's job).
public static class TaskExportFormatter
{
    public static string ToJson(TasksFile data) =>
        JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

    public static string ToCsv(TasksFile data)
    {
        var listNames = BuildListNameMap(data);
        var sb = new StringBuilder();
        sb.AppendLine("Title,List,Due Date,Priority,Completed,Tags,Notes");

        foreach (var t in data.Tasks)
        {
            sb.AppendLine(string.Join(",",
                CsvField(t.Title),
                CsvField(ListNameFor(t, listNames)),
                CsvField(t.DueDate?.ToString("yyyy-MM-dd") ?? ""),
                CsvField(t.HasPriority ? t.Priority.ToString() : ""),
                CsvField(t.IsCompleted ? "Yes" : "No"),
                CsvField(string.Join("; ", t.Tags)),
                CsvField(t.Notes ?? "")));
        }

        return sb.ToString();
    }

    public static string ToMarkdown(TasksFile data)
    {
        var listNames = BuildListNameMap(data);
        var sb = new StringBuilder();
        sb.AppendLine($"# Hatch Tasks — {DateTime.Now:yyyy-MM-dd}");
        sb.AppendLine();

        foreach (var group in data.Tasks
            .GroupBy(t => ListNameFor(t, listNames))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {group.Key}");

            foreach (var t in group.OrderBy(t => t.IsCompleted).ThenByDescending(TaskSorting.CreatedInstant))
            {
                var box = t.IsCompleted ? "[x]" : "[ ]";
                var meta = new List<string>();
                if (t.DueDate is { } due) meta.Add($"due {due:MMM d}");
                if (t.HasPriority) meta.Add(t.Priority.ToString());
                if (t.Tags.Count > 0) meta.Add(string.Join(" ", t.Tags.Select(tag => $"#{tag}")));
                var suffix = meta.Count > 0 ? $" ({string.Join(", ", meta)})" : "";

                sb.AppendLine($"- {box} {t.Title}{suffix}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static Dictionary<Guid, string> BuildListNameMap(TasksFile data) =>
        data.Lists.ToDictionary(l => l.Id, l => l.Name);

    private static string ListNameFor(TodoItem task, Dictionary<Guid, string> listNames) =>
        task.ListId == Guid.Empty
            ? "Task"
            : listNames.TryGetValue(task.ListId, out var name) ? name : "Task";

    private static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
