package dev.hatch.android

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowDropDown
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.AssistChip
import androidx.compose.material3.AssistChipDefaults
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconToggleButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.InputChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.paneTitle
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import dev.hatch.sync.DEFAULT_LIST_ID
import dev.hatch.sync.DueDatePresets
import dev.hatch.sync.RecurrenceHelper
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
import kotlinx.coroutines.launch
import kotlinx.datetime.toKotlinLocalDate
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import kotlinx.datetime.LocalDate as CalendarDate

// Indexed by the wire value of Recurrence and Priority (§4).
private val RecurrenceLabels = listOf("Never", "Daily", "Weekdays", "Weekly", "Monthly")

private val PriorityLabels = listOf("None", "Low", "Med", "High")

// Index 0 blank: printing "no priority" on every task would be noise.
internal val PriorityMetaLabels = listOf("", "Low priority", "Medium priority", "High priority")

// Same normalization Windows uses, so a date set here does not show as the previous day.
// Through RecurrenceHelper.wireForm so every due date this app writes has one spelling:
// ISO_OFFSET_DATE_TIME renders a zero offset as "Z", the recurrence path already wrote
// "+00:00", and the Planned list sorts on this text.
fun dueDateIsoOf(date: LocalDate): String = RecurrenceHelper.wireForm(date.toKotlinLocalDate())

// The four presets from windows/Helpers/DueDatePresets.cs, resolved against today.
private fun presetIso(pick: (CalendarDate) -> CalendarDate): String =
    RecurrenceHelper.wireForm(pick(LocalDate.now().toKotlinLocalDate()))

fun localDateOf(iso: String?): LocalDate? =
    iso?.let { runCatching { OffsetDateTime.parse(it).toLocalDate() }.getOrNull() }

