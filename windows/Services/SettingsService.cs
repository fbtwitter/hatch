using System.Text.Json;
using Hatch.Models;

namespace Hatch.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    // SaveAsync is called fire-and-forget from many settings setters — serialize the
    // writes so they can't interleave on the same file.
    private static readonly SemaphoreSlim _fileLock = new(1, 1);

    private readonly string _filePath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Hatch");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "settings.json");
    }

    public void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch { Current = new(); }
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Current, _options);
        await _fileLock.WaitAsync();
        try
        {
            var tmpPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
