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

    // Returns the appropriate backdrop for this OS: Mica on Win11, acrylic on Win10 2004+, null below that.
    internal static SystemBackdrop? CreateMicaOrFallbackBackdrop() =>
        IsWindows11OrGreater ? new MicaBackdrop()
        : SupportsAcrylic ? new DesktopAcrylicBackdrop()
        : null;
}
