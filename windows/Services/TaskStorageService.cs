using System.Text.Json;
using Hatch.Models;

namespace Hatch.Services;

public sealed class TaskStorageService
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions _options = new();

    // Serializes all readers/writers of tasks.json across instances — the debounced
    // ViewModel save and SyncService pulls run on different threads and would otherwise
    // interleave writes to the same file.
    private static readonly SemaphoreSlim _fileLock = new(1, 1);

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

        await _fileLock.WaitAsync();
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
                await WriteFileAsync(file);

            return file;
        }
        catch
        {
            // Returning empty means the next auto-save would overwrite the file —
            // preserve the unreadable original so the user's data is recoverable.
            try { File.Copy(_filePath, _filePath + ".bak", overwrite: true); } catch { }
            return new TasksFile();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(TasksFile data)
    {
        await _fileLock.WaitAsync();
        try
        {
            await WriteFileAsync(data);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // Write to a temp file first so a crash mid-write can never corrupt the only copy.
    private async Task WriteFileAsync(TasksFile data)
    {
        var json = JsonSerializer.Serialize(data, _options);
        var tmpPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, _filePath, overwrite: true);
    }

    public void ResetDataFile()
    {
        _fileLock.Wait();
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
