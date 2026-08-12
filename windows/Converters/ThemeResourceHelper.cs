using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

// TryGetValue on a ResourceDictionary only searches the top level — WinUI stores
// system tokens inside nested MergedDictionaries, so we walk them recursively.
internal static class ThemeResourceHelper
{
    public static Brush GetBrush(string key)
    {
        var themeKey = ResolveThemeKey();

        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var dict)
            && dict is ResourceDictionary rd
            && TryFindInDictionary(rd, key, out var themed)
            && themed is Brush themedBrush)
            return themedBrush;

        // Fallback: search app-level resources (non-theme-specific)
        if (TryFindInDictionary(Application.Current.Resources, key, out var fallback)
            && fallback is Brush fallbackBrush)
            return fallbackBrush;

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public static Style GetStyle(string key)
    {
        var themeKey = ResolveThemeKey();

        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out var dict)
            && dict is ResourceDictionary rd
            && TryFindInDictionary(rd, key, out var themed)
            && themed is Style themedStyle)
            return themedStyle;

        if (TryFindInDictionary(Application.Current.Resources, key, out var fallback)
            && fallback is Style fallbackStyle)
            return fallbackStyle;

        return new Style();
    }

    public static bool IsDarkTheme() => ResolveThemeKey() == "Dark";

    public static Brush GetThemedBrush(Windows.UI.Color lightColor, Windows.UI.Color darkColor)
    {
        var color = IsDarkTheme() ? darkColor : lightColor;
        return new SolidColorBrush(color);
    }

    // WinUI stores system tokens (e.g. ControlFillColorDefaultBrush) in nested
    // MergedDictionaries that TryGetValue alone cannot reach.
    private static bool TryFindInDictionary(ResourceDictionary rd, string key, out object? value)
    {
        if (rd.TryGetValue(key, out value))
            return true;

        foreach (var merged in rd.MergedDictionaries)
            if (TryFindInDictionary(merged, key, out value))
                return true;

        value = null;
        return false;
    }

    private static string ResolveThemeKey()
    {
        if (App.MainWindowInstance?.Content is FrameworkElement root)
        {
            return root.ActualTheme switch
            {
                ElementTheme.Dark  => "Dark",
                ElementTheme.Light => "Light",
                _                  => "Default"
            };
        }
        return "Default";
    }
}
