package dev.hatch.android

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.autofill.ContentType
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentType
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp

// Mirrors the Windows minimum (SettingsViewModel.SetSyncPassphraseAsync).
private const val MIN_PASSPHRASE = 8

// TOTP is always 6 digits (docs/mfa-spec.md).
private const val MFA_CODE_LENGTH = 6

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SyncScreen(
    sync: SyncState,
    onSignIn: (String, String) -> Unit,
    onSignUp: (String, String) -> Unit,
    onGithub: () -> Unit,
    onPassphrase: (String) -> Unit,
    onMfaCode: (String) -> Unit,
    onShowRecovery: (Boolean) -> Unit,
    onRedeemRecovery: (String) -> Unit,
    onRefresh: () -> Unit,
    onPush: () -> Unit,
    onSignOut: () -> Unit,
    onBack: () -> Unit,
    snackbar: SnackbarHostState,
) {
    // Hoisted above the `when`: the Working branch removes the form from composition, so
    // state remembered inside it would force a retype after every failure.
    var email by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }
    var creating by rememberSaveable { mutableStateOf(false) }
    var passphrase by rememberSaveable { mutableStateOf("") }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            TopAppBar(
                title = { Text("Sync") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        // The spinner owns the whole screen, so it stays outside the scroll container —
        // fillMaxSize means nothing under an unbounded height constraint.
        if (sync is SyncState.Working) {
            Box(Modifier.fillMaxSize().padding(padding), Alignment.Center) {
                CircularProgressIndicator()
            }
            return@Scaffold
        }

        // Scrollable because the MFA and passphrase forms put their submit button under the
        // keyboard on a short screen, with no way to reach it.
        Column(
            Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .imePadding(),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Column(Modifier.widthIn(max = ContentMaxWidth).padding(24.dp)) {
                when (sync) {
                    SyncState.NotConfigured -> Info(
                        "Not configured",
                        "Add supabase.url and supabase.key to mobile/local.properties, then rebuild.",
                    )
                    // Handled above, before the scroll container.
                    SyncState.Working -> Unit
                    is SyncState.Off -> CredentialsForm(
                        email = email,
                        password = password,
                        creating = creating,
                        error = sync.error,
                        notice = sync.notice,
                        onEmail = { email = it },
                        onPassword = { password = it },
                        onToggleMode = { creating = !creating },
                        onSubmit = { if (creating) onSignUp(email, password) else onSignIn(email, password) },
                        onGithub = onGithub,
                    )
                    is SyncState.NeedsMfaCode ->
                        if (sync.redeeming) {
                            RecoveryCodeForm(
                                error = sync.error,
                                onSubmit = onRedeemRecovery,
                                onBack = { onShowRecovery(false) },
                            )
                        } else {
                            MfaCodeForm(
                                error = sync.error,
                                onSubmit = onMfaCode,
                                onUseRecovery = { onShowRecovery(true) },
                                onSignOut = onSignOut,
                            )
                        }
                    SyncState.NeedsPassphrase -> PassphraseForm(
                        "These tasks are end-to-end encrypted. Enter your sync passphrase to read them.",
                        isError = false,
                        onSubmit = onPassphrase,
                    )
                    SyncState.WrongPassphrase -> PassphraseForm(
                        "That passphrase can't decrypt this account's data. Nothing has been changed or lost.",
                        isError = true,
                        onSubmit = onPassphrase,
                    )
                    is SyncState.On -> Column {
                        Text("Sync is on", style = MaterialTheme.typography.titleMedium)
                        Spacer(Modifier.height(4.dp))
                        Text(
                            sync.email ?: "signed in",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        if (sync.serverUpdatedAt.isNotEmpty()) {
                            Text(
                                // Trimmed: seconds and microseconds are noise here.
                                "Server copy: " + sync.serverUpdatedAt.take(16).replace('T', ' ') + " UTC",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }

                        if (!sync.hasPassphrase) {
                            Spacer(Modifier.height(16.dp))
                            Banner(
                                "Set a passphrase to send changes up. Your tasks are encrypted on " +
                                    "this phone before they leave it, so nobody with server access " +
                                    "can read them — not even us. There is no way to recover it.",
                                MaterialTheme.colorScheme.secondaryContainer,
                                MaterialTheme.colorScheme.onSecondaryContainer,
                            )
                            Spacer(Modifier.height(12.dp))
                            OutlinedTextField(
                                value = passphrase,
                                onValueChange = { passphrase = it },
                                label = { Text("Passphrase") },
                                singleLine = true,
                                visualTransformation = PasswordVisualTransformation(),
                                keyboardOptions = KeyboardOptions(
                                    keyboardType = KeyboardType.Password,
                                    imeAction = ImeAction.Done,
                                ),
                                modifier = Modifier.fillMaxWidth(),
                            )
                            Text(
                                "At least $MIN_PASSPHRASE characters.",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.padding(top = 4.dp),
                            )
                            Spacer(Modifier.height(8.dp))
                            Button(
                                onClick = { onPassphrase(passphrase) },
                                // Must match Windows exactly: a shorter passphrase set here
                                // would be rejected there, and the clients would diverge.
                                enabled = passphrase.length >= MIN_PASSPHRASE,
                                modifier = Modifier.fillMaxWidth(),
                            ) { Text("Set passphrase") }
                        }

                        Spacer(Modifier.height(20.dp))
                        Button(onClick = onRefresh, Modifier.fillMaxWidth()) { Text("Pull now") }
                        Spacer(Modifier.height(8.dp))
                        Button(
                            onClick = onPush,
                            enabled = sync.hasPassphrase,
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Push now") }
                        Spacer(Modifier.height(8.dp))
                        OutlinedButton(onClick = onSignOut, Modifier.fillMaxWidth()) { Text("Sign out") }
                    }
                    is SyncState.Failed -> Column {
                        Info("Sync failed", sync.message)
                        Spacer(Modifier.height(20.dp))
                        Button(onClick = onRefresh, Modifier.fillMaxWidth()) { Text("Try again") }
                    }
                }
            }
        }
    }
}

@Composable
private fun CredentialsForm(
    email: String,
    password: String,
    creating: Boolean,
    error: String?,
    notice: String?,
    onEmail: (String) -> Unit,
    onPassword: (String) -> Unit,
    onToggleMode: () -> Unit,
    onSubmit: () -> Unit,
    onGithub: () -> Unit,
) {
    Column {
        Text(
            if (creating) "Create a sync account" else "Sync across devices",
            style = MaterialTheme.typography.titleMedium,
        )
        Spacer(Modifier.height(4.dp))
        Text(
            "Optional. Your tasks stay on this phone until you sign in.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        if (error != null) {
            Spacer(Modifier.height(16.dp))
            Banner(error, MaterialTheme.colorScheme.errorContainer, MaterialTheme.colorScheme.onErrorContainer)
        }
        if (notice != null) {
            Spacer(Modifier.height(16.dp))
            Banner(notice, MaterialTheme.colorScheme.secondaryContainer, MaterialTheme.colorScheme.onSecondaryContainer)
        }

        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = email,
            onValueChange = onEmail,
            label = { Text("Email") },
            singleLine = true,
            isError = error != null,
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Email,
                imeAction = ImeAction.Next,
            ),
            // Without content types password managers cannot see this form at all. The
            // passphrase and MFA fields deliberately carry none: a manager offering to
            // save the passphrase as "the password" would teach exactly the wrong thing.
            modifier = Modifier
                .fillMaxWidth()
                .semantics { contentType = ContentType.EmailAddress + ContentType.Username },
        )
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = password,
            onValueChange = onPassword,
            label = { Text("Password") },
            singleLine = true,
            isError = error != null,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier
                .fillMaxWidth()
                .semantics {
                    contentType = if (creating) ContentType.NewPassword else ContentType.Password
                },
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = onSubmit,
            enabled = email.isNotBlank() && password.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text(if (creating) "Create account" else "Sign in") }
        Spacer(Modifier.height(8.dp))
        TextButton(onClick = onToggleMode, modifier = Modifier.fillMaxWidth()) {
            Text(if (creating) "I already have an account" else "Create an account instead")
        }

        Spacer(Modifier.height(20.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            HorizontalDivider(Modifier.weight(1f))
            Text(
                "  or  ",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            HorizontalDivider(Modifier.weight(1f))
        }
        Spacer(Modifier.height(20.dp))
        OutlinedButton(onClick = onGithub, modifier = Modifier.fillMaxWidth()) {
            Text("Continue with GitHub")
        }
    }
}

@Composable
private fun Banner(text: String, container: Color, content: Color) {
    Surface(color = container, shape = MaterialTheme.shapes.large, modifier = Modifier.fillMaxWidth()) {
        Text(
            text,
            style = MaterialTheme.typography.bodyMedium,
            color = content,
            modifier = Modifier.padding(16.dp),
        )
    }
}

@Composable
private fun RecoveryCodeForm(error: String?, onSubmit: (String) -> Unit, onBack: () -> Unit) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Use a recovery code", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        // Stated plainly: this is not what most people expect a recovery code to do.
        Text(
            "This turns two-factor authentication OFF and discards your remaining codes — " +
                "it is not a one-time sign-in. Set it up again afterwards on Windows.\n\n" +
                "Your tasks and your sync passphrase are unaffected.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        if (error != null) {
            Spacer(Modifier.height(16.dp))
            Banner(error, MaterialTheme.colorScheme.errorContainer, MaterialTheme.colorScheme.onErrorContainer)
        }

        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = value,
            onValueChange = { value = it.uppercase() },
            label = { Text("Recovery code") },
            placeholder = { Text("XXXXX-XXXXX") },
            singleLine = true,
            isError = error != null,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(value) },
            enabled = value.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Turn off two-factor and continue") }
        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = onBack, Modifier.fillMaxWidth()) { Text("Back") }
    }
}

