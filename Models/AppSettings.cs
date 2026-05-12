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
    public int MascotSize { get; set; } = 120; // Window size in pixels
    public bool MuteAnimation { get; set; } = false;
    public bool LockMascotPosition { get; set; } = false;
    public string? LottieFilePath { get; set; } = null;
    public string ActiveNavItem { get; set; } = "alltasks";
    public Guid LastUsedListId { get; set; } = Guid.Empty;
    public bool FirstRunComplete { get; set; } = false;
    public long? HideUntilTicks { get; set; } = null; // DateTime.UtcNow.Ticks when hide expires
    public bool MascotAlwaysOnTop { get; set; } = true;
    public bool HideWhenFullscreen { get; set; } = true;
    public bool RunAtStartup { get; set; } = false;

    // Global hotkey — default Ctrl+Shift+Space
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0004; // MOD_CONTROL | MOD_SHIFT
    public uint HotkeyVirtualKey { get; set; } = 0x20;            // VK_SPACE

    // Tip Engine — tracks when bubble was last auto-opened to enforce one-per-day
    public DateTime? LastTipShowDate { get; set; } = null;
}
