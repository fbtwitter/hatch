package dev.hatch.android

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MediumTopAppBar
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp

// Mirrors the Windows Settings page: Appearance → Theme, then Sync, then About. Theme used
// to be an overflow menu on the task list, which put a preference in the same place as the
// screen's actions — Material puts persistent preferences on their own screen.
//
// No back arrow: Settings is a bottom-bar tab now, a peer of Tasks and Summary, not a screen
// pushed on top of one — there's nowhere "back" to go from a peer tab.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    themeMode: ThemeMode,
    sync: SyncState,
    onThemeMode: (ThemeMode) -> Unit,
    onOpenSync: () -> Unit,
) {
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        topBar = {
            MediumTopAppBar(
                title = { Text("Settings") },
                colors = TopAppBarDefaults.topAppBarColors(
                    scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer,
                ),
                scrollBehavior = scrollBehavior,
            )
        },
    ) { padding ->
        Column(
            Modifier
                .fillMaxSize()
                .verticalScroll(rememberScrollState())
                .padding(padding),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Column(
                Modifier
                    .widthIn(max = ContentMaxWidth)
                    .padding(horizontal = 12.dp),
            ) {
                SectionHeader("Appearance")
                // selectableGroup, so a screen reader announces "1 of 3" rather than three
                // unrelated checkable rows.
                Column(
                    Modifier.selectableGroup(),
                    verticalArrangement = Arrangement.spacedBy(GroupGap),
                ) {
                    val modes = ThemeMode.entries
                    modes.forEachIndexed { index, mode ->
                        val selected = mode == themeMode
                        SettingsRow(
                            shape = groupedShape(index, modes.size),
                            headline = themeLabel(mode),
                            supporting = themeCaption(mode),
                            // onClick = null: the row carries the click, so the button must
                            // not be a second target announced on its own.
                            trailing = { RadioButton(selected = selected, onClick = null) },
                            modifier = Modifier.selectable(
                                selected = selected,
                                role = Role.RadioButton,
                                onClick = { onThemeMode(mode) },
                            ),
                        )
                    }
                }

                SectionHeader("Sync")
                SettingsRow(
                    shape = groupedShape(0, 1),
                    headline = "Sync across devices",
                    supporting = syncSummary(sync),
                    leading = { Icon(Icons.Rounded.Refresh, contentDescription = null) },
                    trailing = {
                        Icon(Icons.AutoMirrored.Rounded.KeyboardArrowRight, contentDescription = null)
                    },
                    modifier = Modifier.clickable(onClick = onOpenSync),
                )

                SectionHeader("About")
                Column(verticalArrangement = Arrangement.spacedBy(GroupGap)) {
                    SettingsRow(
                        shape = groupedShape(0, 2),
                        headline = "Hatch companion",
                        supporting = "Version ${BuildConfig.VERSION_NAME}",
                        leading = { Icon(Icons.Rounded.Info, contentDescription = null) },
                    )
                    SettingsRow(
                        shape = groupedShape(1, 2),
                        headline = "Local first",
                        supporting = "No account needed. Your tasks live on this phone. " +
                            "Sync is optional, and everything is encrypted here before it leaves.",
                        leading = { Icon(Icons.Rounded.Lock, contentDescription = null) },
                    )
                }

                Spacer(Modifier.height(24.dp))
            }
        }
    }
}

private fun themeLabel(mode: ThemeMode) = when (mode) {
    ThemeMode.System -> "System default"
    ThemeMode.Light -> "Light"
    ThemeMode.Dark -> "Dark"
}

private fun themeCaption(mode: ThemeMode) = when (mode) {
    ThemeMode.System -> "Follows your phone's setting"
    ThemeMode.Light -> "Always light"
    ThemeMode.Dark -> "Always dark"
}

private fun syncSummary(sync: SyncState): String = when (sync) {
    is SyncState.On -> "On · " + (sync.email ?: "signed in")
    SyncState.NotConfigured -> "Not available in this build"
    SyncState.Working -> "Working…"
    SyncState.NeedsPassphrase, SyncState.WrongPassphrase ->
        "Signed in — passphrase needed to read your data"
    is SyncState.NeedsMfaCode -> "Signed in — two-factor code needed"
    is SyncState.Failed -> "The last attempt failed"
    is SyncState.Off -> "Off — your tasks stay on this phone"
}

@Composable
private fun SectionHeader(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.labelLarge,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(start = 16.dp, top = 24.dp, bottom = 8.dp),
    )
}

@Composable
private fun SettingsRow(
    shape: Shape,
    headline: String,
    supporting: String? = null,
    leading: (@Composable () -> Unit)? = null,
    trailing: (@Composable () -> Unit)? = null,
    modifier: Modifier = Modifier,
) {
    ListItem(
        // Clip first, then the caller's click: the other order leaves the ripple square
        // inside a rounded row.
        modifier = Modifier.clip(shape).then(modifier),
        headlineContent = { Text(headline) },
        supportingContent = if (supporting == null) null else {
            { Text(supporting) }
        },
        leadingContent = leading,
        trailingContent = trailing,
        colors = ListItemDefaults.colors(
            containerColor = MaterialTheme.colorScheme.surfaceContainer,
        ),
    )
}
