package dev.hatch.android

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.ArrowDropDown
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.AssistChip
import androidx.compose.material3.AssistChipDefaults
import androidx.compose.material3.Button
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconToggleButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle

// Indexed by the wire value of Recurrence and Priority (§4).
private val RecurrenceLabels = listOf("Never", "Daily", "Weekdays", "Weekly", "Monthly")

private val PriorityLabels = listOf("None", "Low", "Med", "High")

// Index 0 blank: printing "no priority" on every task would be noise.
internal val PriorityMetaLabels = listOf("", "Low priority", "Medium priority", "High priority")

// Same normalization Windows uses, so a date set here does not show as the previous day.
fun dueDateIsoOf(date: LocalDate): String =
    date.atStartOfDay().atOffset(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME)

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
    // Half-height first, expandable by drag. Opening straight to full screen hid the list you
    // came from and made every tap on a row feel like leaving the app; at half you can read
    // the title, due date and notes — the fields most edits touch — with the list still
    // behind it, and pull up only when you need the rest.
    val sheetState = rememberModalBottomSheetState()
    val scope = rememberCoroutineScope()

    // Working copy, committed on dismiss: per-keystroke saves would push an UpdatedAt each
    // time a character is typed.
    var draft by remember(task.id) { mutableStateOf(task) }
    var showDatePicker by remember { mutableStateOf(false) }
    var newTag by remember(task.id) { mutableStateOf("") }

    val commit = { if (draft != task) onSave(draft) }

    // hide() before the composable leaves: ModalBottomSheet only animates a dismissal it
    // performs itself, so removing it outright made the sheet vanish in one frame.
    val closeSmoothly = {
        scope.launch {
            sheetState.hide()
            commit()
            onDismiss()
        }
        Unit
    }

    // A field taking focus at half height would sit under the keyboard.
    val expandOnFocus = Modifier.onFocusChanged {
        if (it.isFocused) scope.launch { sheetState.expand() }
    }

    ModalBottomSheet(
        // The swipe-down and scrim paths are already animated by the sheet itself.
        onDismissRequest = { commit(); onDismiss() },
        sheetState = sheetState,
    ) {
        Column(
            Modifier
                .widthIn(max = ContentMaxWidth)
                .align(Alignment.CenterHorizontally)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp)
                .navigationBarsPadding(),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Task", style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.weight(1f))
                // A toggle button, not an IconButton whose tint carries the state: the
                // filled container reads as on/off at a glance and announces itself as
                // toggled. StarBorder would need material-icons-extended.
                FilledIconToggleButton(
                    checked = draft.isStarred,
                    onCheckedChange = { draft = draft.copy(isStarred = it) },
                ) {
                    Icon(
                        Icons.Rounded.Star,
                        contentDescription = if (draft.isStarred) "Remove from Important" else "Mark as Important",
                    )
                }
                IconButton(onClick = closeSmoothly) {
                    Icon(Icons.Rounded.Close, contentDescription = "Close")
                }
            }

            Spacer(Modifier.height(8.dp))
            OutlinedTextField(
                value = draft.title,
                onValueChange = { draft = draft.copy(title = it) },
                label = { Text("Title") },
                shape = MaterialTheme.shapes.large,
                keyboardOptions = KeyboardOptions(
                    capitalization = KeyboardCapitalization.Sentences,
                    imeAction = ImeAction.Next,
                ),
                modifier = Modifier.fillMaxWidth().then(expandOnFocus),
            )

            Spacer(Modifier.height(12.dp))
            OutlinedTextField(
                value = draft.notes.orEmpty(),
                // null, not "": empty means "no notes" on the wire.
                onValueChange = { draft = draft.copy(notes = it.ifBlank { null }) },
                label = { Text("Notes") },
                shape = MaterialTheme.shapes.large,
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences),
                modifier = Modifier.fillMaxWidth().heightIn(min = 96.dp).then(expandOnFocus),
            )

            Spacer(Modifier.height(16.dp))
            SheetGroup("Due") {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    AssistChip(
                        onClick = { showDatePicker = true },
                        label = { Text(dueDateLabel(draft.dueDate) ?: "Set a date") },
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
                    DuePreset("Today") { draft = draft.copy(dueDate = dueDateIsoOf(LocalDate.now())) }
                    DuePreset("Tomorrow") { draft = draft.copy(dueDate = dueDateIsoOf(LocalDate.now().plusDays(1))) }
                }
            }

            Spacer(Modifier.height(8.dp))
            SheetGroup("My Day") {
            FilterChip(
                selected = draft.isInMyDay,
                onClick = {
                    val on = !draft.isInMyDay
                    // Membership and date are one rule — see CompanionViewModel.setMyDay.
                    draft = draft.copy(
                        isInMyDay = on,
                        myDayDate = if (on) LocalDate.now().toString() else null,
                    )
                },
                label = { Text(if (draft.isInMyDay) "In My Day" else "Add to My Day") },
            )
            }

            Spacer(Modifier.height(8.dp))
            SheetGroup("Priority") {
                SingleChoiceSegmentedButtonRow(Modifier.fillMaxWidth()) {
                    PriorityLabels.forEachIndexed { value, label ->
                        SegmentedButton(
                            selected = draft.priority == value,
                            onClick = { draft = draft.copy(priority = value) },
                            shape = SegmentedButtonDefaults.itemShape(value, PriorityLabels.size),
                        ) { Text(label) }
                    }
                }
            }

            Spacer(Modifier.height(8.dp))
            SheetGroup("Repeat") {
                WirePicker(
                    selected = draft.recurrence,
                    options = RecurrenceLabels,
                    onSelect = { draft = draft.copy(recurrence = it) },
                )
            }

            Spacer(Modifier.height(8.dp))
            SheetGroup("List") {
                val listNames = remember(lists) {
                    listOf("Tasks") + lists.map { it.name }
                }
                val listIds = remember(lists) {
                    listOf(DEFAULT_LIST_ID) + lists.map { it.id }
                }
                // -1 for a list this phone has not pulled yet: fall back to the default
                // rather than showing a blank chip.
                WirePicker(
                    selected = listIds.indexOf(draft.listId).coerceAtLeast(0),
                    options = listNames,
                    onSelect = { draft = draft.copy(listId = listIds[it]) },
                )
            }

            Spacer(Modifier.height(8.dp))
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
                        shape = MaterialTheme.shapes.large,
                        modifier = Modifier.weight(1f).then(expandOnFocus),
                    )
                    TextButton(
                        onClick = {
                            val trimmed = newTag.trim()
                            // Case-insensitive, matching MainViewModel.AddTagToTask.
                            if (trimmed.isNotEmpty() && draft.tags.none { it.equals(trimmed, ignoreCase = true) }) {
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

            Spacer(Modifier.height(16.dp))
            TextButton(
                // Down and out, then delete: the row disappearing behind a sheet that is
                // still on screen reads as the wrong thing having been deleted.
                onClick = { scope.launch { sheetState.hide(); onDelete(task); onDismiss() } },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Icon(
                    Icons.Rounded.Delete,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.error,
                )
                Spacer(Modifier.width(8.dp))
                Text("Delete task", color = MaterialTheme.colorScheme.error)
            }
            Spacer(Modifier.height(16.dp))
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

// One rounded container per field, rather than labels floating on the sheet background —
// the grouping Material uses to make a long settings-style form scannable.
@Composable
private fun SheetGroup(label: String, content: @Composable ColumnScope.() -> Unit) {
    Surface(
        color = MaterialTheme.colorScheme.surfaceContainerLow,
        shape = MaterialTheme.shapes.large,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(Modifier.padding(16.dp)) {
            Text(
                label,
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(bottom = 8.dp),
            )
            content()
        }
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
