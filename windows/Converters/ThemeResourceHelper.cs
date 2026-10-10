using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Hatch.Converters;

internal static class ThemeResourceHelper
{
    private static readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();

    public static Windows.UI.Color GetColor(string key)
        => TryFindInDictionary(Application.Current.Resources, key, ResolveThemeKey(), out var value)
            && value is Windows.UI.Color color ? color : Microsoft.UI.Colors.Transparent;

    public static Brush GetBrush(string key)
    {
        if (TryFindInDictionary(Application.Current.Resources, key, ResolveThemeKey(), out var value)
            && value is Brush brush)
            return brush;

        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public static Style GetStyle(string key)
    {
        if (TryFindInDictionary(Application.Current.Resources, key, ResolveThemeKey(), out var value)
            && value is Style style)
            return style;

        return new Style();
    }

    // Explicitly select nested theme dictionaries before ambient application lookup:
    // the app's chosen theme can differ from Windows, and Dark often uses "Default".
    private static bool TryFindInDictionary(ResourceDictionary rd, string key, string themeKey, out object? value)
    {
        string[] themeKeys = Accessibility.HighContrast
            ? ["HighContrast", themeKey, "Default"] : [themeKey, "Default"];
        foreach (var candidate in themeKeys)
            if (rd.ThemeDictionaries.TryGetValue(candidate, out var dictionary)
                && dictionary is ResourceDictionary themed
                && TryFindInDictionary(themed, key, themeKey, out value))
                return true;

        for (int i = rd.MergedDictionaries.Count - 1; i >= 0; i--)
            if (TryFindInDictionary(rd.MergedDictionaries[i], key, themeKey, out value))
                return true;

        return rd.TryGetValue(key, out value);
    }

    private static string ResolveThemeKey()
    {
        var theme = App.GetElementTheme(App.Settings.Theme);
        if (theme == ElementTheme.Default && App.MainWindowInstance?.Content is FrameworkElement root)
            theme = root.ActualTheme;
        return theme == ElementTheme.Light ? "Light" : "Default";
    }
}
