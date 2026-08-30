namespace Hatch.Models;

// ADR-0010 — one ordered step (subtask / checklist item) within a TodoItem.
// Phase 1 on Windows: carried through load/save and sync untouched, no desktop UI yet.
// Per-step Id/UpdatedAt are stored now even though v3 merges at parent granularity
// (whole-record LWW on TodoItem.UpdatedAt) — it costs nothing, gives every client stable
// list keys, and lets a later ADR move to per-step merge without another wire break.
public sealed class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
