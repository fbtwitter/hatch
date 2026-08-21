package dev.hatch.android

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.calculateEndPadding
import androidx.compose.foundation.layout.calculateStartPadding
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.CheckCircle
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Delete
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowUp
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.minimumInteractiveComponentSize
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.SwipeToDismissBox
import androidx.compose.material3.SwipeToDismissBoxValue
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.pulltorefresh.PullToRefreshDefaults
import androidx.compose.material3.pulltorefresh.pullToRefresh
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.material3.rememberSwipeToDismissBoxState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.LocalViewConfiguration
import androidx.compose.ui.platform.ViewConfiguration
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import dev.hatch.sync.TaskList
import dev.hatch.sync.TodoItem
import kotlinx.coroutines.flow.drop
import kotlinx.coroutines.flow.filter
import java.time.LocalDate

// My Day: a whole screen with a collapsing title and a Suggested section. Every other list is
// a folder page inside the Lists tab, which shares this file's body but brings its own chrome.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TaskListScreen(
    nav: String,
    tasks: List<TodoItem>,
    lists: List<TaskList>,
    loaded: Boolean,
    refreshEnabled: Boolean,
    refreshing: Boolean,
    snackbar: SnackbarHostState,
    onOpenSearch: () -> Unit,
    onRefresh: () -> Unit,
    onAdd: (String) -> String?,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    onAddToMyDay: (TodoItem) -> Unit,
    // False only on the My Day route — the outer SwipePeekHost (see MainActivity.kt) owns
    // horizontal swipe there instead, so rows must not also react to it.
    swipeToDeleteEnabled: Boolean = true,
) {
    // Local to the route, which is what makes it clear itself on the way out — the Windows
    // original has to null it by hand in the ActiveNavItem setter.
    var tagFilter by rememberSaveable { mutableStateOf<String?>(null) }
    // remember, not a bare lambda literal: this screen recomposes on every scroll-driven
    // composerCollapsed/atTop flip below, and an un-hoisted lambda here was a fresh instance
    // each time — flowing into every visible TaskRow's onTagClick parameter and making
    // Compose treat every row as changed mid-scroll, the exact moment that matters most.
    val onTagClick: (String) -> Unit = remember { { tag -> tagFilter = tag } }

    val model = rememberTaskListModel(nav, tasks, tagFilter)
    val listNames = remember(lists) { lists.associate { it.id to it.name } }

    val listState = rememberLazyListState()
    var pendingScrollId by remember { mutableStateOf<String?>(null) }

    // The add field is at the bottom, so a task added while scrolled down otherwise lands
    // off-screen and reads as nothing having happened. By key rather than index 0 because
    // neither Important nor Planned puts the newest task first.
    LaunchedEffect(pendingScrollId, model.rows) {
        val id = pendingScrollId ?: return@LaunchedEffect
        val index = model.rows.indexOfFirst { it.key == id }
        if (index >= 0) listState.animateScrollToItem(index)
        pendingScrollId = null
    }

    val composerCollapsed = rememberComposerCollapsed(listState)
    val onSubmit: (String) -> Unit = remember(onAdd) { { title -> pendingScrollId = onAdd(title) } }

    Scaffold(
        topBar = {
            // Plain bar, matching Sync and Lists — not the collapsing MediumTopAppBar this
            // screen used to have. That bar's hero treatment (large title giving back space
            // on scroll) meant its own top row held only the search icon at rest, floating
            // with no title beside it until the bar collapsed — inconsistent with every
            // other screen in the app, which all keep title and search in the same row.
            TopAppBar(
                title = {
                    Column {
                        Text(navTitle(nav, lists))
                        // The filter replaces the count rather than adding a third line:
                        // while one is on, the count is a count of the filter, not the list.
                        if (tagFilter != null) {
                            TagFilterPill(tagFilter!!) { tagFilter = null }
                        } else {
                            Text(
                                model.countLabel,
                                style = MaterialTheme.typography.labelMedium,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                },
                actions = {
                    IconButton(onClick = onOpenSearch) {
                        Icon(Icons.Rounded.Search, contentDescription = "Search")
                    }
                },
            )
        },
        bottomBar = {
            ComposerBar(
                collapsed = composerCollapsed,
                snackbar = snackbar,
                onSubmit = onSubmit,
            )
        },
    ) { padding ->
        // Nothing at all until the disk read lands: showing "Nothing yet" for one frame and
        // then replacing it with the real list reads as a bug.
        if (!loaded) return@Scaffold

        TaskListBody(
            nav = nav,
            model = model,
            anyTasks = tasks.isNotEmpty(),
            listNames = listNames,
            listState = listState,
            tagFilter = tagFilter,
            padding = padding,
            refreshEnabled = refreshEnabled,
            refreshing = refreshing,
            // No collapsing bar to fight over the same downward drag here, so a pull at
            // the top is always a pull — matches the Lists tab's own folder pages.
            pullEnabled = true,
            onRefresh = onRefresh,
            onToggle = onToggle,
            onOpen = onOpen,
            onDelete = onDelete,
            onTagClick = onTagClick,
            onAddToMyDay = onAddToMyDay,
            swipeToDeleteEnabled = swipeToDeleteEnabled,
        )
    }
}

// Everything the list needs to draw, computed once. Shared by My Day, which owns a whole
// screen, and by each folder page inside the Lists tab, which owns only a body.
internal class TaskListModel(
    val rows: List<ListRow>,
    val countLabel: String,
)

@Composable
internal fun rememberTaskListModel(
    nav: String,
    tasks: List<TodoItem>,
    tagFilter: String?,
): TaskListModel {
    // Derived, not recomputed: without remember these run whenever anything on the screen
    // changes, and none of them depend on most of it.
    val visible = remember(tasks, nav, tagFilter) {
        withTagFilter(tasksForNav(tasks, nav), tagFilter)
    }
    // LocalDate.now() inside the remember, not hoisted: an app left open past midnight
    // regroups on the next edit rather than showing yesterday's buckets forever.
    val sections = remember(visible, nav) { sectionsFor(nav, visible, LocalDate.now()) }
    // Mirrors MainViewModel.SuggestionsVisible — My Day only.
    val suggested = remember(tasks, nav) {
        if (nav == NAV_MY_DAY) suggestions(tasks) else emptyList()
    }
    // Once reviewed, the whole tray collapses to just its header — this only ever exists on
    // the My Day route, so one saveable flag per screen instance is enough; no key on nav.
    var suggestionsExpanded by rememberSaveable { mutableStateOf(true) }
    val rows = remember(sections, suggested, suggestionsExpanded) {
        buildRows(sections, suggested, suggestionsExpanded) { suggestionsExpanded = !suggestionsExpanded }
    }

    // Open-only, matching the folder tabs' own counts.
    val countLabel = remember(visible) {
        val open = visible.count { !it.isCompleted }
        val done = visible.size - open
        when {
            open == 0 && done == 0 -> "Nothing here"
            open == 0 -> "All done"
            else -> "$open open" + if (done == 0) "" else " · $done done"
        }
    }

    return remember(rows, countLabel) { TaskListModel(rows, countLabel) }
}

// The list itself, with no chrome of its own. My Day wraps this in a screen with its own plain
// top bar; a folder page is handed the Lists tab's padding and draws nothing else.
@Composable
internal fun TaskListBody(
    nav: String,
    model: TaskListModel,
    anyTasks: Boolean,
    listNames: Map<String, String>,
    listState: LazyListState,
    tagFilter: String?,
    padding: PaddingValues,
    refreshEnabled: Boolean,
    refreshing: Boolean,
    pullEnabled: Boolean,
    onRefresh: () -> Unit,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    onTagClick: (String) -> Unit,
    onAddToMyDay: (TodoItem) -> Unit,
    // False only on My Day — see TaskRow for why folder pages keep swipe-to-delete as-is.
    swipeToDeleteEnabled: Boolean = true,
) {
    // Completing or deleting the last task used to swap the list for the empty state
    // between two frames. A fade only — nothing has moved, so nothing should slide.
    val body: @Composable () -> Unit = {
        AnimatedContent(
            targetState = model.rows.isEmpty(),
            transitionSpec = { contentFade() },
            label = "body",
        ) { empty ->
            if (empty) {
                EmptyState(
                    nav = nav,
                    tagFilter = tagFilter,
                    anyTasks = anyTasks,
                    modifier = Modifier.padding(padding),
                )
            } else {
                Box(Modifier.fillMaxSize()) {
                    // A header row already carries its own top padding (SectionHeaderRow),
                    // but the open section has no header — its title is null — so a real
                    // task can be the very first row, and `padding` alone is exactly the
                    // bar's height with nothing left over. Added here rather than baked into
                    // TaskRow itself, which would double this gap for every row after the
                    // first.
                    // Keyed on the four measured values, not on the PaddingValues object.
                    // Keying on the object cached the very first measurement for the lifetime
                    // of the list, so a top bar that changes height later — the Lists tab's
                    // tag-filter row appearing above the list — never moved the content down,
                    // and the first task's title ended up drawn underneath the bar.
                    val layoutDirection = LocalLayoutDirection.current
                    val padStart = padding.calculateStartPadding(layoutDirection)
                    val padTop = padding.calculateTopPadding()
                    val padEnd = padding.calculateEndPadding(layoutDirection)
                    val padBottom = padding.calculateBottomPadding()
                    val listContentPadding = remember(padStart, padTop, padEnd, padBottom) {
                        PaddingValues(
                            start = padStart,
                            top = padTop + ScreenPadding,
                            end = padEnd,
                            bottom = padBottom,
                        )
                    }
                    LazyColumn(
                        state = listState,
                        modifier = Modifier
                            .fillMaxHeight()
                            .widthIn(max = ContentMaxWidth)
                            .align(Alignment.TopCenter),
                        // The composer's own height is already in `padding`, because it is
                        // the hosting Scaffold's bottomBar and that bar keeps a fixed height
                        // whether it is expanded or collapsed. So the end of the list always
                        // clears it, and collapsing never reflows the list. The bar does grow
                        // while an undo snackbar is showing (see ComposerBar) — `padding`
                        // tracks that too, so the list still clears it without a separate
                        // case here.
                        contentPadding = listContentPadding,
                        verticalArrangement = Arrangement.spacedBy(GroupGap),
                    ) {
                        items(
                            model.rows,
                            key = { it.key },
                            // Lets rows be reused across sections rather than rebuilt.
                            contentType = { it.contentType },
                        ) { row ->
                            when (row) {
                                is ListRow.Header -> SectionHeaderRow(row)
                                is ListRow.Task -> TaskRow(
                                    row.task,
                                    listNames,
                                    groupedShape(row.index, row.count),
                                    onToggle,
                                    onOpen,
                                    onDelete,
                                    onTagClick = onTagClick,
                                    swipeToDeleteEnabled = swipeToDeleteEnabled,
                                    modifier = Modifier.animateItem(),
                                )
                                is ListRow.Suggestion -> SuggestionRow(
                                    row.task,
                                    listNames,
                                    groupedShape(row.index, row.count),
                                    onOpen,
                                    onAddToMyDay,
                                    Modifier.animateItem(),
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    // Composed only while signed in (or mid-pull): signed out gets no gesture and no
    // spinner at all — sync is opt-in, and a spring-back pull would advertise it.
    if (refreshEnabled) {
        val pullState = rememberPullToRefreshState()
        // Hand-built from Modifier.pullToRefresh rather than PullToRefreshBox: this
        // Material3 version's PullToRefreshBox doesn't expose `enabled`, and the gate needs it.
        Box(
            Modifier.pullToRefresh(
                isRefreshing = refreshing,
                state = pullState,
                enabled = pullEnabled,
                onRefresh = onRefresh,
            ),
        ) {
            body()
            // The default slot pins the spinner to the box's own top edge, which sits behind
            // the app bar — the scaffold hands insets down as padding rather than shrinking
            // its content area.
            PullToRefreshDefaults.Indicator(
                state = pullState,
                isRefreshing = refreshing,
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .padding(top = padding.calculateTopPadding()),
            )
        }
    } else {
        body()
    }
}

// Reading downward as "get out of the way" is the convention every Android list follows.
// Tracked outside the composer because the composer cannot see the list.
//
// Hysteresis, not a direction flag. Reacting to the first pixel of travel made the composer
// flap open and shut under a thumb that was barely moving; it now has to see a deliberate run
// downward before it gets out of the way, and gives up far sooner on the way back, so it errs
// towards being available.
@Composable
internal fun rememberComposerCollapsed(listState: LazyListState): Boolean {
    var collapsed by remember(listState) { mutableStateOf(false) }
    LaunchedEffect(listState) {
        var prevIndex = listState.firstVisibleItemIndex
        var prevOffset = listState.firstVisibleItemScrollOffset
        var travel = 0
        snapshotFlow { listState.firstVisibleItemIndex to listState.firstVisibleItemScrollOffset }
            .collect { (index, offset) ->
                // An index change carries no pixel count, so it stands in as "at least a row".
                val delta =
                    if (index != prevIndex) (index - prevIndex) * ApproxRowPx
                    else offset - prevOffset
                prevIndex = index
                prevOffset = offset
                if (delta == 0) return@collect

                // A direction change restarts the run, so these thresholds measure one
                // continuous gesture rather than an afternoon of accumulated jitter.
                travel = if ((travel > 0) != (delta > 0)) delta else travel + delta
                when {
                    travel > CollapseAfterPx -> collapsed = true
                    travel < -ExpandAfterPx -> collapsed = false
                }
                // Back at the very top there is nothing left to be in the way of.
                if (index == 0 && offset == 0) collapsed = false
            }
    }
    return collapsed
}

// One flat list of what the LazyColumn will emit, built once. The scroll-to-new-task effect
// needs an item index, and deriving that from nested sections meant writing the ordering
// twice and keeping the two in step by hand.
internal sealed interface ListRow {
    val key: String
    val contentType: String

    // count/expanded/onToggle stay null for a plain section divider (Overdue, Today, ...);
    // only the Suggested header sets all three. Key excludes them so toggling never
    // recreates the row.
    data class Header(
        val text: String,
        val count: Int? = null,
        val expanded: Boolean = true,
        val onToggle: (() -> Unit)? = null,
    ) : ListRow {
        override val key get() = "header-$text"
        override val contentType get() = "header"
    }

    data class Task(val task: TodoItem, val index: Int, val count: Int) : ListRow {
        override val key get() = task.id
        override val contentType get() = "task"
    }

    // Prefixed: a suggestion and a task are different rows, and nothing stops the same task
    // from being both across a My Day toggle mid-animation.
    data class Suggestion(val task: TodoItem, val index: Int, val count: Int) : ListRow {
        override val key get() = "suggested-${task.id}"
        override val contentType get() = "suggestion"
    }
}

private fun buildRows(
    sections: List<TaskSection>,
    suggested: List<TodoItem>,
    suggestionsExpanded: Boolean,
    onToggleSuggestions: () -> Unit,
): List<ListRow> =
    buildList {
        sections.forEach { section ->
            section.title?.let { add(ListRow.Header(it)) }
            section.tasks.forEachIndexed { index, task ->
                add(ListRow.Task(task, index, section.tasks.size))
            }
        }
        if (suggested.isNotEmpty()) {
            add(
                ListRow.Header(
                    SuggestedHeader,
                    count = suggested.size,
                    expanded = suggestionsExpanded,
                    onToggle = onToggleSuggestions,
                ),
            )
            if (suggestionsExpanded) {
                suggested.forEachIndexed { index, task ->
                    add(ListRow.Suggestion(task, index, suggested.size))
                }
            }
        }
    }

// Copy taken from windows/Strings/en-US/Resources.resw (Suggestions_Header).
private const val SuggestedHeader = "Suggested"

// Pixels, not dp — these compare raw LazyListState scroll offsets. Collapsing costs more
// travel than expanding on purpose: being one tap from capture matters more than the few
// rows the bar covers.
private const val CollapseAfterPx = 160
private const val ExpandAfterPx = 60
private const val ApproxRowPx = 200

@Composable
private fun SectionHeaderRow(row: ListRow.Header) {
    val label = if (row.count != null) "${row.text} (${row.count})" else row.text
    if (row.onToggle == null) {
        Text(
            label,
            style = MaterialTheme.typography.labelLarge,
            // Overdue is the one group heading that is itself a warning; the rest are neutral
            // dividers and should not compete with the rows under them.
            color = if (row.text == "Overdue") MaterialTheme.colorScheme.error
            else MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(start = 28.dp, top = 24.dp, bottom = 8.dp),
        )
        return
    }
    // The only collapsible header: once suggestions have been reviewed, tapping this tucks
    // the whole tray away behind its own count rather than leaving it to grow unbounded.
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = row.onToggle)
            .padding(start = 28.dp, end = 20.dp, top = 24.dp, bottom = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            label,
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Icon(
            if (row.expanded) Icons.Rounded.KeyboardArrowUp else Icons.Rounded.KeyboardArrowDown,
            contentDescription = if (row.expanded) "Collapse suggestions" else "Expand suggestions",
            tint = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

// "Showing: #tag ✕", sized to sit in the app bar's second line where the count normally is.
// Not an InputChip: its 32dp minimum would push the collapsed bar taller than the title it
// is collapsing to.
@Composable
internal fun TagFilterPill(tag: String, onClear: () -> Unit) {
    Surface(
        onClick = onClear,
        color = MaterialTheme.colorScheme.secondaryContainer,
        shape = RoundedCornerShape(8.dp),
    ) {
        Row(
            Modifier.padding(start = 8.dp, end = 4.dp, top = 2.dp, bottom = 2.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                "Showing: #$tag",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSecondaryContainer,
            )
            Icon(
                Icons.Rounded.Close,
                contentDescription = "Clear tag filter",
                tint = MaterialTheme.colorScheme.onSecondaryContainer,
                modifier = Modifier.padding(start = 4.dp).size(14.dp),
            )
        }
    }
}

// The capture field, docked above the navigation bar. Always one tap from a new task — ≤4s
// capture is the product premise, so this never becomes a plain FAB that hides the field
// behind a dialog.
//
// The bar keeps a fixed height in both states. Collapsing shrinks what is drawn, not what is
// reserved, so the list underneath never reflows mid-scroll — content sliding under a thumb
// that is already moving is the kind of thing that reads as jank rather than as motion.
private val ComposerBarHeight = 76.dp
private val ComposerSurfaceHeight = 56.dp

// Stacks the composer above the undo snackbar instead of letting Scaffold dock the
// snackbar above the whole bottomBar on its own — that put it above the composer's fixed
// slot, floating in the middle of the screen rather than reading as attached to anything.
// Ordering them in a Column instead makes SnackbarHost (zero height until Material has a
// message to show) the bottommost thing, flush with the outer NavigationBar, with the
// composer riding above it. animateContentSize turns that height change into a slide
// instead of a jump; imePadding lives here rather than on the composer alone so a
// snackbar showing while the keyboard is up rises with it instead of landing underneath.
@Composable
internal fun ComposerBar(collapsed: Boolean, snackbar: SnackbarHostState, onSubmit: (String) -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .imePadding()
            .animateContentSize(animationSpec = tween(MotionMedium)),
    ) {
        DockedComposer(collapsed = collapsed, onSubmit = onSubmit)
        SnackbarHost(snackbar)
    }
}

@Composable
private fun DockedComposer(collapsed: Boolean, onSubmit: (String) -> Unit) {
    // rememberSaveable: a half-typed task should survive a rotation.
    var draft by rememberSaveable { mutableStateOf("") }
    var openedByTap by remember { mutableStateOf(false) }
    val focusRequester = remember { FocusRequester() }

    // Text already typed always wins over the scroll position: collapsing a field with a
    // half-written task in it would look like the app had thrown the words away.
    val expanded = !collapsed || openedByTap || draft.isNotBlank()

    LaunchedEffect(collapsed) { if (collapsed) openedByTap = false }

    val submit = {
        if (draft.isNotBlank()) {
            onSubmit(draft)
            draft = ""
        }
    }

    // Insets on the Box and centring via contentAlignment, not Modifier.align on the Row: the
    // previous form measured as 1px tall while drawing full height, which left the Scaffold
    // placing the snackbar over an area that could not be touched.
    //
    // No navigationBarsPadding() here: this bar floats above the bottom NavigationBar rather
    // than the system gesture bar directly, and the outer Scaffold in HatchApp already
    // consumes that system inset once via its own bottomBar.
    // A scrim, not an opaque bar: rows scrolling underneath fade out into the page colour
    // instead of being sliced in half by a hard edge, and the composer still reads as
    // floating over the list rather than as a wall at the bottom of it.
    val surface = MaterialTheme.colorScheme.surface
    val scrim = remember(surface) {
        Brush.verticalGradient(listOf(Color.Transparent, surface.copy(alpha = 0.9f), surface))
    }

    // No imePadding() here — ComposerBar applies it once, above both this and the
    // snackbar, so the keyboard inset isn't paid twice.
    BoxWithConstraints(
        Modifier
            .fillMaxWidth()
            .height(ComposerBarHeight)
            .background(scrim)
            .padding(horizontal = ScreenPadding, vertical = 10.dp),
        contentAlignment = Alignment.CenterEnd,
    ) {
        // One control that resizes, not two that swap. The previous version cross-faded a
        // full-width field with a round button, so the two shapes were briefly on top of each
        // other at half opacity and the change of size read as a glitch rather than a move.
        // Now the same pill shrinks to a circle around the button that never leaves.
        val fullWidth = minOf(maxWidth, ContentMaxWidth)
        val width by animateDpAsState(
            targetValue = if (expanded) fullWidth else ComposerSurfaceHeight,
            // A spring, lightly damped: this is a direct response to a gesture in progress,
            // and a fixed-duration tween cannot keep up with a flick or slow down for a drag.
            animationSpec = spring(
                dampingRatio = Spring.DampingRatioNoBouncy,
                stiffness = Spring.StiffnessMediumLow,
            ),
            label = "composerWidth",
        )
        // Out fast, in slow: the text should be gone well before the pill reaches the button,
        // and should not appear until there is somewhere to put it.
        val fieldAlpha by animateFloatAsState(
            targetValue = if (expanded) 1f else 0f,
            animationSpec = tween(if (expanded) MotionMedium else MotionShort / 2),
            label = "composerField",
        )

        Surface(
            color = MaterialTheme.colorScheme.surfaceContainerHigh,
            shape = CircleShape,
            shadowElevation = 6.dp,
            modifier = Modifier.width(width).height(ComposerSurfaceHeight),
        ) {
            Box(Modifier.fillMaxSize()) {
                // Skipped entirely once invisible, so a collapsed composer holds no text
                // field and no focus target behind the button.
                if (fieldAlpha > 0.01f) {
                    Box(
                        Modifier
                            .fillMaxSize()
                            .padding(start = 20.dp, end = ComposerSurfaceHeight)
                            .alpha(fieldAlpha),
                        contentAlignment = Alignment.CenterStart,
                    ) {
                        // BasicTextField, not TextField: the filled TextField's own 56dp
                        // minimum plus this Surface's padding made the bar 92dp tall, which
                        // is a lot of screen to give a single line of text.
                        BasicTextField(
                            value = draft,
                            // trimStart, so a leading space can never get in. Submitting is
                            // gated on isNotBlank while the placeholder used to be gated on
                            // isEmpty, and a field holding just a space satisfied neither:
                            // no placeholder, no text, and a dead Add button. Same disagreement
                            // between "empty" and "blank" that put a phantom space in the
                            // search field (48ea91c).
                            onValueChange = { draft = it.trimStart() },
                            singleLine = true,
                            textStyle = MaterialTheme.typography.bodyLarge.copy(
                                color = MaterialTheme.colorScheme.onSurface,
                            ),
                            cursorBrush = SolidColor(MaterialTheme.colorScheme.primary),
                            keyboardOptions = KeyboardOptions(
                                capitalization = KeyboardCapitalization.Sentences,
                                imeAction = ImeAction.Done,
                            ),
                            keyboardActions = KeyboardActions(onDone = { submit() }),
                            modifier = Modifier
                                .fillMaxWidth()
                                .focusRequester(focusRequester),
                            decorationBox = { field ->
                                Box(contentAlignment = Alignment.CenterStart) {
                                    // isBlank, matching the submit gate below it.
                                    if (draft.isBlank()) {
                                        Text(
                                            "Add a task",
                                            style = MaterialTheme.typography.bodyLarge,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                            maxLines = 1,
                                        )
                                    }
                                    field()
                                }
                            },
                        )
                    }
                }

                // Grows as the field becomes submittable. A disabled-to-enabled colour flip
                // alone is easy to miss with a thumb over the button.
                val addScale by animateFloatAsState(
                    targetValue = if (!expanded || draft.isNotBlank()) 1f else 0.88f,
                    animationSpec = tween(MotionShort, easing = EmphasizedDecelerate),
                    label = "addScale",
                )
                FilledIconButton(
                    // The same button in both states: submits when there is something to
                    // submit, and otherwise opens the field it is standing in for.
                    onClick = { if (expanded) submit() else openedByTap = true },
                    enabled = !expanded || draft.isNotBlank(),
                    modifier = Modifier
                        .align(Alignment.CenterEnd)
                        .padding(end = 4.dp)
                        // 48dp, not smaller: Material's own accessibility minimum for a
                        // touch target, even though the surface it sits in reads visually
                        // tighter than that.
                        .size(48.dp)
                        .graphicsLayer { scaleX = addScale; scaleY = addScale },
                ) {
                    Icon(
                        Icons.Rounded.Add,
                        contentDescription = if (expanded) "Add task" else "Add a task",
                    )
                }
            }
        }

        // Only when the compact button was tapped — stealing focus (and the keyboard) every
        // time the list happens to stop scrolling would be unusable.
        LaunchedEffect(openedByTap) { if (openedByTap) focusRequester.requestFocus() }
    }
}

@Composable
private fun EmptyState(
    nav: String,
    tagFilter: String?,
    anyTasks: Boolean,
    modifier: Modifier = Modifier,
) {
    val (headline, subtext) = when {
        tagFilter != null -> "Nothing tagged #$tagFilter" to
            "No task in this list carries that tag. Clear the filter to see the rest."
        nav == NAV_MY_DAY -> "My Day is clear" to
            "Add a task here, or open one and add it to My Day."
        nav == NAV_IMPORTANT -> "Nothing marked Important" to
            "Star a task to keep it here."
        nav == NAV_PLANNED -> "Nothing planned" to
            "Tasks with a due date show up here, soonest first."
        !anyTasks -> "Nothing yet" to
            "Add a task below. No account needed — Sync is optional."
        else -> "This list is empty" to "Add a task below."
    }

    Column(
        // Scrollable despite never overflowing: pull-to-refresh triggers off nested scroll,
        // and an empty list is exactly the state most worth refreshing.
        modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(32.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        TonalIcon(
            icon = if (nav == NAV_IMPORTANT) Icons.Rounded.Star else Icons.Rounded.CheckCircle,
            container = MaterialTheme.colorScheme.primaryContainer,
            content = MaterialTheme.colorScheme.onPrimaryContainer,
            size = 84.dp,
        )
        Spacer(Modifier.height(20.dp))
        Text(headline, style = MaterialTheme.typography.titleLarge)
        Spacer(Modifier.height(8.dp))
        Text(
            subtext,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}

// Rows are one grouped container per section, the shape rounding only where the group
// actually ends — the pattern Pixel's own apps use in place of full-width dividers. Shared
// with the settings rows, so both screens speak the same shape language.
internal val GroupGap = 2.dp

// How far across a row has to travel before a swipe counts as a delete. Material's default is
// half; deleting propagates to every synced device, so this one asks for most of the row —
// raised again from 0.62 after a real swipe-only pass still read as too easy to trigger by
// accident, with no other affordance in the row to fall back on.
private const val SwipeCommitFraction = 0.8f

// How much larger than the system default the row's own horizontal-swipe touch slop is made,
// scoped to just the SwipeToDismissBox subtree. The list's own vertical scroll keeps the
// unscaled default, so a diagonal drag needs meaningfully more sideways travel than up-or-down
// travel before the row claims it — tilting anything genuinely diagonal toward the scroll.
private const val SwipeTouchSlopMultiplier = 3f

internal fun groupedShape(index: Int, count: Int) = RoundedCornerShape(
    topStart = if (index == 0) CardCorner else CardCornerInner,
    topEnd = if (index == 0) CardCorner else CardCornerInner,
    bottomStart = if (index == count - 1) CardCorner else CardCornerInner,
    bottomEnd = if (index == count - 1) CardCorner else CardCornerInner,
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun TaskRow(
    task: TodoItem,
    listNames: Map<String, String>,
    shape: RoundedCornerShape,
    onToggle: (TodoItem) -> Unit,
    onOpen: (TodoItem) -> Unit,
    onDelete: (TodoItem) -> Unit,
    onTagClick: ((String) -> Unit)?,
    // False only on My Day, which repurposes horizontal swipe for a page-level gesture
    // instead (see TaskListScreen) — a per-row gesture there would fight it for the same
    // drag, and would only ever fire for a swipe that starts on a row, not one that starts
    // between rows or on a header, which the request was any swipe on the page.
    swipeToDeleteEnabled: Boolean = true,
    modifier: Modifier = Modifier,
) {
    val haptics = LocalHapticFeedback.current
    val listName = listNames[task.listId]
    val due = remember(task.dueDate) { dueChipFor(task.dueDate) }
    // Matches windows/Converters/TagsPreviewConverter.cs + TagsOverflowCountConverter.cs:
    // the first 2 as chips, the rest folded into a single "+N" chip rather than wrapping
    // an unbounded row onto a second line in a compact list row.
    val tagChips = remember(task.tags) { task.tags.take(2) }
    val tagOverflow = remember(task.tags) { (task.tags.size - 2).coerceAtLeast(0) }
    // Title colour only: animating the whole row would run a per-row animation on scroll.
    val titleColor by animateColorAsState(
        if (task.isCompleted) MaterialTheme.colorScheme.onSurfaceVariant
        else MaterialTheme.colorScheme.onSurface,
        label = "titleColor",
    )

    // A fresh lambda each recomposition would defeat the stability config's skipping.
    val toggle = remember(task.id, task.isCompleted) {
        {
            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
            onToggle(task)
        }
    }

    if (!swipeToDeleteEnabled) {
        Box(modifier.padding(horizontal = ScreenPadding).clip(shape)) {
            TaskRowContent(task, listName, due, tagChips, tagOverflow, titleColor, toggle, onOpen, onTagClick)
        }
        return
    }

    // Deleting used to be far too easy, and the reason was not the threshold — it was which
    // signal the delete was hung off. See the collector below. The row also now has to travel
    // most of its own width rather than Material's default half.
    val dismissState = rememberSwipeToDismissBoxState(
        positionalThreshold = { distance -> distance * SwipeCommitFraction },
    )

    // The felt counterpart to the visual arming below: targetValue flips exactly when the
    // swipe crosses the commit threshold, and again if it retreats. Arming only — the
    // release already gets the LongPress in the delete collector.
    LaunchedEffect(dismissState) {
        snapshotFlow { dismissState.targetValue }
            .drop(1)
            .filter { it == SwipeToDismissBoxValue.EndToStart }
            .collect { haptics.performHapticFeedback(HapticFeedbackType.GestureThresholdActivate) }
    }

    // settledValue, not currentValue. currentValue flips the moment a drag crosses the
    // threshold — while the finger is still down — so the task was being deleted mid-gesture,
    // before the swipe had been released and before there was any chance to drag back. That
    // is what "it suddenly deleted" was: not an over-eager threshold but a delete fired from
    // a signal that means "where this would land", not "where it landed". settledValue only
    // changes once the gesture is over and the row has actually come to rest dismissed.
    //
    // Keyed on the state object rather than on its value: the body below changes the value,
    // and keying on that cancelled this coroutine in the middle of its own work.
    LaunchedEffect(dismissState) {
        snapshotFlow { dismissState.settledValue }
            // The value the box already holds is not a gesture. LazyColumn saves each item's
            // state under its key, which is the task id, so a task restored by Undo came back
            // carrying the dismissed box it left with — and that alone re-ran the delete on
            // the next frame. That is why Undo looked like it did nothing.
            .drop(1)
            .filter { it == SwipeToDismissBoxValue.EndToStart }
            .collect {
                haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                // snapTo, not reset(): reset animates to Settled, and the row leaves the
                // list within a frame or two, cancelling that animation and saving a
                // dismissed box under the task id. Snapping first is instant, so the state
                // that gets saved is always upright.
                dismissState.snapTo(SwipeToDismissBoxValue.Settled)
                onDelete(task)
            }
    }

    Box(
        modifier
            .padding(horizontal = ScreenPadding)
            .clip(shape),
    ) {
        // A diagonal drag races SwipeToDismissBox's own horizontal slop detector against the
        // list's vertical one, and near the top or bottom of the list — where the list has
        // little or nothing left to consume and so puts up less resistance — that race was
        // easy for the row to win by accident. Scoping a larger touchSlop to just this
        // subtree (the list's own vertical scrollable sits outside it, still at the system
        // default) means the row needs meaningfully more sideways travel before it claims a
        // gesture at all, tilting anything genuinely diagonal toward the scroll instead —
        // the same fix Gmail-style swipe rows use, applied through Compose's own tested
        // slop detector rather than a hand-rolled one.
        val baseViewConfiguration = LocalViewConfiguration.current
        val swipeViewConfiguration = remember(baseViewConfiguration) {
            object : ViewConfiguration by baseViewConfiguration {
                override val touchSlop: Float =
                    baseViewConfiguration.touchSlop * SwipeTouchSlopMultiplier
            }
        }
        CompositionLocalProvider(LocalViewConfiguration provides swipeViewConfiguration) {
        SwipeToDismissBox(
            state = dismissState,
            // One direction only: two-way makes an accidental delete far too easy.
            enableDismissFromStartToEnd = false,
            backgroundContent = {
                // Composed only while a swipe is actually under way. backgroundContent runs
                // for every row on screen, so animating unconditionally put three animation
                // objects behind each row for a gesture almost never in progress.
                if (dismissState.dismissDirection == SwipeToDismissBoxValue.Settled) {
                    return@SwipeToDismissBox
                }
                // Past the threshold the background commits to the stronger error colour and
                // the icon grows, so a swipe that will delete looks different from one that
                // will spring back — the only warning there is before the snackbar.
                val armed = dismissState.targetValue == SwipeToDismissBoxValue.EndToStart
                val container by animateColorAsState(
                    if (armed) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.errorContainer,
                    tween(MotionShort),
                    label = "swipeContainer",
                )
                val iconTint by animateColorAsState(
                    if (armed) MaterialTheme.colorScheme.onError
                    else MaterialTheme.colorScheme.onErrorContainer,
                    tween(MotionShort),
                    label = "swipeIcon",
                )
                val iconScale by animateFloatAsState(
                    if (armed) 1.15f else 0.85f,
                    tween(MotionShort, easing = EmphasizedDecelerate),
                    label = "swipeIconScale",
                )
                Row(
                    Modifier
                        .fillMaxSize()
                        .background(container)
                        .padding(horizontal = 24.dp),
                    horizontalArrangement = Arrangement.End,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(
                        Icons.Rounded.Delete,
                        contentDescription = null,
                        tint = iconTint,
                        modifier = Modifier.graphicsLayer { scaleX = iconScale; scaleY = iconScale },
                    )
                }
            },
        ) {
            TaskRowContent(task, listName, due, tagChips, tagOverflow, titleColor, toggle, onOpen, onTagClick)
        }
        }
    }
}

@Composable
private fun TaskRowContent(
    task: TodoItem,
    listName: String?,
    due: DueChip?,
    tagChips: List<String>,
    tagOverflow: Int,
    titleColor: Color,
    toggle: () -> Unit,
    onOpen: (TodoItem) -> Unit,
    onTagClick: ((String) -> Unit)?,
) {
            ListItem(
                // Body opens, checkbox completes: with editable fields there has to be a way
                // in that is not "complete it". A finished task also recedes without becoming
                // unreadable.
                modifier = Modifier
                    .alpha(if (task.isCompleted) 0.65f else 1f)
                    .clickable { onOpen(task) },
                leadingContent = { PriorityCheckbox(task, onToggle = toggle) },
                headlineContent = {
                    Text(
                        task.title,
                        style = MaterialTheme.typography.bodyLarge,
                        textDecoration = if (task.isCompleted) TextDecoration.LineThrough else null,
                        color = titleColor,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                },
                supportingContent = if (listName == null && due == null && tagChips.isEmpty()) null else {
                    {
                        // One metadata line, chips and all: the list name used to be joined
                        // into the same grey sentence as the due date, which meant the one
                        // piece that changes colour when it matters was buried in the middle
                        // of text that never does.
                        Row(
                            Modifier.padding(top = 4.dp),
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(6.dp),
                        ) {
                            if (due != null) DueDateChip(due)
                            if (listName != null) {
                                Text(
                                    listName,
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                            tagChips.forEach { tag ->
                                TagChip("#$tag", onClick = onTagClick?.let { { it(tag) } })
                            }
                            // Inert: "+2" is a count, not a tag, so there is nothing for it
                            // to filter by. Matches the Windows chip, also not clickable.
                            if (tagOverflow > 0) TagChip("+$tagOverflow", onClick = null)
                        }
                    }
                },
                trailingContent = if (!task.isStarred) null else {
                    {
                        Icon(
                            Icons.Rounded.Star,
                            contentDescription = "Important",
                            // Gold, the palette's tertiary — the same colour Windows draws a
                            // starred task in, and the reason tertiary is gold at all.
                            tint = MaterialTheme.colorScheme.tertiary,
                        )
                    }
                },
                colors = ListItemDefaults.colors(
                    containerColor = if (task.isCompleted)
                        MaterialTheme.colorScheme.surfaceContainerLow
                    else MaterialTheme.colorScheme.surfaceContainer,
                ),
    )
}

// The checkbox carries the priority, so an urgent task is visible from the control you are
// about to tap rather than from a fourth chip in a row that already has three.
@Composable
private fun PriorityCheckbox(task: TodoItem, onToggle: () -> Unit) {
    val priority = priorityColor(task.priority)
    val label = PriorityMetaLabels.getOrNull(task.priority)?.takeIf { it.isNotEmpty() }

    Checkbox(
        checked = task.isCompleted,
        onCheckedChange = { onToggle() },
        colors = CheckboxDefaults.colors(
            // Only the unchecked outline takes the priority colour: a checked box is a
            // finished task, and finished tasks are not urgent.
            uncheckedColor = priority ?: MaterialTheme.colorScheme.outline,
            checkedColor = MaterialTheme.colorScheme.primary,
        ),
        // Colour alone would leave the priority invisible to a screen reader.
        modifier = if (label == null) Modifier else Modifier.semantics {
            contentDescription = "${task.title}, $label"
        },
    )
}

// Transcription of the Suggested panel in windows/Views/TaskListPage.xaml: a task you could
// pull into today, with the one action that does it. No swipe-to-delete — this row is an
// offer, and deleting a task from a list it is not in would be a trap.
@Composable
private fun SuggestionRow(
    task: TodoItem,
    listNames: Map<String, String>,
    shape: RoundedCornerShape,
    onOpen: (TodoItem) -> Unit,
    onAddToMyDay: (TodoItem) -> Unit,
    modifier: Modifier = Modifier,
) {
    val listName = listNames[task.listId]
    val due = remember(task.dueDate) { dueChipFor(task.dueDate) }

    // Dimmed rather than outlined: a border read as too heavy a treatment for an offer, not
    // a real row. surfaceContainerLowest is a tone no other row uses (open tasks sit on
    // surfaceContainer, completed on surfaceContainerLow — the tone this row used to share,
    // which made an offer look like something already done), and the alpha fades the whole
    // card including its own "+" affordance, so the card itself reads as lighter-weight.
    Box(
        modifier
            .padding(horizontal = ScreenPadding)
            .clip(shape)
            .alpha(0.82f),
    ) {
        ListItem(
            modifier = Modifier.clickable { onOpen(task) },
            headlineContent = {
                Text(
                    task.title,
                    style = MaterialTheme.typography.bodyLarge,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            },
            supportingContent = if (listName == null && due == null) null else {
                {
                    Row(
                        Modifier.padding(top = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                    ) {
                        if (due != null) DueDateChip(due)
                        if (listName != null) {
                            Text(
                                listName,
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            },
            trailingContent = {
                // Tonal rather than a bare icon: this is the row's whole point, and a plain
                // glyph out at the margin reads as decoration.
                FilledIconButton(
                    onClick = { onAddToMyDay(task) },
                    colors = IconButtonDefaults.filledIconButtonColors(
                        containerColor = MaterialTheme.colorScheme.primaryContainer,
                        contentColor = MaterialTheme.colorScheme.onPrimaryContainer,
                    ),
                    // minimumInteractiveComponentSize before size: pads the touch target out
                    // to 48dp around the visually smaller 36dp button rather than growing it.
                    modifier = Modifier.minimumInteractiveComponentSize().size(36.dp),
                ) { Icon(Icons.Rounded.Add, contentDescription = "Add to My Day") }
            },
            colors = ListItemDefaults.colors(
                containerColor = MaterialTheme.colorScheme.surfaceContainerLowest,
            ),
        )
    }
}

// Compact pill for a row's tag preview — deliberately not AssistChip/SuggestionChip, whose
// 36dp minimum height would inflate every row in the list just to show two words of tag text.
@Composable
private fun TagChip(text: String, onClick: (() -> Unit)?) {
    Surface(
        color = MaterialTheme.colorScheme.secondaryContainer,
        shape = RoundedCornerShape(6.dp),
        // minimumInteractiveComponentSize only when there is something to tap: it pads the
        // touch target out to Material's 48dp minimum around the small visual chip rather
        // than growing the chip itself, and a non-interactive chip has no target to pad.
        modifier = if (onClick == null) {
            Modifier
        } else {
            Modifier.clickable(onClick = onClick).minimumInteractiveComponentSize()
        },
    ) {
        Text(
            text,
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSecondaryContainer,
            modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp),
        )
    }
}
