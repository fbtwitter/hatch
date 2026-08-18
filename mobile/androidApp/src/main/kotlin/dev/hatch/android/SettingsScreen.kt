package dev.hatch.android

import android.net.Uri
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ListItem
import androidx.compose.material3.ListItemDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.MediumTopAppBar
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.unit.dp
import java.time.LocalDate

// Mirrors the Windows Settings page: Appearance → Theme, then Sync, then Export, then About.
//
// No back arrow: Settings is a bottom-bar tab, a peer of My Day and Lists, not a screen
// pushed on top of one — there's nowhere "back" to go from a peer tab.
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    themeMode: ThemeMode,
    useDynamicColor: Boolean,
    sync: SyncState,
    snackbar: SnackbarHostState,
    onThemeMode: (ThemeMode) -> Unit,
    onDynamicColor: (Boolean) -> Unit,
    onOpenSync: () -> Unit,
    onExport: (Uri, ExportFormat) -> Unit,
) {
    val scrollBehavior = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()

    Scaffold(
        modifier = Modifier.nestedScroll(scrollBehavior.nestedScrollConnection),
        snackbarHost = { SnackbarHost(snackbar) },
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

                // Below Android 12 there is no wallpaper palette to read, so the row would
                // be a switch that does nothing.
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    Spacer(Modifier.height(GroupGap))
                    SettingsRow(
                        shape = groupedShape(0, 1),
                        headline = "Use wallpaper colours",
                        supporting = if (useDynamicColor) {
                            "Material You — Hatch takes its palette from your wallpaper"
                        } else {
                            "Off — Hatch uses its own blue"
                        },
                        leading = { PaletteSwatch() },
                        trailing = {
                            Switch(checked = useDynamicColor, onCheckedChange = null)
                        },
                        modifier = Modifier.toggleable(
                            value = useDynamicColor,
                            role = Role.Switch,
                            onValueChange = onDynamicColor,
                        ),
                    )
                }

                SectionHeader("Sync")
                SettingsRow(
                    shape = groupedShape(0, 1),
                    headline = "Sync across devices",
                    supporting = syncSummary(sync),
                    leading = {
                        TonalIcon(
                            Icons.Rounded.Refresh,
                            MaterialTheme.colorScheme.primaryContainer,
                            MaterialTheme.colorScheme.onPrimaryContainer,
                        )
                    },
                    trailing = {
                        Icon(
                            Icons.AutoMirrored.Rounded.KeyboardArrowRight,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    },
                    modifier = Modifier.clickable(onClick = onOpenSync),
                )

                SectionHeader("Your data")
                ExportRow(onExport)

                SectionHeader("About")
                Column(verticalArrangement = Arrangement.spacedBy(GroupGap)) {
                    SettingsRow(
                        shape = groupedShape(0, 2),
                        headline = "Hatch companion",
                        supporting = "Version ${BuildConfig.VERSION_NAME}",
                        leading = {
                            TonalIcon(
                                Icons.Rounded.Info,
                                MaterialTheme.colorScheme.surfaceContainerHighest,
                                MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        },
                    )
                    SettingsRow(
                        shape = groupedShape(1, 2),
                        headline = "Local first",
                        supporting = "No account needed. Your tasks live on this phone. " +
                            "Sync is optional, and everything is encrypted here before it leaves.",
                        leading = {
                            TonalIcon(
                                Icons.Rounded.Lock,
                                MaterialTheme.colorScheme.secondaryContainer,
                                MaterialTheme.colorScheme.onSecondaryContainer,
                            )
                        },
                    )
                }

                Spacer(Modifier.height(24.dp))
            }
        }
    }
}

// The Windows one-click export (SettingsViewModel.ExportAsync), through the system document
// picker: the file lands wherever the user points it and nowhere else, which is why this
// needs no storage permission and touches no network.
@Composable
private fun ExportRow(onExport: (Uri, ExportFormat) -> Unit) {
    var menuOpen by remember { mutableStateOf(false) }

    // One launcher per format rather than one contract rebuilt on the fly: CreateDocument
    // takes its MIME type at construction, and swapping that mid-flight re-registers the
    // launcher underneath itself.
    val json = rememberExportLauncher(ExportFormat.Json, onExport)
    val csv = rememberExportLauncher(ExportFormat.Csv, onExport)
    val markdown = rememberExportLauncher(ExportFormat.Markdown, onExport)

    Box {
        SettingsRow(
            shape = groupedShape(0, 1),
            headline = "Export tasks",
            supporting = "Save a copy as JSON, CSV or Markdown",
            leading = {
                TonalIcon(
                    Icons.Rounded.Share,
                    MaterialTheme.colorScheme.secondaryContainer,
                    MaterialTheme.colorScheme.onSecondaryContainer,
                )
            },
            trailing = {
                Icon(
                    Icons.AutoMirrored.Rounded.KeyboardArrowRight,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            },
            modifier = Modifier.clickable { menuOpen = true },
        )
        DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
            ExportFormat.entries.forEach { format ->
                DropdownMenuItem(
                    text = { Text(format.label) },
                    onClick = {
                        menuOpen = false
                        val name = "hatch-tasks-${LocalDate.now()}.${format.extension}"
                        when (format) {
                            ExportFormat.Json -> json.launch(name)
                            ExportFormat.Csv -> csv.launch(name)
                            ExportFormat.Markdown -> markdown.launch(name)
                        }
                    },
                )
            }
        }
    }
}

@Composable
private fun rememberExportLauncher(format: ExportFormat, onExport: (Uri, ExportFormat) -> Unit) =
    rememberLauncherForActivityResult(
        // remember(format): a fresh contract instance on every recomposition would re-register
        // this launcher with the activity's result registry each time.
        remember(format) { ActivityResultContracts.CreateDocument(format.mimeType) }
    ) { uri ->
        // null when the picker was dismissed — not an error, and not worth a message.
        if (uri != null) onExport(uri, format)
    }

// Three overlapping dots in the scheme's own key colours — a swatch, and the one leading
// icon in this app that shows what it does rather than describing it. material-icons-core
// ships 49 icons and no palette among them; this needs no eleventh-hour icon dependency.
@Composable
private fun PaletteSwatch() {
    Box(Modifier.size(AvatarSize), contentAlignment = Alignment.Center) {
        val dot = 15.dp
        Box(Modifier.offset(x = (-7).dp, y = (-4).dp).size(dot).clip(CircleShape)
            .background(MaterialTheme.colorScheme.primary))
        Box(Modifier.offset(x = 7.dp, y = (-4).dp).size(dot).clip(CircleShape)
            .background(MaterialTheme.colorScheme.tertiary))
        Box(Modifier.offset(y = 6.dp).size(dot).clip(CircleShape)
            .background(MaterialTheme.colorScheme.secondary))
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
