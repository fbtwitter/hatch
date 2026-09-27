using Hatch.Helpers;
using Hatch.Models;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace Hatch.Services;

internal static class MyDayWidgetUpdater
{
    internal static void Refresh(TasksFile data)
    {
        if (!OsVersionHelper.IsWindows11OrGreater || AppDataPath.IsUiTest || !IsPackaged()) return;

        var snapshot = MyDayWidgetCardBuilder.CreateSnapshot(data);
        try
        {
            var manager = WidgetManager.GetDefault();
            foreach (var info in manager.GetWidgetInfos())
            {
                var context = info.WidgetContext;
                if (context.DefinitionId != MyDayWidgetProvider.DefinitionId) continue;

                var request = new WidgetUpdateRequestOptions(context.Id)
                {
                    Template = MyDayWidgetCardBuilder.Build(snapshot, context.Size.ToString()),
                    Data = "{}"
                };
                manager.UpdateWidget(request);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"My Day widget refresh failed: {ex}");
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
