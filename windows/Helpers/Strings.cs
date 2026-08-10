using Microsoft.Windows.ApplicationModel.Resources;

namespace Hatch.Helpers;

/// <summary>
/// Typed, static access to Strings/en-US/Resources.resw.
/// Use this from C# code-behind and view-models.
/// XAML should use x:Uid instead.
/// </summary>
internal static class Strings
{
    private static readonly ResourceLoader _loader = new ResourceLoader();

    public static string Get(string key) => _loader.GetString(key);

    // ── Navigation ────────────────────────────────────────────
    // Navigation item Content and Tooltip are set via x:Uid in XAML.
    // These are not needed in C# — nav labels are handled by Header_* below.

    // ── Page headers ─────────────────────────────────────────
    public static string Header_MyDay     => Get("Header_MyDay");
    public static string Header_Important => Get("Header_Important");
    public static string Header_Planned   => Get("Header_Planned");
    public static string Header_AllTasks  => Get("Header_AllTasks");

    // ── Empty states ─────────────────────────────────────────
    public static string EmptyState_MyDay_Headline       => Get("EmptyState_MyDay_Headline");
    public static string EmptyState_MyDay_Subtext        => Get("EmptyState_MyDay_Subtext");
    public static string EmptyState_Important_Headline   => Get("EmptyState_Important_Headline");
    public static string EmptyState_Important_Subtext    => Get("EmptyState_Important_Subtext");
    public static string EmptyState_Planned_Headline     => Get("EmptyState_Planned_Headline");
    public static string EmptyState_Planned_Subtext      => Get("EmptyState_Planned_Subtext");
    public static string EmptyState_AllTasks_Headline    => Get("EmptyState_AllTasks_Headline");
    public static string EmptyState_AllTasks_Subtext     => Get("EmptyState_AllTasks_Subtext");
    public static string EmptyState_CustomList_Headline  => Get("EmptyState_CustomList_Headline");
    public static string EmptyState_CustomList_Subtext   => Get("EmptyState_CustomList_Subtext");

    // ── Task row ─────────────────────────────────────────────
    public static string Task_Tooltip_MarkComplete  => Get("Task_Tooltip_MarkComplete");
    public static string Task_Tooltip_ChangeDueDate => Get("Task_Tooltip_ChangeDueDate");
    public static string Task_Tooltip_SetDueDate    => Get("Task_Tooltip_SetDueDate");
    public static string Task_Chip_AddDate          => Get("Task_Chip_AddDate");
    public static string Task_Tooltip_Star_Add      => Get("Task_Tooltip_Star_Add");
    public static string Task_Tooltip_Star_Remove   => Get("Task_Tooltip_Star_Remove");
    public static string Task_Tooltip_Edit          => Get("Task_Tooltip_Edit");
    public static string Task_Tooltip_More          => Get("Task_Tooltip_More");
    public static string Task_Menu_Delete           => Get("Task_Menu_Delete");

    // ── Undo snackbar ────────────────────────────────────────
    public static string UndoMessage_TaskCompleted => Get("UndoMessage_TaskCompleted");
    public static string UndoMessage_TaskDeleted   => Get("UndoMessage_TaskDeleted");

    // ── New task ─────────────────────────────────────────────
    // PlaceholderText and tooltip are set via x:Uid in XAML.

    // ── Due date chip ─────────────────────────────────────────
    public static string DueDate_Today    => Get("DueDate_Today");
    public static string DueDate_Tomorrow => Get("DueDate_Tomorrow");
    public static string DueDate_Overdue(int days) => string.Format(Get("DueDate_Overdue"), days);

    // ── Summary page tiles ────────────────────────────────────
    public static string Stats_Tile_Overdue        => Get("Stats_Tile_Overdue");
    public static string Stats_Tile_CompletedToday => Get("Stats_Tile_CompletedToday");
    public static string Stats_Tile_Open           => Get("Stats_Tile_Open");

    // ── Planned group headers ─────────────────────────────────
    public static string PlannedGroup_Overdue   => Get("PlannedGroup_Overdue");
    public static string PlannedGroup_Today     => Get("PlannedGroup_Today");
    public static string PlannedGroup_Tomorrow  => Get("PlannedGroup_Tomorrow");
    public static string PlannedGroup_ThisWeek  => Get("PlannedGroup_ThisWeek");
    public static string PlannedGroup_Later     => Get("PlannedGroup_Later");

