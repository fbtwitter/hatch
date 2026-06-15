using System.Text.Json;
using Hatch.Models;

namespace Hatch.Services;

public sealed class TaskStorageService
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions _options = new();

    public TaskStorageService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Hatch");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "tasks.json");
    }

    public async Task<TasksFile> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new TasksFile();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            TasksFile file;
            // Migration: old format was a plain array of TodoItem
            if (json.TrimStart().StartsWith('['))
            {
                var tasks = JsonSerializer.Deserialize<List<TodoItem>>(json) ?? [];
                file = new TasksFile { Tasks = tasks };
            }
            else
            {
                file = JsonSerializer.Deserialize<TasksFile>(json) ?? new TasksFile();
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            bool needsSave = false;
            foreach (var task in file.Tasks)
            {
                if (task.IsInMyDay && task.MyDayDate == null)
                {
                    // Migrate: task was in My Day before MyDayDate existed
                    task.MyDayDate = today;
                    needsSave = true;
                }
                else if (task.IsInMyDay && task.MyDayDate.HasValue && task.MyDayDate < today)
                {
                    // Daily reset: My Day doesn't carry over to a new day
                    task.ResetMyDayForNewDay();
                    needsSave = true;
                }
            }

            if (needsSave)
                await SaveAsync(file);

            return file;
        }
        catch
        {
            return new TasksFile();
        }
    }

    public async Task SaveAsync(TasksFile data)
    {
        var json = JsonSerializer.Serialize(data, _options);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public void ResetDataFile()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
