package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Download
import androidx.compose.material.icons.filled.Downloading
import androidx.compose.material.icons.filled.Star
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.StarBorder
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.setValue
import kotlinx.coroutines.launch
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.ChapterDto
import com.mangrove.app.data.DownloadProgress
import com.mangrove.app.data.DownloadRequest
import com.mangrove.app.data.DownloadState
import com.mangrove.app.data.ProgressDto
import com.mangrove.app.data.SeriesDetailDto
import com.mangrove.app.ui.theme.BgDark
import com.mangrove.app.ui.theme.TealMint

private data class ChapterRow(val chapter: ChapterDto, val volumeNumber: Float)

private data class ResumeTarget(val chapterId: Int, val page: Int, val label: String, val isNext: Boolean)

private fun isEpub(format: String) = format.equals("epub", ignoreCase = true)

/**
 * Where to drop the reader next: the most recently touched chapter if still in progress, otherwise
 * the next unread chapter after the last one finished. Null when there's nothing meaningful to resume.
 */
private fun computeResume(rows: List<ChapterRow>, progress: List<ProgressDto>): ResumeTarget? {
    if (rows.isEmpty() || progress.isEmpty()) return null
    fun label(c: ChapterDto): String =
        c.title?.takeIf { it.isNotBlank() }
            ?: if (c.number > 0f) "Chapter ${trimNum(c.number)}" else "Chapter"

    val latest = progress.maxByOrNull { it.updatedAt ?: "" } ?: return null
    val idx = rows.indexOfFirst { it.chapter.id == latest.chapterId }
    if (idx < 0) return null
    if (!latest.isRead) {
        val c = rows[idx].chapter
        return ResumeTarget(c.id, latest.page, label(c), isNext = false)
    }
    val next = rows.getOrNull(idx + 1) ?: return null
    return ResumeTarget(next.chapter.id, 0, label(next.chapter), isNext = true)
}

