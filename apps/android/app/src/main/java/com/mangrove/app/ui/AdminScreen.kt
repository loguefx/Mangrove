package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.text.KeyboardOptions
import androidx.navigation.NavController
import com.mangrove.app.data.ActivityDto
import com.mangrove.app.data.AdminUserDto
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.CreateCredentialRequest
import com.mangrove.app.data.CreateLibraryRequest
import com.mangrove.app.data.CreateUserRequest
import com.mangrove.app.data.LibraryDto
import com.mangrove.app.data.ScanStatusDto
import com.mangrove.app.data.SettingDto
import com.mangrove.app.data.StorageTestRequest
import com.mangrove.app.data.UpdateLibraryRequest
import com.mangrove.app.data.UpdateUserRequest
import com.mangrove.app.ui.theme.BgDark
import com.mangrove.app.ui.theme.Teal
import com.mangrove.app.ui.theme.TealDeep
import com.mangrove.app.ui.theme.TealMint
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private val ROLES = listOf("Admin", "User", "ReadOnly")

@Composable
fun AdminScreen(container: AppContainer, nav: NavController) {
    var tab by remember { mutableStateOf(0) }
    val tabs = listOf("Settings", "Activity", "Libraries", "Users")

    Column(
        Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(BgDark, TealDeep))),
    ) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { nav.popBackStack() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
            }
            Text("Admin", fontSize = 22.sp, fontWeight = FontWeight.SemiBold)
        }

        TabRow(selectedTabIndex = tab, containerColor = androidx.compose.ui.graphics.Color.Transparent) {
            tabs.forEachIndexed { i, title ->
                Tab(selected = tab == i, onClick = { tab = i }, text = { Text(title) })
            }
        }

        when (tab) {
            0 -> AdminSettingsTab(container)
            1 -> AdminActivityTab(container)
            2 -> AdminLibrariesTab(container)
            else -> AdminUsersTab(container)
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Activity
// ---------------------------------------------------------------------------------------------

@Composable
private fun AdminActivityTab(container: AppContainer) {
    var rows by remember { mutableStateOf<List<ActivityDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }

    LaunchedRefresh {
        rows = runCatching { container.activity() }.getOrDefault(emptyList())
        loading = false
    }

    if (loading) {
        LoadingBox()
        return
    }

    // "Currently reading": each user's most recent unfinished chapter.
    val current = rows.filter { it.status == "reading" }.distinctBy { it.userId }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        if (rows.isEmpty()) {
            Text("No reading activity yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        }

        if (rows.isNotEmpty()) {
            SectionTitle("Currently reading")
            if (current.isEmpty()) {
                Text(
                    "Nobody has a chapter open right now.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            current.forEach { a ->
                Card {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(a.username, fontWeight = FontWeight.SemiBold)
                            Text(
                                (a.seriesName ?: "Unknown series") + " · Ch. " + trimNumber(a.chapterNumber) +
                                    (a.chapterTitle?.let { " — $it" } ?: ""),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text(
                                "p. ${a.page}" + if (a.pageCount > 0) "/${a.pageCount}" else "",
                                style = MaterialTheme.typography.labelLarge,
                                color = AmberWarn,
                                fontWeight = FontWeight.SemiBold,
                            )
                            Text(
                                timeAgo(a.updatedAt),
                                style = MaterialTheme.typography.labelSmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }
            }

            Spacer(Modifier.height(6.dp))
            SectionTitle("Recent activity")
            rows.forEach { a ->
                Card {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(a.username, fontWeight = FontWeight.SemiBold)
                            Text(
                                activityDescription(a),
                                style = MaterialTheme.typography.bodySmall,
                                color = if (a.status == "reading") AmberWarn else TealMint,
                            )
                        }
                        Text(
                            timeAgo(a.updatedAt),
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }
}

private val AmberWarn = Color(0xFFFBBF24)

private fun activityDescription(a: ActivityDto): String {
    val series = a.seriesName ?: "Unknown series"
    val ch = "Ch. " + trimNumber(a.chapterNumber) + (a.chapterTitle?.let { " — $it" } ?: "")
    return when (a.status) {
        "caught-up" -> "Caught up on $series — finished $ch"
        "finished" -> "Finished $ch of $series"
        else -> "Left off on page ${a.page}" + (if (a.pageCount > 0) "/${a.pageCount}" else "") + " of $ch — $series"
    }
}

private fun trimNumber(n: Float): String =
    if (n == n.toLong().toFloat()) n.toLong().toString() else n.toString()

/** Coarse "x ago" string from an ISO-8601 UTC timestamp. */
private fun timeAgo(iso: String): String {
    val then = runCatching { java.time.Instant.parse(if (iso.endsWith("Z") || iso.contains("+")) iso else "${iso}Z") }
        .getOrNull() ?: return iso
    val secs = java.time.Duration.between(then, java.time.Instant.now()).seconds.coerceAtLeast(0)
    return when {
        secs < 60 -> "just now"
        secs < 3600 -> "${secs / 60} min ago"
        secs < 86400 -> "${secs / 3600} hr ago"
        secs < 2592000 -> "${secs / 86400} d ago"
        else -> java.time.format.DateTimeFormatter.ofPattern("MMM d, yyyy")
            .withZone(java.time.ZoneId.systemDefault()).format(then)
    }
}

// ---------------------------------------------------------------------------------------------
// Shared building blocks
// ---------------------------------------------------------------------------------------------

@Composable
private fun Card(content: @Composable ColumnScope.() -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(16.dp))
            .background(MaterialTheme.colorScheme.surface)
            .padding(16.dp),
        content = content,
    )
}

@Composable
private fun ToggleChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Box(
        Modifier
            .clip(RoundedCornerShape(20.dp))
            .background(if (selected) Teal else MaterialTheme.colorScheme.surfaceVariant)
            .clickable(onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 8.dp),
    ) {
        Text(
            label,
            color = if (selected) androidx.compose.ui.graphics.Color.White else MaterialTheme.colorScheme.onSurfaceVariant,
            style = MaterialTheme.typography.labelLarge,
        )
    }
}

@Composable
private fun PrimaryButton(text: String, enabled: Boolean = true, onClick: () -> Unit) {
    Button(
        onClick = onClick,
        enabled = enabled,
        shape = RoundedCornerShape(12.dp),
        colors = ButtonDefaults.buttonColors(containerColor = Teal),
    ) { Text(text) }
}

// ---------------------------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------------------------

private data class SettingMeta(val label: String, val help: String?, val kind: String)

private fun settingMeta(key: String): SettingMeta = when (key) {
    "scan.intervalMinutes" -> SettingMeta(
        "Auto-scan interval (minutes)",
        "How often libraries are re-scanned so new chapters appear automatically. 0 disables it; values below 5 are treated as 5.",
        "number",
    )
    "scan.onStartup" -> SettingMeta("Scan on startup", "Run a scan shortly after the server starts.", "bool")
    "opds.enabled" -> SettingMeta("Enable OPDS feed", null, "bool")
    "server.baseUrl" -> SettingMeta("Public base URL", "Used for OPDS/links when behind a proxy.", "text")
    "theme.default" -> SettingMeta("Default theme", null, "text")
    else -> SettingMeta(key, null, "text")
}

@Composable
private fun AdminSettingsTab(container: AppContainer) {
    var settings by remember { mutableStateOf<List<SettingDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var status by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    LaunchedRefresh {
        settings = runCatching { container.settings() }.getOrDefault(emptyList())
        loading = false
    }

    if (loading) {
        LoadingBox()
        return
    }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        settings.forEachIndexed { i, s ->
            val meta = settingMeta(s.key)
            Card {
                Text(meta.label, fontWeight = FontWeight.Medium)
                if (meta.help != null) {
                    Text(
                        meta.help,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(top = 2.dp, bottom = 6.dp),
                    )
                }
                when (meta.kind) {
                    "bool" -> {
                        val on = s.value?.equals("true", ignoreCase = true) == true
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Switch(
                                checked = on,
                                onCheckedChange = { checked ->
                                    settings = settings.toMutableList().also {
                                        it[i] = s.copy(value = if (checked) "true" else "false")
                                    }
                                },
                            )
                            Spacer(Modifier.height(0.dp))
                            Text(
                                if (on) "  Enabled" else "  Disabled",
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                    else -> OutlinedTextField(
                        value = s.value ?: "",
                        onValueChange = { v ->
                            settings = settings.toMutableList().also { it[i] = s.copy(value = v) }
                        },
                        singleLine = true,
                        keyboardOptions = if (meta.kind == "number")
                            KeyboardOptions(keyboardType = KeyboardType.Number) else KeyboardOptions.Default,
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }

        Row(verticalAlignment = Alignment.CenterVertically) {
            PrimaryButton("Save settings") {
                scope.launch {
                    status = if (runCatching { container.saveSettings(settings) }.isSuccess) "Saved" else "Failed to save"
                }
            }
            status?.let {
                Text("  $it", color = TealMint, modifier = Modifier.padding(start = 8.dp))
            }
        }
        Spacer(Modifier.height(24.dp))
    }
}

// ---------------------------------------------------------------------------------------------
// Libraries
// ---------------------------------------------------------------------------------------------

@Composable
private fun AdminLibrariesTab(container: AppContainer) {
    var libraries by remember { mutableStateOf<List<LibraryDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var editing by remember { mutableStateOf<LibraryDto?>(null) }
    var adding by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var scans by remember { mutableStateOf<Map<Int, ScanStatusDto>>(emptyMap()) }
    val scope = rememberCoroutineScope()

    suspend fun reload() {
        libraries = runCatching { container.libraries() }.getOrDefault(emptyList())
        loading = false
    }

    LaunchedRefresh { reload() }

    // Poll scan status so the admin sees a live progress bar; fast while scanning, slow when idle.
    LaunchedEffect(libraries) {
        if (libraries.isEmpty()) return@LaunchedEffect
        val ids = libraries.map { it.id }
        while (true) {
            val updated = ids.mapNotNull { id ->
                runCatching { container.scanStatus(id) }.getOrNull()?.let { id to it }
            }.toMap()
            if (updated.isNotEmpty()) scans = scans + updated
            val busy = updated.values.any { it.state != "idle" }
            delay(if (busy) 1000 else 5000)
        }
    }

    if (loading) {
        LoadingBox()
        return
    }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }

        PrimaryButton("+ Add library") { adding = true }

        libraries.forEach { lib ->
            val folders = if (lib.paths.isNotEmpty()) lib.paths.map { it.path } else listOf(lib.rootPath)
            val st = scans[lib.id]
            val busy = st != null && st.state != "idle"
            Card {
                Text(lib.name, fontWeight = FontWeight.SemiBold)
                Text(
                    "${if (lib.storageKind == 1) "SMB / UNC" else "Local"} · ${lib.seriesCount} series · ${folders.size} folder" +
                        if (folders.size == 1) "" else "s",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(Modifier.height(8.dp))
                folders.forEach { f ->
                    Text(
                        f,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurface,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(8.dp))
                            .background(MaterialTheme.colorScheme.surfaceVariant)
                            .padding(horizontal = 10.dp, vertical = 6.dp),
                    )
                    Spacer(Modifier.height(4.dp))
                }
                Spacer(Modifier.height(8.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(
                        enabled = !busy,
                        onClick = {
                            scans = scans + (lib.id to ScanStatusDto(lib.id, "queued"))
                            scope.launch { runCatching { container.scanLibrary(lib.id) } }
                        },
                        shape = RoundedCornerShape(10.dp),
                    ) { Text(if (busy) "Scanning…" else "Scan") }
                    OutlinedButton(onClick = { editing = lib }, shape = RoundedCornerShape(10.dp)) { Text("Edit") }
                    OutlinedButton(
                        onClick = {
                            scope.launch {
                                if (runCatching { container.deleteLibrary(lib.id) }.isSuccess) reload()
                                else error = "Delete failed"
                            }
                        },
                        shape = RoundedCornerShape(10.dp),
                        colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    ) { Text("Delete") }
                }
                if (busy && st != null) {
                    Spacer(Modifier.height(8.dp))
                    Row(
                        Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text(
                            if (st.state == "queued") "Queued…" else (st.phase ?: "Scanning…"),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        if (st.total > 0) {
                            Text(
                                "${st.done}/${st.total}",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                    Spacer(Modifier.height(4.dp))
                    if (st.total > 0) {
                        LinearProgressIndicator(
                            progress = { st.done.toFloat() / st.total.toFloat() },
                            modifier = Modifier.fillMaxWidth(),
                        )
                    } else {
                        LinearProgressIndicator(Modifier.fillMaxWidth())
                    }
                }
            }
        }
        if (libraries.isEmpty()) Text("No libraries yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
        Spacer(Modifier.height(24.dp))
    }

    if (adding) {
        AddLibrarySheet(
            container = container,
            onClose = { adding = false },
            onCreated = { adding = false; scope.launch { reload() } },
        )
    }
    editing?.let { lib ->
        EditLibrarySheet(
            container = container,
            library = lib,
            onClose = { editing = null },
            onSaved = { editing = null; scope.launch { reload() } },
        )
    }
}

@Composable
private fun AddLibrarySheet(container: AppContainer, onClose: () -> Unit, onCreated: () -> Unit) {
    var name by remember { mutableStateOf("") }
    var smb by remember { mutableStateOf(false) }
    var paths by remember { mutableStateOf(listOf("")) }
    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var domain by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    val clean = paths.map { it.trim() }.filter { it.isNotEmpty() }

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Add library") },
        text = {
            Column(
                Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                OutlinedTextField(name, { name = it }, label = { Text("Name") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    ToggleChip("Local", !smb) { smb = false }
                    ToggleChip("SMB / UNC", smb) { smb = true }
                }
                PathEditor(paths, smb) { paths = it }
                if (smb) {
                    OutlinedTextField(username, { username = it }, label = { Text("Username") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    OutlinedTextField(
                        password, { password = it }, label = { Text("Password") }, singleLine = true,
                        visualTransformation = PasswordVisualTransformation(), modifier = Modifier.fillMaxWidth(),
                    )
                    OutlinedTextField(domain, { domain = it }, label = { Text("Domain (optional)") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                }
                message?.let { Text(it, color = if (it.startsWith("OK")) TealMint else MaterialTheme.colorScheme.error) }
                TextButton(
                    enabled = !busy && clean.isNotEmpty(),
                    onClick = {
                        scope.launch {
                            busy = true
                            message = try {
                                var ok = true
                                for (p in clean) {
                                    val r = container.testStorage(
                                        StorageTestRequest(
                                            storageKind = if (smb) 1 else 0, rootPath = p,
                                            username = if (smb) username else null,
                                            password = if (smb) password else null,
                                            domain = if (smb) domain else null,
                                        )
                                    )
                                    if (!r.success) { ok = false; message = "${p}: ${r.message}"; break }
                                }
                                if (ok) "OK · all folders reachable" else message
                            } catch (e: Exception) { e.message ?: "Test failed" }
                            busy = false
                        }
                    },
                ) { Text("Test connection") }
            }
        },
        confirmButton = {
            PrimaryButton("Create", enabled = !busy && name.isNotBlank() && clean.isNotEmpty()) {
                scope.launch {
                    busy = true
                    try {
                        var credentialId: Int? = null
                        if (smb) {
                            credentialId = container.createCredential(
                                CreateCredentialRequest("$name credentials", username, password, domain.ifBlank { null }, 1)
                            ).id
                        }
                        container.createLibrary(
                            CreateLibraryRequest(
                                name = name, type = 0, storageKind = if (smb) 1 else 0,
                                credentialId = credentialId, folderWatch = false, paths = clean,
                            )
                        )
                        onCreated()
                    } catch (e: Exception) {
                        message = e.message ?: "Failed to create"
                    } finally {
                        busy = false
                    }
                }
            }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Cancel") } },
    )
}

@Composable
private fun EditLibrarySheet(
    container: AppContainer,
    library: LibraryDto,
    onClose: () -> Unit,
    onSaved: () -> Unit,
) {
    val initial = if (library.paths.isNotEmpty()) library.paths.map { it.path } else listOf(library.rootPath)
    var name by remember { mutableStateOf(library.name) }
    var paths by remember { mutableStateOf(initial) }
    var folderWatch by remember { mutableStateOf(library.folderWatch) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    val clean = paths.map { it.trim() }.filter { it.isNotEmpty() }

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Edit library") },
        text = {
            Column(
                Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                OutlinedTextField(name, { name = it }, label = { Text("Name") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                PathEditor(paths, library.storageKind == 1) { paths = it }
                Text(
                    "New folders use this library's existing credentials. Removing a folder drops its content on the next scan (files on disk are untouched).",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Switch(checked = folderWatch, onCheckedChange = { folderWatch = it })
                    Text("  Watch folders for changes")
                }
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            PrimaryButton("Save", enabled = !busy && name.isNotBlank() && clean.isNotEmpty()) {
                scope.launch {
                    busy = true
                    try {
                        container.updateLibrary(
                            library.id,
                            UpdateLibraryRequest(name = name, folderWatch = folderWatch, paths = clean),
                        )
                        onSaved()
                    } catch (e: Exception) {
                        error = e.message ?: "Failed to save"
                    } finally {
                        busy = false
                    }
                }
            }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Cancel") } },
    )
}

@Composable
private fun PathEditor(paths: List<String>, smb: Boolean, onChange: (List<String>) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("Folders", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        paths.forEachIndexed { i, p ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = p,
                    onValueChange = { v -> onChange(paths.toMutableList().also { it[i] = v }) },
                    singleLine = true,
                    placeholder = { Text(if (smb) "\\\\NAS\\Manga" else "/data/Manga") },
                    modifier = Modifier.weight(1f),
                )
                if (paths.size > 1) {
                    TextButton(onClick = { onChange(paths.filterIndexed { idx, _ -> idx != i }) }) { Text("−") }
                }
            }
        }
        TextButton(onClick = { onChange(paths + "") }) { Text("+ Add another folder") }
    }
}

// ---------------------------------------------------------------------------------------------
// Users
// ---------------------------------------------------------------------------------------------

@Composable
private fun AdminUsersTab(container: AppContainer) {
    var users by remember { mutableStateOf<List<AdminUserDto>>(emptyList()) }
    var libraries by remember { mutableStateOf<List<LibraryDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var editing by remember { mutableStateOf<AdminUserDto?>(null) }
    var adding by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    suspend fun reload() {
        users = runCatching { container.users() }.getOrDefault(emptyList())
        libraries = runCatching { container.libraries() }.getOrDefault(emptyList())
        loading = false
    }

    LaunchedRefresh { reload() }

    if (loading) {
        LoadingBox()
        return
    }

    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        PrimaryButton("+ Add user") { adding = true }

        users.forEach { u ->
            Card {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(u.username, fontWeight = FontWeight.SemiBold)
                        Text(
                            (if (u.roles.isEmpty()) "User" else u.roles.joinToString(" · ")) +
                                if (u.isLocked) " · Locked" else "",
                            style = MaterialTheme.typography.bodySmall,
                            color = if (u.isLocked) MaterialTheme.colorScheme.error else TealMint,
                        )
                        u.email?.takeIf { it.isNotBlank() }?.let {
                            Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                    OutlinedButton(onClick = { editing = u }, shape = RoundedCornerShape(10.dp)) { Text("Edit") }
                }
            }
        }
        Spacer(Modifier.height(24.dp))
    }

    if (adding) {
        AddUserSheet(
            container = container,
            libraries = libraries,
            onClose = { adding = false },
            onSaved = { adding = false; scope.launch { reload() } },
        )
    }
    editing?.let { u ->
        EditUserSheet(
            container = container,
            user = u,
            libraries = libraries,
            onClose = { editing = null },
            onSaved = { editing = null; scope.launch { reload() } },
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun RoleAndAccess(
    roles: List<String>,
    onRoles: (List<String>) -> Unit,
    libraries: List<LibraryDto>,
    libraryIds: List<Int>,
    onLibraryIds: (List<Int>) -> Unit,
) {
    Text("Roles", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        ROLES.forEach { r ->
            ToggleChip(r, roles.any { it.equals(r, true) }) {
                onRoles(if (roles.any { it.equals(r, true) }) roles.filterNot { it.equals(r, true) } else roles + r)
            }
        }
    }
    Spacer(Modifier.height(6.dp))
    Text("Library access", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
    Text(
        "Admins always see everything. These grants apply to non-admins.",
        style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
    )
    libraries.forEach { lib ->
        Row(
            Modifier.fillMaxWidth().clickable {
                onLibraryIds(if (libraryIds.contains(lib.id)) libraryIds - lib.id else libraryIds + lib.id)
            },
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Checkbox(checked = libraryIds.contains(lib.id), onCheckedChange = {
                onLibraryIds(if (it) libraryIds + lib.id else libraryIds - lib.id)
            })
            Text(lib.name)
        }
    }
}

@Composable
private fun AddUserSheet(
    container: AppContainer,
    libraries: List<LibraryDto>,
    onClose: () -> Unit,
    onSaved: () -> Unit,
) {
    var username by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var roles by remember { mutableStateOf(listOf("User")) }
    var libraryIds by remember { mutableStateOf(listOf<Int>()) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Add user") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(username, { username = it }, label = { Text("Username") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(email, { email = it }, label = { Text("Email (optional)") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(
                    password, { password = it }, label = { Text("Password (min 6)") }, singleLine = true,
                    visualTransformation = PasswordVisualTransformation(), modifier = Modifier.fillMaxWidth(),
                )
                RoleAndAccess(roles, { roles = it }, libraries, libraryIds) { libraryIds = it }
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            PrimaryButton("Create", enabled = !busy && username.isNotBlank() && password.length >= 6) {
                scope.launch {
                    busy = true
                    try {
                        container.createUser(
                            CreateUserRequest(
                                username = username.trim(),
                                email = email.ifBlank { null },
                                password = password,
                                roles = roles.ifEmpty { listOf("User") },
                                libraryIds = libraryIds,
                            )
                        )
                        onSaved()
                    } catch (e: Exception) {
                        error = e.message ?: "Failed to create user"
                    } finally {
                        busy = false
                    }
                }
            }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Cancel") } },
    )
}

@Composable
private fun EditUserSheet(
    container: AppContainer,
    user: AdminUserDto,
    libraries: List<LibraryDto>,
    onClose: () -> Unit,
    onSaved: () -> Unit,
) {
    var roles by remember { mutableStateOf(user.roles.ifEmpty { listOf("User") }) }
    var libraryIds by remember { mutableStateOf(user.libraryIds) }
    var locked by remember { mutableStateOf(user.isLocked) }
    var newPassword by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text(user.username) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                RoleAndAccess(roles, { roles = it }, libraries, libraryIds) { libraryIds = it }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Switch(checked = locked, onCheckedChange = { locked = it })
                    Text("  Account locked")
                }
                OutlinedTextField(
                    newPassword, { newPassword = it }, label = { Text("Reset password (optional)") }, singleLine = true,
                    visualTransformation = PasswordVisualTransformation(), modifier = Modifier.fillMaxWidth(),
                )
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                TextButton(
                    onClick = {
                        scope.launch {
                            if (runCatching { container.deleteUser(user.id) }.isSuccess) onSaved()
                            else error = "Delete failed (can't delete the last admin)."
                        }
                    },
                ) { Text("Delete user", color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            PrimaryButton("Save", enabled = !busy) {
                scope.launch {
                    busy = true
                    try {
                        container.updateUser(
                            user.id,
                            UpdateUserRequest(roles = roles, isLocked = locked, libraryIds = libraryIds),
                        )
                        if (newPassword.length >= 6) container.resetPassword(user.id, newPassword)
                        onSaved()
                    } catch (e: Exception) {
                        error = e.message ?: "Failed to save"
                    } finally {
                        busy = false
                    }
                }
            }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Cancel") } },
    )
}

/** Runs [block] once when first composed (loads data for a tab). */
@Composable
private fun LaunchedRefresh(block: suspend () -> Unit) {
    androidx.compose.runtime.LaunchedEffect(Unit) { block() }
}
