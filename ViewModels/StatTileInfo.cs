using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

public sealed record StatTileInfo(
    string AutomationId,
    int Value,
    string Label,
    string IconGlyph,
    Brush IconForeground,
    Brush BadgeBackground);