@Composable
fun SeriesScreen(container: AppContainer, nav: NavController, seriesId: Int) {
    var detail by remember { mutableStateOf<SeriesDetailDto?>(null) }
    var failed by remember { mutableStateOf(false) }
    var favorite by remember { mutableStateOf(false) }
    val progress by container.downloadManager.progress.collectAsState()
    var downloadedIds by remember { mutableStateOf<Set<Int>>(emptySet()) }
    var readIds by remember { mutableStateOf<Set<Int>>(emptySet()) }
    var progressRows by remember { mutableStateOf<List<ProgressDto>>(emptyList()) }

    val scope = rememberCoroutineScope()

    suspend fun loadProgress() {
        progressRows = runCatching { container.seriesProgress(seriesId) }.getOrDefault(emptyList())
        readIds = progressRows.filter { it.isRead }.map { it.chapterId }.toSet()
    }

    suspend fun load() {
        runCatching { container.seriesDetail(seriesId) }
            .onSuccess { detail = it; favorite = it.wantToRead; failed = false }
            .onFailure { if (detail == null) failed = true }
        loadProgress()
    }

    fun toggleChapterRead(chapterId: Int, read: Boolean) {
        readIds = if (read) readIds + chapterId else readIds - chapterId // optimistic
        scope.launch {
            val ok = runCatching { container.markChapterRead(chapterId, read) }.isSuccess
            if (!ok) loadProgress()
        }
    }

    fun markAllRead(read: Boolean) {
        scope.launch {
            runCatching { container.markSeriesRead(seriesId, read) }
            loadProgress()
        }
    }

    fun toggleFavorite() {
        val want = !favorite
        favorite = want // optimistic
        scope.launch {
            val ok = runCatching {
                if (want) container.addFavorite(seriesId) else container.removeFavorite(seriesId)
            }.isSuccess
            if (!ok) favorite = !want // revert on failure
        }
    }

    LaunchedEffect(seriesId) { load() }
    // Refresh the "downloaded" set whenever a download completes.
    LaunchedEffect(progress) {
        downloadedIds = container.downloadStore.allMetas().filter { it.complete }.map { it.chapterId }.toSet()
    }
    // Pick up newly-scanned chapters automatically while viewing the series.
    LifecycleResumeRefresh { scope.launch { load() } }
    AutoRefresh { scope.launch { load() } }

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { nav.popBackStack() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
            }
            Text(
                detail?.name ?: "Series",
                fontSize = 20.sp,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
                overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (detail != null) {
                IconButton(onClick = { toggleFavorite() }) {
                    Icon(
                        if (favorite) Icons.Filled.Star else Icons.Outlined.StarBorder,
                        contentDescription = if (favorite) "Remove from favorites" else "Add to favorites",
                        tint = if (favorite) TealMint else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
            }
        }

        when {
            failed -> MessageBox("Couldn't load this series.")
            detail == null -> LoadingBox()
            else -> {
                val d = detail!!
                val rows = d.volumes
                    .sortedBy { it.number }
                    .flatMap { v -> v.chapters.sortedBy { it.number }.map { ChapterRow(it, v.number) } }
                val resume = computeResume(rows, progressRows)

                LazyColumn(Modifier.fillMaxSize()) {
                    item {
                        // Hero header: cover backdrop fading into the page, with the sharp cover + key meta.
                        Box(Modifier.fillMaxWidth().height(230.dp)) {
                            if (d.hasCover) {
                                NetworkImage(
                                    container,
                                    "api/series/${d.id}/cover",
                                    null,
                                    Modifier.fillMaxSize().alpha(0.4f),
                                )
                            }
                            Box(
                                Modifier.fillMaxSize().background(
                                    Brush.verticalGradient(listOf(Color.Transparent, BgDark)),
                                ),
                            )
                            Row(
                                Modifier.align(Alignment.BottomStart).fillMaxWidth().padding(16.dp),
                                verticalAlignment = Alignment.Bottom,
                            ) {
                                Box(
                                    Modifier
                                        .width(120.dp)
                                        .aspectRatio(2f / 3f)
                                        .clip(RoundedCornerShape(12.dp))
                                        .background(MaterialTheme.colorScheme.surfaceVariant),
                                    contentAlignment = Alignment.Center,
                                ) {
                                    if (d.hasCover) {
                                        NetworkImage(container, "api/series/${d.id}/cover", d.name, Modifier.fillMaxSize())
                                    } else {
                                        Text("No cover", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                    }
                                }
                                Column(Modifier.padding(start = 16.dp).weight(1f)) {
                                    Text(
                                        d.name,
                                        style = MaterialTheme.typography.titleLarge,
                                        fontWeight = FontWeight.Bold,
                                        maxLines = 3,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                    listOfNotNull(
                                        d.ageRating?.takeIf { it.isNotBlank() },
                                        d.language?.takeIf { it.isNotBlank() }?.uppercase(),
                                        d.publisher?.takeIf { it.isNotBlank() },
                                    ).takeIf { it.isNotEmpty() }?.let {
                                        Text(
                                            it.joinToString(" · "),
                                            style = MaterialTheme.typography.bodySmall,
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                            modifier = Modifier.padding(top = 4.dp),
                                        )
                                    }
                                    Text("${rows.size} chapters", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 6.dp))
                                }
                            }
                        }

                        resume?.let { r ->
                            Row(
                                Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = 16.dp, vertical = 4.dp)
                                    .clip(RoundedCornerShape(16.dp))
                                    .background(TealMint.copy(alpha = 0.15f))
                                    .clickable { nav.navigate("reader/${r.chapterId}") }
                                    .padding(horizontal = 16.dp, vertical = 12.dp),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Column(Modifier.weight(1f)) {
                                    Text(
                                        if (r.isNext) "UP NEXT" else "CONTINUE READING",
                                        style = MaterialTheme.typography.labelSmall,
                                        color = TealMint,
                                        fontWeight = FontWeight.SemiBold,
                                    )
                                    Text(
                                        r.label + if (!r.isNext && r.page > 0) " · page ${r.page + 1}" else "",
                                        style = MaterialTheme.typography.bodyLarge,
                                        fontWeight = FontWeight.SemiBold,
                                        maxLines = 1,
                                        overflow = TextOverflow.Ellipsis,
                                    )
                                }
                                Text(
                                    if (r.isNext) "Start →" else "Resume →",
                                    color = TealMint,
                                    fontWeight = FontWeight.SemiBold,
                                )
                            }
                        }

                        Column(Modifier.padding(horizontal = 16.dp)) {
                            listOfNotNull(
                                d.writer?.takeIf { it.isNotBlank() }?.let { "Story: $it" },
                                d.penciller?.takeIf { it.isNotBlank() }?.let { "Art: $it" },
                            ).forEach {
                                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 2.dp))
                            }
                            d.genres?.takeIf { it.isNotBlank() }?.let {
                                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp))
                            }
                            d.tags?.takeIf { it.isNotBlank() }?.let {
                                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp))
                            }
                        }
                        d.summary?.takeIf { it.isNotBlank() }?.let {
                            Text(it, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp), color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                            SectionTitle("Chapters")
                            androidx.compose.foundation.layout.Spacer(Modifier.weight(1f))
                            val allRead = rows.isNotEmpty() && rows.all { it.chapter.id in readIds }
                            TextButton(onClick = { markAllRead(!allRead) }) {
                                Text(if (allRead) "Mark unread" else "Mark all read", color = TealMint)
                            }
                            val downloadable = rows.filter { !isEpub(it.chapter.fileFormat) && it.chapter.id !in downloadedIds }
                            if (downloadable.isNotEmpty()) {
                                TextButton(onClick = {
                                    downloadable.forEach { row ->
                                        container.downloadManager.enqueue(
                                            DownloadRequest(row.chapter.id, d.id, d.name, row.volumeNumber, row.chapter.number, row.chapter.title),
                                        )
                                    }
                                }, modifier = Modifier.padding(end = 8.dp)) {
                                    Text("Download all", color = TealMint)
                                }
                            }
                        }
                    }

                    items(rows, key = { it.chapter.id }) { row ->
                        ChapterListItem(
                            row = row,
                            downloaded = row.chapter.id in downloadedIds,
                            read = row.chapter.id in readIds,
                            onToggleRead = { toggleChapterRead(row.chapter.id, row.chapter.id !in readIds) },
                            progress = progress[row.chapter.id],
                            epub = isEpub(row.chapter.fileFormat),
                            onOpen = { nav.navigate("reader/${row.chapter.id}") },
                            onDownload = {
                                container.downloadManager.enqueue(
                                    DownloadRequest(row.chapter.id, d.id, d.name, row.volumeNumber, row.chapter.number, row.chapter.title),
                                )
                            },
                            onDelete = {
                                container.downloadStore.deleteChapter(row.chapter.id)
                                downloadedIds = downloadedIds - row.chapter.id
                            },
                        )
                        HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
                    }
                }
            }
        }
    }
}