    // ── Edit task dialog ─────────────────────────────────────
    public static string EditTask_Title           => Get("EditTask_Title");
    public static string EditTask_Section_Title   => Get("EditTask_Section_Title");
    public static string EditTask_Section_DueDate => Get("EditTask_Section_DueDate");
    public static string EditTask_TitlePlaceholder => Get("EditTask_TitlePlaceholder");
    public static string EditTask_Save            => Get("EditTask_Save");
    public static string EditTask_Cancel          => Get("EditTask_Cancel");
    public static string EditTask_MarkImportant   => Get("EditTask_MarkImportant");
    public static string EditTask_ClearDate       => Get("EditTask_ClearDate");
    public static string EditTask_DatePlaceholder => Get("EditTask_DatePlaceholder");

    // ── Date presets ─────────────────────────────────────────
    public static string DatePreset_Today    => Get("DatePreset_Today");
    public static string DatePreset_Tomorrow => Get("DatePreset_Tomorrow");
    public static string DatePreset_Weekend  => Get("DatePreset_Weekend");
    public static string DatePreset_NextWeek => Get("DatePreset_NextWeek");

    // ── Quick Add ────────────────────────────────────────────
    public static string QuickAdd_Tooltip_OpenMain    => Get("QuickAdd_Tooltip_OpenMain");
    public static string QuickAdd_Placeholder         => Get("QuickAdd_Placeholder");
    public static string QuickAdd_Label_AddTo         => Get("QuickAdd_Label_AddTo");
    public static string QuickAdd_Placeholder_NoLists => Get("QuickAdd_Placeholder_NoLists");
    public static string QuickAdd_Label_DueDate       => Get("QuickAdd_Label_DueDate");
    public static string QuickAdd_Date_NoDate         => Get("QuickAdd_Date_NoDate");
    public static string QuickAdd_Date_Today          => Get("QuickAdd_Date_Today");
    public static string QuickAdd_Date_Tomorrow       => Get("QuickAdd_Date_Tomorrow");
    public static string QuickAdd_Date_Weekend        => Get("QuickAdd_Date_Weekend");
    public static string QuickAdd_Date_NextWeek       => Get("QuickAdd_Date_NextWeek");
    public static string QuickAdd_Button_Add          => Get("QuickAdd_Button_Add");

    // ── Default list ─────────────────────────────────────────
    public static string List_AllTasks_Name => Get("List_AllTasks_Name");

    // ── Priority chip labels ─────────────────────────────────
    public static string Priority_None   => Get("Priority_None");
    public static string Priority_Low    => Get("Priority_Low");
    public static string Priority_Medium => Get("Priority_Medium");
    public static string Priority_High   => Get("Priority_High");

    // ── Tips ─────────────────────────────────────────────────
    public static string Tip_SampleTaskTitle => Get("Tip_SampleTaskTitle");

    // ── Sync ─────────────────────────────────────────────────
    public static string Sync_Error_NotReady     => Get("Sync_Error_NotReady");
    public static string Sync_Error_SignInFailed => Get("Sync_Error_SignInFailed");
    public static string Sync_Info_ConfirmEmail  => Get("Sync_Info_ConfirmEmail");
    public static string Sync_Error_NotSignedIn  => Get("Sync_Error_NotSignedIn");
    public static string Sync_Error_NoUserId     => Get("Sync_Error_NoUserId");
    public static string Sync_NeverSynced        => Get("Sync_NeverSynced");
    public static string Sync_JustNow            => Get("Sync_JustNow");
    public static string Sync_MinAgo(int minutes) => string.Format(Get("Sync_MinAgo"), minutes);
    public static string Sync_HrAgo(int hours)    => string.Format(Get("Sync_HrAgo"), hours);
    public static string Sync_Error_NoPassphrase       => Get("Sync_Error_NoPassphrase");
    public static string Sync_Error_WrongPassphrase    => Get("Sync_Error_WrongPassphrase");
    public static string Sync_Error_PassphraseTooShort => Get("Sync_Error_PassphraseTooShort");
    public static string Sync_Error_MfaRequired        => Get("Sync_Error_MfaRequired");
    public static string Sync_Error_NoMfaFactor        => Get("Sync_Error_NoMfaFactor");
    public static string Sync_Error_BadRecoveryCode    => Get("Sync_Error_BadRecoveryCode");
    public static string Sync_Info_RecoveryUsed        => Get("Sync_Info_RecoveryUsed");

    // ── Settings ─────────────────────────────────────────────
    public static string Settings_NoFileSelected => Get("Settings_NoFileSelected");
}
