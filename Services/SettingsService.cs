using System.Text.Json;
using TodoWinUI3.Models;

namespace TodoWinUI3.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private readonly string _filePath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "TodoWinUI3");
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
        await File.WriteAllTextAsync(_filePath, json);
    }
}
