namespace Hatch.Models;

public enum AppTheme { SystemDefault = 0, Light = 1, Dark = 2 }
public enum AppBackdrop { None = 0, Mica = 1, MicaAlt = 2, DesktopAcrylic = 3 }

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.SystemDefault;
    public AppBackdrop Backdrop { get; set; } = AppBackdrop.Mica;
    public bool MinimizeToTray { get; set; } = true;
    public int MascotX { get; set; } = -1; // -1 = not yet set; use default on first launch
    public int MascotY { get; set; } = -1;
}