fun dueDateLabel(iso: String?): String? {
    val date = localDateOf(iso) ?: return null
    val today = LocalDate.now()
    return when (date) {
        today -> "Due today"
        today.plusDays(1) -> "Due tomorrow"
        else -> {
            val overdueBy = today.toEpochDay() - date.toEpochDay()
            if (overdueBy > 0) "Overdue ($overdueBy" + "d)"
            else date.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM))
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskDetailSheet(
    task: TodoItem,
    lists: List<TaskList>,
    onSave: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    onDismiss: () -> Unit,
) {
    // Stock defaults throughout, no arguments: opens partially expanded so the list stays
    // visible behind it, drags up to full, drags down to dismissed. Gestures, drag handle,
    // back and predictive back, scrim tap, window insets and max width are all the
    // component's own — nothing here overrides them.
    val sheetState = rememberModalBottomSheetState()
    val scope = rememberCoroutineScope()

    // Working copy, committed on dismiss: per-keystroke saves would push an UpdatedAt each
    // time a character is typed.
    var draft by remember(task.id) { mutableStateOf(task) }
    var showDatePicker by remember { mutableStateOf(false) }
    var newTag by remember(task.id) { mutableStateOf("") }

    val commit = { if (draft != task) onSave(draft) }

    // task.title, not draft.title: names the sheet as a distinct region for a screen reader,
    // which matters more now that closing it is a gesture rather than a labelled button. Using
    // the live draft would re-announce on every keystroke.
    val paneTitleText = if (task.title.isBlank()) "Task details" else "Task details: ${task.title}"

    ModalBottomSheet(
        // Fires after the sheet has animated itself out, for every dismissal path the
        // component owns: drag down, scrim tap, back, predictive back.
        onDismissRequest = { commit(); onDismiss() },
        modifier = Modifier.semantics { paneTitle = paneTitleText },
        sheetState = sheetState,
    ) {
        // LazyColumn, not Column + verticalScroll: with content this tall, the plain
        // scrollable measured its max offset before the sheet's own height settled at
        // Expanded, so the tail of the form (List, Tags, Delete) was past what the scroll
        // state believed was the bottom and could never be dragged into view. LazyColumn
        // tracks each item's own height independently as they're placed, so the max scroll
        // extent is never stale. Its nested scrolling is also what hands a downward drag at
        // the top of the form back to the sheet, which is how the sheet closes.
        //
        // Horizontal padding only: the sheet already caps itself at 640.dp
        // (BottomSheetDefaults.SheetMaxWidth) and already applies safeDrawing top/bottom via
        // its default contentWindowInsets, so a width cap or navigationBarsPadding in here
        // would only duplicate what the component does.
        LazyColumn(modifier = Modifier.padding(horizontal = 20.dp)) {
            item {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "Task",
                    style = MaterialTheme.typography.titleMedium,
                    modifier = Modifier.semantics { heading() },
                )
                Spacer(Modifier.weight(1f))
                // A toggle button, not an IconButton whose tint carries the state: the
                // filled container reads as on/off at a glance and announces itself as
                // toggled. StarBorder would need material-icons-extended.
                //
                // Gold when on, matching the star on a task row and the Important list.
                FilledIconToggleButton(
                    checked = draft.isStarred,
                    onCheckedChange = { draft = draft.copy(isStarred = it) },
                    colors = IconButtonDefaults.filledIconToggleButtonColors(
                        checkedContainerColor = MaterialTheme.colorScheme.tertiaryContainer,
                        checkedContentColor = MaterialTheme.colorScheme.onTertiaryContainer,
                    ),
                ) {
                    Icon(
                        Icons.Rounded.Star,
                        contentDescription = if (draft.isStarred) "Remove from Important" else "Mark as Important",
                    )
                }
            }
            }

            item {
            Spacer(Modifier.height(8.dp))
            // The title is the subject of the sheet, so it is set in the sheet's own heading
            // size rather than in body text inside a labelled box like the rest of the form.
            OutlinedTextField(
                value = draft.title,
                onValueChange = { draft = draft.copy(title = it) },
                label = { Text("Title") },
                textStyle = MaterialTheme.typography.titleMedium,
                shape = RoundedCornerShape(CardCorner),
                keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Sentences,
                    imeAction = ImeAction.Next,
                ),
                modifier = Modifier.fillMaxWidth(),
            )
            }

            item {
            Spacer(Modifier.height(12.dp))
            OutlinedTextField(
                value = draft.notes.orEmpty(),
                // null, not "": empty means "no notes" on the wire.
                onValueChange = { draft = draft.copy(notes = it.ifBlank { null }) },
                label = { Text("Notes") },
                shape = RoundedCornerShape(CardCorner),
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
                modifier = Modifier.fillMaxWidth().heightIn(min = 96.dp),
            )
            }

            item {
            Spacer(Modifier.height(16.dp))
            // One card for every field group below, rather than six separately floating
            // ones — matches how Settings and Lists already group related rows in this app.
            Surface(
                color = MaterialTheme.colorScheme.surfaceContainerLow,
                shape = RoundedCornerShape(CardCorner),
                modifier = Modifier.fillMaxWidth(),
            ) {
                val divider = @Composable {
                    HorizontalDivider(
                        color = MaterialTheme.colorScheme.outlineVariant,
                        modifier = Modifier.padding(horizontal = 16.dp),
                    )
                }
                Column {
                    SheetGroup("Due") {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            val due = dueChipFor(draft.dueDate)
                            AssistChip(
                                onClick = { showDatePicker = true },
                                label = { Text(due?.label ?: "Set a date") },
                                // Takes the same tone the row's chip does, so an overdue
                                // task looks overdue here too rather than turning neutral
                                // the moment it is opened.
                                colors = if (due == null) AssistChipDefaults.assistChipColors() else
                                    AssistChipDefaults.assistChipColors(
                                        containerColor = dueContainerColor(due.tone),
                                        labelColor = dueContentColor(due.tone),
                                    ),
                                border = if (due == null) AssistChipDefaults.assistChipBorder(true) else null,
                            )
                            if (draft.dueDate != null) {
                                Spacer(Modifier.width(8.dp))
                                TextButton(onClick = { draft = draft.copy(dueDate = null) }) { Text("Clear") }
                            }
                        }

                        Spacer(Modifier.height(8.dp))
                        FlowRow(
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            DuePreset("Today") {
                                draft = draft.copy(dueDate = presetIso(DueDatePresets::today))
                            }
                            DuePreset("Tomorrow") {
                                draft = draft.copy(dueDate = presetIso(DueDatePresets::tomorrow))
                            }
                            DuePreset("This weekend") {
                                draft = draft.copy(dueDate = presetIso(DueDatePresets::thisWeekend))
                            }
                            DuePreset("Next week") {
                                draft = draft.copy(dueDate = presetIso(DueDatePresets::nextWeek))
                            }
                        }
                    }
                    divider()
                    SheetGroup("My Day") {
                        FilterChip(
                            selected = draft.isInMyDay,
                            onClick = {
                                val on = !draft.isInMyDay
                                // Membership and date are one rule — see
                                // CompanionViewModel.setMyDay.
                                draft = draft.copy(
                                    isInMyDay = on,
                                    myDayDate = if (on) LocalDate.now().toString() else null,
                                )
                            },
                            label = { Text(if (draft.isInMyDay) "In My Day" else "Add to My Day") },
                        )
                    }
                    divider()
                    SheetGroup("Priority") {
                        SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
                            PriorityLabels.forEachIndexed { value, label ->
                                // Same red/gold/blue the row's checkbox already carries a
                                // priority in — picking High here should look like picking
                                // High there.
                                SegmentedButton(
                                    selected = draft.priority == value,
                                    onClick = { draft = draft.copy(priority = value) },
                                    shape = SegmentedButtonDefaults.itemShape(value, PriorityLabels.size),
                                    colors = SegmentedButtonDefaults.colors(
                                        // Unspecified falls back to the default spec
                                        // colour on its own — None has no tint to give it.
                                        activeContainerColor = priorityContainerColor(value) ?: Color.Unspecified,
                                        activeContentColor = priorityOnContainerColor(value) ?: Color.Unspecified,
                                    ),
                                ) { Text(label) }
                            }
                        }
                    }
                    divider()
                    SheetGroup("Repeat") {
                        WirePicker(
                            selected = draft.recurrence,
                            options = RecurrenceLabels,
                            onSelect = { draft = draft.copy(recurrence = it) },
                        )
                    }
                    divider()
                    SheetGroup("List") {
                        val listNames = remember(lists) {
                            listOf("Tasks") + lists.map { it.name }
                        }
                        val listIds = remember(lists) {
                            listOf(DEFAULT_LIST_ID) + lists.map { it.id }
                        }
                        // -1 for a list this phone has not pulled yet: fall back to the
                        // default rather than showing a blank chip.
                        WirePicker(
                            selected = listIds.indexOf(draft.listId).coerceAtLeast(0),
                            options = listNames,
                            onSelect = { draft = draft.copy(listId = listIds[it]) },
                        )
                    }
                    divider()
                    SheetGroup("Tags") {
                        Row(
                            Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            OutlinedTextField(
                                value = newTag,
                                onValueChange = { newTag = it },
                                label = { Text("Add a tag") },
                                singleLine = true,
                                shape = RoundedCornerShape(CardCorner),
                                modifier = Modifier.weight(1f),
                            )
                            TextButton(
                                onClick = {
                                    val trimmed = newTag.trim()
                                    // Case-insensitive, matching MainViewModel.AddTagToTask.
                                    if (trimmed.isNotEmpty() &&
                                        draft.tags.none { it.equals(trimmed, ignoreCase = true) }
                                    ) {
                                        draft = draft.copy(tags = draft.tags + trimmed)
                                    }
                                    newTag = ""
                                },
                                enabled = newTag.isNotBlank(),
                            ) { Text("Add") }
                        }
                        if (draft.tags.isNotEmpty()) {
                            Spacer(Modifier.height(8.dp))
                            // Wraps: a Row silently clipped every tag past the third.
                            FlowRow(
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                                verticalArrangement = Arrangement.spacedBy(4.dp),
                            ) {
                                draft.tags.forEach { tag ->
                                    InputChip(
                                        selected = false,
                                        onClick = { draft = draft.copy(tags = draft.tags - tag) },
                                        label = { Text("#$tag") },
                                        trailingIcon = {
                                            Icon(Icons.Rounded.Close, contentDescription = "Remove tag $tag")
                                        },
                                    )
                                }
                            }
                        }
                    }
                }
            }
            }

            item {
            Spacer(Modifier.height(20.dp))
            // Tonal rather than a bare text button: this is the one irreversible control on
            // the sheet, and it was drawn exactly like "Clear" and "Add" above it.
            FilledTonalButton(
                // Down and out, then delete: the row disappearing behind a sheet that is
                // still on screen reads as the wrong thing having been deleted.
                onClick = { scope.launch { sheetState.hide(); onDelete(task); onDismiss() } },
                colors = ButtonDefaults.filledTonalButtonColors(
                    containerColor = MaterialTheme.colorScheme.errorContainer,
                    contentColor = MaterialTheme.colorScheme.onErrorContainer,
                ),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Icon(Icons.Rounded.Delete, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(8.dp))
                Text("Delete task")
            }
            Spacer(Modifier.height(16.dp))
            }
        }
    }

    if (showDatePicker) {
        val initial = localDateOf(draft.dueDate) ?: LocalDate.now()
        val pickerState = rememberDatePickerState(
            initialSelectedDateMillis = initial.atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli(),
        )
        DatePickerDialog(
            onDismissRequest = { showDatePicker = false },
            confirmButton = {
                Button(onClick = {
                    pickerState.selectedDateMillis?.let { millis ->
                        // The picker reports UTC midnight, so read it back in UTC.
                        val picked = Instant.ofEpochMilli(millis).atZone(ZoneOffset.UTC).toLocalDate()
                        draft = draft.copy(dueDate = dueDateIsoOf(picked))
                    }
                    showDatePicker = false
                }) { Text("Set") }
            },
            dismissButton = {
                TextButton(onClick = { showDatePicker = false }) { Text("Cancel") }
            },
        ) { DatePicker(state = pickerState) }
    }
}

