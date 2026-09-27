using System.Globalization;
using System.Text.Json.Nodes;
using Hatch.Models;

namespace Hatch.Services;

internal static class MyDayWidgetCardBuilder
{
    private static readonly CultureInfo WidgetCulture = CultureInfo.GetCultureInfo("en-US");

    internal static MyDayWidgetSnapshot CreateSnapshot(TasksFile file)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = file.Tasks ?? [];
        var lists = (file.Lists ?? [])
            .Where(list => !list.IsDeleted)
            .ToDictionary(list => list.Id, list => list.Name);
        var myDay = tasks
            .Where(task => !task.IsDeleted && task.IsInMyDay &&
                           (task.MyDayDate == null || task.MyDayDate == today))
            .OrderBy(task => task.MyDayOrder)
            .ThenByDescending(task => task.CreatedAt)
            .ThenBy(task => task.IsCompleted)
            .ToList();

        var openTasks = myDay
            .Where(task => !task.IsCompleted)
            .Select(task => new MyDayWidgetTask(
                task.Id,
                task.Title,
                BuildMetadata(task, lists, today),
                PriorityColor(task.Priority),
                task.IsStarred))
            .ToList();

        return new MyDayWidgetSnapshot(
            openTasks,
            myDay.Count(task => task.IsCompleted),
            myDay.Count,
            today);
    }

    internal static string Build(MyDayWidgetSnapshot snapshot, string size)
    {
        var body = new JsonArray();
        var isSmall = size.Equals("small", StringComparison.OrdinalIgnoreCase);
        var isLarge = size.Equals("large", StringComparison.OrdinalIgnoreCase);
        var hour = DateTime.Now.Hour;
        var greeting = isSmall ? "My Day" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        var remaining = snapshot.Total - snapshot.Done;

        var header = new JsonObject
        {
            ["type"] = "ColumnSet",
            ["selectAction"] = Execute("myday", "Open My Day"),
            ["columns"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = "stretch",
                    ["items"] = new JsonArray
                    {
                        Text(greeting, "large", weight: "bolder"),
                        Text(snapshot.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM", WidgetCulture), "small", "default", true)
                    }
                },
                new JsonObject
                {
                    ["type"] = "Column",
                    ["width"] = "auto",
                    ["verticalContentAlignment"] = "center",
                    ["selectAction"] = Execute("add", "Add a task"),
                    ["items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "TextBlock",
                            ["text"] = "+",
                            ["size"] = "large",
                            ["weight"] = "bolder",
                            ["color"] = "accent"
                        }
                    }
                }
            }
        };
        body.Add(header);

        var status = snapshot.Total == 0
            ? "Nothing planned yet"
            : remaining == 0
                ? hour >= 18 || hour < 5
                    ? $"All {snapshot.Total} done. Rest easy."
                    : $"All {snapshot.Total} done for today."
                : $"{snapshot.Done} of {snapshot.Total} done · {remaining} left";
        body.Add(Text(status, "default", remaining == 0 && snapshot.Total > 0 ? "accent" : "default", true));

        if (isLarge && snapshot.Total > 0)
        {
            var filled = (int)Math.Round((double)snapshot.Done / snapshot.Total * 10);
            body.Add(Text(new string('■', filled) + new string('□', 10 - filled), "small", "accent"));
        }

        if (!isSmall && snapshot.OpenTasks.Count > 0)
        {
            var rowLimit = isLarge ? 6 : 3;
            foreach (var task in snapshot.OpenTasks.Take(rowLimit))
                body.Add(BuildTaskRow(task));
        }

        return new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = new JsonArray
            {
                Execute("myday", "Open My Day")
            }
        }.ToJsonString();
    }

    private static JsonObject BuildTaskRow(MyDayWidgetTask task)
    {
        var details = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = task.Title,
                ["wrap"] = true,
                ["maxLines"] = 2
            }
        };
        if (task.Metadata != null)
            details.Add(Text(task.Metadata, "small", "default", wrap: false));

        var columns = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Column",
                ["width"] = "auto",
                ["verticalContentAlignment"] = "center",
                ["selectAction"] = Execute($"complete:{task.Id}", $"Complete {task.Title}"),
                ["items"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = "○",
                        ["size"] = "medium",
                        ["color"] = task.PriorityColor
                    }
                }
            },
            new JsonObject
            {
                ["type"] = "Column",
                ["width"] = "stretch",
                ["selectAction"] = Execute($"open:{task.Id}", $"Open {task.Title}"),
                ["items"] = details
            }
        };

        if (task.IsStarred)
        {
            columns.Add(new JsonObject
            {
                ["type"] = "Column",
                ["width"] = "auto",
                ["verticalContentAlignment"] = "center",
                ["selectAction"] = Execute($"open:{task.Id}", $"Open {task.Title}"),
                ["items"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "TextBlock",
                        ["text"] = "★",
                        ["color"] = "warning"
                    }
                }
            });
        }

        return new JsonObject
        {
            ["type"] = "Container",
            ["style"] = "emphasis",
            ["spacing"] = "small",
            ["items"] = new JsonArray
            {
                new JsonObject { ["type"] = "ColumnSet", ["columns"] = columns }
            }
        };
    }

    private static string? BuildMetadata(TodoItem task, IReadOnlyDictionary<Guid, string> lists, DateOnly today)
    {
        var values = new List<string>();
        if (task.DueDate is { } due)
        {
            var date = DateOnly.FromDateTime(due.LocalDateTime);
            var daysOverdue = today.DayNumber - date.DayNumber;
            values.Add(date == today ? "Due today"
                : date == today.AddDays(1) ? "Due tomorrow"
                : daysOverdue > 0 ? $"Overdue ({daysOverdue}d)"
                : date.ToDateTime(TimeOnly.MinValue).ToString("d MMM yyyy", WidgetCulture));
        }

        if (task.Steps.Count > 0)
            values.Add($"{task.Steps.Count(step => step.IsCompleted)}/{task.Steps.Count}");
        if (lists.TryGetValue(task.ListId, out var listName) && !string.IsNullOrWhiteSpace(listName))
            values.Add(listName);

        return values.Count == 0 ? null : string.Join(" · ", values);
    }

    private static string PriorityColor(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => "accent",
        TaskPriority.Medium => "warning",
        TaskPriority.High => "attention",
        _ => "default"
    };

    private static JsonObject Text(string value, string size = "default", string color = "default", bool wrap = false, string? weight = null)
    {
        var block = new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = value,
            ["size"] = size,
            ["color"] = color,
            ["wrap"] = wrap
        };
        if (weight != null) block["weight"] = weight;
        return block;
    }

    private static JsonObject Execute(string verb, string title) => new()
    {
        ["type"] = "Action.Execute",
        ["verb"] = verb,
        ["title"] = title,
        ["associatedInputs"] = "none"
    };
}
