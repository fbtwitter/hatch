using System.Text.Json;
using Hatch.Models;

namespace Hatch.Helpers;

// Pure formatting — no file I/O here (that's SettingsViewModel.ExportAsync's job).
public static class TaskExportFormatter
{
    public static string ToJson(TasksFile data) =>
        JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
}
