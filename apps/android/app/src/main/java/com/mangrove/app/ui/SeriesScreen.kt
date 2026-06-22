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
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.ChapterDto
import com.mangrove.app.data.DownloadProgress
import com.mangrove.app.data.DownloadRequest
import com.mangrove.app.data.DownloadState
import com.mangrove.app.data.SeriesDetailDto
import com.mangrove.app.ui.theme.TealMint

private data class ChapterRow(val chapter: ChapterDto, val volumeNumber: Float)

private fun isEpub(format: String) = format.equals("epub", ignoreCase = true)

@Composable
fun SeriesScreen(container: AppContainer, nav: NavController, seriesId: Int) {
    var detail by remember { mutableStateOf<SeriesDetailDto?>(null) }
    var failed by remember { mutableStateOf(false) }
    val progress by container.downloadManager.progress.collectAsState()
    var downloadedIds by remember { mutableStateOf<Set<Int>>(emptySet()) }

    LaunchedEffect(seriesId) {
        runCatching { container.seriesDetail(seriesId) }
            .onSuccess { detail = it }
            .onFailure { failed = true }
    }
    // Refresh the "downloaded" set whenever a download completes.
    LaunchedEffect(progress) {
        downloadedIds = container.downloadStore.allMetas().filter { it.complete }.map { it.chapterId }.toSet()
    }

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
            )
        }

        when {
            failed -> MessageBox("Couldn't load this series.")
            detail == null -> LoadingBox()
            else -> {
                val d = detail!!
                val rows = d.volumes
                    .sortedBy { it.number }
                    .flatMap { v -> v.chapters.sortedBy { it.number }.map { ChapterRow(it, v.number) } }

                LazyColumn(Modifier.fillMaxSize()) {
                    item {
                        Row(Modifier.fillMaxWidth().padding(16.dp)) {
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
                            Column(Modifier.padding(start = 16.dp)) {
                                d.publisher?.takeIf { it.isNotBlank() }?.let {
                                    Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                                d.genres?.takeIf { it.isNotBlank() }?.let {
                                    Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp))
                                }
                                Text("${rows.size} chapters", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(top = 8.dp))
                            }
                        }
                        d.summary?.takeIf { it.isNotBlank() }?.let {
                            Text(it, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.padding(horizontal = 16.dp), color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                            SectionTitle("Chapters")
                            androidx.compose.foundation.layout.Spacer(Modifier.weight(1f))
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
            .padding(start = 16.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), maxLines = 2)
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
