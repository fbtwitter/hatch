using Microsoft.Win32;

namespace Hatch.Services;

public sealed class StartupRegistryService
{
    private const string StartupRegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Hatch";

    public void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = AppContext.BaseDirectory;
                var fullPath = Path.Combine(exePath, "todo-winui3.exe");
                if (File.Exists(fullPath))
                    key.SetValue(AppName, fullPath);
            }
            else
            {
                try
                {
                    key.DeleteValue(AppName);
                }
                catch { }
            }
        }
        catch { }
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegPath);
            if (key == null) return false;
            return key.GetValue(AppName) != null;
        }
        catch { return false; }
    }
}
