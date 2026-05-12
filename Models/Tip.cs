namespace Hatch.Models;

public class Tip
{
    public string Text { get; set; } = string.Empty;
    public TipAction? Action { get; set; }
}

public class TipAction
{
    public string Label { get; set; } = string.Empty;
    public TipActionType Type { get; set; }
}

public enum TipActionType
{
    None = 0,
    ViewOverdue = 1,
    ViewMyDay = 2,
    AddSampleTask = 3,
    OpenMainWindow = 4
}
