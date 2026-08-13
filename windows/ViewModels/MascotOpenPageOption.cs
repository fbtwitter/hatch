namespace Hatch.ViewModels;

public sealed record MascotOpenPageOption(string? Tag, string DisplayName)
{
    public override string ToString() => DisplayName;
}
