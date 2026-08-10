using Microsoft.UI.Xaml.Media;

namespace Hatch.ViewModels;

// Value/SecondaryValue are pre-formatted strings, not raw numbers — some tiles show a
// fraction ("3 / 3") rather than a single count, and there is nothing further this record
// needs to compute from them, so the ViewModel formats once rather than the view formatting
// on every bind. SecondaryValue is null for every tile except My Day (its "0%" line).
public sealed record StatTileInfo(
    string AutomationId,
    string Title,
    string Value,
    string? SecondaryValue,
    string Description,
    string IconGlyph,
    Brush IconForeground,
    string? NavTag)
{
    // A screen reader announces the button's Name and nothing inside it, so binding the
    // title alone would say "My Day" and drop the number the tile exists to convey.
    public string AutomationName =>
        SecondaryValue is null
            ? $"{Title}: {Value} {Description}"
            : $"{Title}: {Value}, {SecondaryValue} {Description}";
}
