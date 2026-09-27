using System.Collections.Concurrent;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using Windows.System;

namespace Hatch.Services;

internal sealed class MyDayWidgetProvider : IWidgetProvider
{
    internal const string DefinitionId = "Hatch_MyDay";
    internal static readonly ManualResetEvent EmptyWidgetListEvent = new(false);

    private static readonly ConcurrentDictionary<string, byte> WidgetIds = new(StringComparer.Ordinal);

    public void CreateWidget(WidgetContext widgetContext) => Update(widgetContext);

    public void DeleteWidget(string widgetId, string customState)
    {
        WidgetIds.TryRemove(widgetId, out _);
        if (WidgetIds.IsEmpty)
            EmptyWidgetListEvent.Set();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var verb = actionInvokedArgs.Verb;
        string? uri = verb switch
        {
            "myday" => "hatch://myday",
            "add" => "hatch://add",
            _ when verb.StartsWith("complete:", StringComparison.Ordinal) &&
                   Guid.TryParse(verb[9..], out var id) => $"hatch://complete?id={id}",
            _ when verb.StartsWith("open:", StringComparison.Ordinal) &&
                   Guid.TryParse(verb[5..], out var id) => $"hatch://opentask?id={id}",
            _ => null
        };

        if (uri != null)
            _ = Launcher.LaunchUriAsync(new Uri(uri));
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
        => Update(contextChangedArgs.WidgetContext);

    public void Activate(WidgetContext widgetContext) => Update(widgetContext);

    public void Deactivate(string widgetId) { }

    private static void Update(WidgetContext context)
    {
        if (context.DefinitionId != DefinitionId) return;

        WidgetIds[context.Id] = 0;
        EmptyWidgetListEvent.Reset();

        _ = UpdateAsync(context.Id, context.Size.ToString());
    }

    private static async Task UpdateAsync(string widgetId, string size)
    {
        try
        {
            var snapshot = MyDayWidgetCardBuilder.CreateSnapshot(await MyDayWidgetSnapshotReader.LoadAsync());
            if (!WidgetIds.ContainsKey(widgetId)) return;

            var request = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = MyDayWidgetCardBuilder.Build(snapshot, size),
                Data = "{}"
            };
            WidgetManager.GetDefault().UpdateWidget(request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"My Day widget update failed: {ex}");
        }
    }
}
