using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;

namespace Hatch.Services;

public sealed class StartupRegistryService
{
    private const string RegPath  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName  = "Hatch";
    public  const string StartupArg   = "--startup";
    private const string StartupTaskId = "HatchStartup";

    // True when running as an MSIX package (Release build).
    // Unpackaged (Debug) uses the registry; packaged uses the StartupTask API.
    private static bool IsPackaged()
    {
        try { _ = Package.Current.Id.Name; return true; }
        catch { return false; }
    }

    public void SetStartupEnabled(bool enabled)
    {
        if (IsPackaged())
            _ = SetPackagedAsync(enabled);
        else
            SetUnpackaged(enabled);
    }

    private static async Task SetPackagedAsync(bool enabled)
    {
        try
        {
            var tasks = await StartupTask.GetForCurrentPackageAsync();
            foreach (var task in tasks)
            {
                if (enabled)
                    await task.RequestEnableAsync();
                else
                    task.Disable();
            }
        }
        catch { }
    }

    private static void SetUnpackaged(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                // Use the running executable's actual path so the entry survives
                // project renames. Include --startup so OnLaunched can distinguish
                // a startup-triggered launch from a normal manual launch.
                var exe = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "hatch.exe");
                if (File.Exists(exe))
                    key.SetValue(AppName, $"\"{exe}\" {StartupArg}");
            }
            else
            {
                try { key.DeleteValue(AppName); } catch { }
            }
        }
        catch { }
    }

}
