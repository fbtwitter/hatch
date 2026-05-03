using System.Text.Json;
using TodoWinUI3.Models;

namespace TodoWinUI3.Services;

public sealed class TaskStorageService
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public TaskStorageService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "TodoWinUI3");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "tasks.json");
    }

    public async Task<List<TodoItem>> LoadTasksAsync()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<TodoItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveTasksAsync(IEnumerable<TodoItem> tasks)
    {
        var json = JsonSerializer.Serialize(tasks, _options);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
