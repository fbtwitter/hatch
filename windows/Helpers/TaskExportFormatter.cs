using System.Text.Json;
using Hatch.Models;
using Hatch.Services;

namespace Hatch.Helpers;

// Pure formatting — no file I/O here (that's SettingsViewModel.ExportAsync's job).
public static class TaskExportFormatter
{
    private static readonly TaskJsonContext JsonContext = new(new JsonSerializerOptions { WriteIndented = true });

    public static string ToJson(TasksFile data) =>
        JsonSerializer.Serialize(data, JsonContext.TasksFile);
}