@Composable
private fun MfaCodeForm(
    error: String?,
    onSubmit: (String) -> Unit,
    onUseRecovery: () -> Unit,
    onSignOut: () -> Unit,
) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Two-factor code", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            "This account uses an authenticator app. Enter the current 6-digit code to " +
                "turn sync on. Your tasks on this phone are unaffected.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )

        if (error != null) {
            Spacer(Modifier.height(16.dp))
            Banner(error, MaterialTheme.colorScheme.errorContainer, MaterialTheme.colorScheme.onErrorContainer)
        }

        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = value,
            // Stops a paste of "123 456" being rejected for a reason the user cannot see.
            onValueChange = { value = it.filter(Char::isDigit).take(MFA_CODE_LENGTH) },
            label = { Text("6-digit code") },
            singleLine = true,
            isError = error != null,
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.NumberPassword,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(value) },
            enabled = value.length == MFA_CODE_LENGTH,
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Verify") }
        Spacer(Modifier.height(8.dp))
        // The routes out: without them a lost authenticator strands this screen, and since
        // the aal2 policy landed, signing out alone would not help either.
        TextButton(onClick = onUseRecovery, Modifier.fillMaxWidth()) {
            Text("Lost your authenticator? Use a recovery code")
        }
        OutlinedButton(onClick = onSignOut, Modifier.fillMaxWidth()) { Text("Sign out") }
    }
}

@Composable
private fun PassphraseForm(prompt: String, isError: Boolean, onSubmit: (String) -> Unit) {
    var value by remember { mutableStateOf("") }

    Column {
        Text("Sync passphrase", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            prompt,
            style = MaterialTheme.typography.bodyMedium,
            color = if (isError) MaterialTheme.colorScheme.error
                    else MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = value,
            onValueChange = { value = it },
            label = { Text("Passphrase") },
            singleLine = true,
            isError = isError,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Password,
                imeAction = ImeAction.Done,
            ),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(value) },
            // Deliberately not the minimum: this unlocks an existing row, and enforcing it
            // would lock out data encrypted before the rule existed.
            enabled = value.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) { Text("Unlock") }
    }
}

@Composable
private fun Info(title: String, body: String) {
    Column {
        Text(title, style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            body,
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
