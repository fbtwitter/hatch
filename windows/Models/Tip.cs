namespace Hatch.Models;

public class Tip
{
    public string Message { get; set; } = string.Empty;
    public TipSeverity Severity { get; set; } = TipSeverity.Info;
    public TipAction? Action { get; set; }
    public int DismissAfterMs { get; set; } = 5000;  // 5s default for low-priority
    public bool IsMeaningful { get; set; } = true;   // false = fallback tip (suppress if user active recently)

    // True only for the once-a-day inspiration line. TipCoordinator stamps
    // LastInspirationDate off this rather than inferring it from Severity/Action, so
    // reordering the engine's tiers cannot silently start consuming the daily slot.
    public bool IsInspiration { get; set; }
}

public class TipAction
{
    public string Label { get; set; } = string.Empty;
    public TipActionType Type { get; set; }
}

public enum TipSeverity
{
    Info = 0,       // low-priority greetings — 3s timeout
    Warning = 1,    // My Day empty, first open — 5s timeout
    Critical = 2    // overdue, actionable — indefinite (user dismisses)
}

public enum TipActionType
{
    None = 0,
    ViewOverdue = 1,
    ViewMyDay = 2,
    AddSampleTask = 3,
    OpenMainWindow = 4,
    ViewPlanned = 5,
    CaptureTask = 6   // put the caret in the quick-add box so a thought can be written down
}
