using System.Text.Json;
using Hatch.Helpers;
using Hatch.Models;

namespace Hatch.Services;

internal static class MyDayWidgetSnapshotReader
{
    internal static async Task<TasksFile> LoadAsync()
    {
        var path = Path.Combine(AppDataPath.Folder, "tasks.json");
        if (!File.Exists(path)) return new TasksFile();

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            return json.TrimStart().StartsWith('[')
                ? new TasksFile { Tasks = JsonSerializer.Deserialize<List<TodoItem>>(json) ?? [] }
                : JsonSerializer.Deserialize<TasksFile>(json) ?? new TasksFile();
        }
        catch
        {
            return new TasksFile();
        }
    }
}
