using Hatch.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Helpers;

internal static class OsVersionHelper
{
    // MicaBackdrop requires Windows 11 build 22000+.
    internal static bool IsWindows11OrGreater =>
        Environment.OSVersion.Version.Build >= 22000;

    // DesktopAcrylicBackdrop requires Windows 10 2004 (build 19041)+.
    internal static bool SupportsAcrylic =>
        Environment.OSVersion.Version.Build >= 19041;

    // Returns Mica on Win11, acrylic fallback on Win10 2004+, null below that.
    // Used for windows/elements whose backdrop is always Mica (e.g. QuickAddBubbleWindow).
    internal static SystemBackdrop? CreateMicaOrFallbackBackdrop() =>
        IsWindows11OrGreater ? new MicaBackdrop()
        : SupportsAcrylic ? new DesktopAcrylicBackdrop()
        : null;

    // Creates the correct backdrop for the given AppBackdrop setting, respecting OS support.
    // Mica/MicaAlt require Win11; DesktopAcrylic requires Win10 2004+.
    internal static SystemBackdrop? CreateBackdrop(AppBackdrop backdrop) => backdrop switch
    {
        AppBackdrop.Mica    when IsWindows11OrGreater => new MicaBackdrop(),
        AppBackdrop.MicaAlt when IsWindows11OrGreater => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        AppBackdrop.Mica or AppBackdrop.MicaAlt or AppBackdrop.DesktopAcrylic
                            when SupportsAcrylic       => new DesktopAcrylicBackdrop(),
        _                                              => null
    };
}