// One section within the shared field-group card below, not its own card — six separately
// floating cards (Due, My Day, Priority, Repeat, List, Tags) read as more fragmented than
// this form needs, and don't match how Settings and Lists already group related rows in
// this app into one continuous card with dividers between sections.
@Composable
private fun SheetGroup(label: String, content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth().padding(16.dp)) {
        Text(
            label,
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(bottom = 8.dp),
        )
        content()
    }
}

@Composable
private fun DuePreset(label: String, onClick: () -> Unit) {
    AssistChip(onClick = onClick, label = { Text(label) })
}

// Selection is an index, which is what the int-valued wire fields need.
@Composable
private fun WirePicker(selected: Int, options: List<String>, onSelect: (Int) -> Unit) {
    var open by remember { mutableStateOf(false) }

    Column {
        AssistChip(
            onClick = { open = true },
            label = { Text(options.getOrElse(selected) { options.first() }) },
            trailingIcon = {
                Icon(
                    Icons.Rounded.ArrowDropDown,
                    contentDescription = null,
                    modifier = Modifier.size(AssistChipDefaults.IconSize),
                )
            },
        )
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            options.forEachIndexed { index, label ->
                DropdownMenuItem(
                    text = { Text(label) },
                    // Reserved space either way, so labels do not shift with the selection.
                    leadingIcon = {
                        if (index == selected) Icon(Icons.Rounded.Check, contentDescription = "Selected")
                        else Spacer(Modifier.size(24.dp))
                    },
                    onClick = { onSelect(index); open = false },
                )
            }
        }
    }
}