@Composable
private fun ChapterListItem(
    row: ChapterRow,
    downloaded: Boolean,
    read: Boolean,
    onToggleRead: () -> Unit,
    progress: DownloadProgress?,
    epub: Boolean,
    onOpen: () -> Unit,
    onDownload: () -> Unit,
    onDelete: () -> Unit,
) {
    val c = row.chapter
    val label = buildString {
        if (row.volumeNumber > 0f) append("Vol ${trimNum(row.volumeNumber)} · ")
        append("Ch ${trimNum(c.number)}")
        if (!c.title.isNullOrBlank()) append(" — ${c.title}")
    }
    val active = progress != null &&
        (progress.state == DownloadState.Queued || progress.state == DownloadState.Running)

    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onOpen)
            .padding(start = 8.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onToggleRead) {
            Icon(
                if (read) Icons.Filled.CheckCircle else Icons.Outlined.CheckCircle,
                contentDescription = if (read) "Mark as unread" else "Mark as read",
                tint = if (read) TealMint else MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Text(
            label,
            modifier = Modifier.weight(1f).alpha(if (read) 0.5f else 1f),
            maxLines = 2,
        )
        Text(
            "${c.pageCount}p",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 8.dp),
        )
        when {
            downloaded -> IconButton(onClick = onDelete) {
                Icon(Icons.Filled.CheckCircle, contentDescription = "Downloaded (tap to remove)", tint = TealMint)
            }
            active -> {
                val total = progress?.total ?: 0
                val done = progress?.done ?: 0
                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(end = 8.dp)) {
                    if (total > 0) {
                        Text(
                            "$done/$total",
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            modifier = Modifier.padding(end = 6.dp),
                        )
                    }
                    CircularProgressIndicator(
                        modifier = Modifier.size(18.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.secondary,
                    )
                }
            }
            progress?.state == DownloadState.Failed -> IconButton(onClick = onDownload) {
                Icon(Icons.Filled.Download, contentDescription = "Retry download", tint = MaterialTheme.colorScheme.error)
            }
            !epub -> IconButton(onClick = onDownload) {
                Icon(Icons.Filled.Download, contentDescription = "Download for offline")
            }
            else -> Icon(
                Icons.Filled.Downloading,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.surfaceVariant,
                modifier = Modifier.padding(12.dp),
            )
        }
    }
}

private fun trimNum(n: Float): String = if (n % 1f == 0f) n.toInt().toString() else n.toString()
