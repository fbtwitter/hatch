package dev.hatch.android

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem

// Search is its own destination now rather than a mode the task list could be in. The two
// were previously told apart by a sentinel space fed into the query itself, which showed up
// as a real leading space in the field (fixed in 48ea91c); a route cannot be confused with
// its own contents.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SearchScreen(
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    snackbar: SnackbarHostState,
    onBack: () -> Unit,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
) {
    // Local to the route: leaving search drops the query, so the next visit starts fresh
    // rather than reopening on whatever was typed last time.
    var query by rememberSaveable { mutableStateOf("") }
    val results = remember(tasks, query) { searchResults(tasks, query) }
    val listNames = remember(lists) { lists.associate { it.id to it.name } }
    val focusRequester = remember { FocusRequester() }

    // Arriving here used to leave the field unfocused, so the first tap on the search icon
    // did nothing visible and the second one — on the field — is what started the search.
    LaunchedEffect(Unit) { focusRequester.requestFocus() }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            // A pill rather than a title slot with a TextField in it — the shape Android
            // users read as "search" before a single word is typed, and the same one Gmail,
            // Files and Play use. Back and clear live inside the pill, so the whole control
            // is one object instead of a bar with three unrelated things on it.
            Column(Modifier.windowInsetsPadding(WindowInsets.statusBars)) {
                Surface(
                    color = MaterialTheme.colorScheme.surfaceContainerHigh,
                    shape = CircleShape,
                    modifier = Modifier
                        .padding(horizontal = ScreenPadding, vertical = 8.dp)
                        .fillMaxWidth()
                        .height(52.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        IconButton(onClick = onBack) {
                            Icon(
                                Icons.AutoMirrored.Rounded.ArrowBack,
                                contentDescription = "Leave search",
                            )
                        }
                        BasicTextField(
                            value = query,
                            // trimStart for the same reason as the task composer: the clear
                            // button and the results are gated on isNotBlank, so a leading
                            // space produced a field with no placeholder, no visible text and
                            // no way to clear it.
                            onValueChange = { query = it.trimStart() },
                            singleLine = true,
                            textStyle = MaterialTheme.typography.bodyLarge.copy(
                                color = MaterialTheme.colorScheme.onSurface,
                            ),
                            cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
                            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                            modifier = Modifier
                                .weight(1f)
                                .focusRequester(focusRequester),
                            decorationBox = { field ->
                                Box(contentAlignment = Alignment.CenterStart) {
                                    if (query.isBlank()) {
                                        Text(
                                            "Search all tasks",
                                            style = MaterialTheme.typography.bodyLarge,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                        )
                                    }
                                    field()
                                }
                            },
                        )
                        if (query.isNotBlank()) {
                            IconButton(onClick = { query = "" }) {
                                Icon(Icons.Rounded.Close, contentDescription = "Clear search")
                            }
                        } else {
                            Spacer(Modifier.width(12.dp))
                        }
                    }
                }
            }
        },
    ) { padding ->
        if (results.isEmpty()) {
            SearchPlaceholder(
                typedAnything = query.isNotBlank(),
                modifier = Modifier.padding(padding),
            )
            return@Scaffold
        }

        Box(Modifier.fillMaxSize()) {
            LazyColumn(
                modifier = Modifier
                    .fillMaxHeight()
                    .widthIn(max = ContentMaxWidth)
                    .align(Alignment.TopCenter),
                contentPadding = padding,
                verticalArrangement = Arrangement.spacedBy(GroupGap),
            ) {
                itemsIndexed(results, key = { _, t -> t.id }, contentType = { _, _ -> "task" }) { i, task ->
                    TaskRow(
                        task,
                        listNames,
                        groupedShape(i, results.size),
                        onToggle,
                        onOpen,
                        onDelete,
                        // Nothing to filter here: results already span every list, so a tag
                        // filter would be a second, hidden query on top of the typed one.
                        onTagClick = null,
                        modifier = Modifier.animateItem(),
                    )
                }
            }
        }
    }
}

@Composable
private fun SearchPlaceholder(typedAnything: Boolean, modifier: Modifier = Modifier) {
    Column(
        modifier.fillMaxSize().padding(32.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(
            Icons.Rounded.Search,
            contentDescription = null,
            modifier = Modifier.size(56.dp),
            tint = MaterialTheme.colorScheme.primary.copy(alpha = 0.35f),
        )
        Spacer(Modifier.height(16.dp))
        Text(
            if (typedAnything) "No matches" else "Search your tasks",
            style = MaterialTheme.typography.titleMedium,
        )
        Spacer(Modifier.height(6.dp))
        Text(
            if (typedAnything) "Nothing in any list matches that — including completed tasks."
            else "Titles, notes and tags, across every list and both completion states.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}
