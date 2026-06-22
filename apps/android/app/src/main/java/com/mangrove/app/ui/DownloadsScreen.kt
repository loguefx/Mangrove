package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items as gridItems
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.DownloadMeta

@Composable
fun DownloadsScreen(container: AppContainer, nav: NavController) {
    val progress by container.downloadManager.progress.collectAsState()
    var refreshKey by remember { mutableStateOf(0) }
    val series = remember(progress, refreshKey) { container.downloadStore.downloadedSeries() }
    var selectedId by remember { mutableStateOf<Int?>(null) }
    val selected = series.firstOrNull { it.seriesId == selectedId }

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { if (selectedId != null) selectedId = null else nav.popBackStack() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
            }
            Text(selected?.name ?: "Downloads", fontSize = 20.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
        }

        if (selectedId == null) {
            var wifiOnly by remember { mutableStateOf(container.prefs.wifiOnly) }
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("Download on Wi-Fi only", modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
                androidx.compose.material3.Switch(
                    checked = wifiOnly,
                    onCheckedChange = {
                        wifiOnly = it
                        container.prefs.wifiOnly = it
                        container.downloadManager.rescheduleForConstraintChange()
                    },
                )
            }
        }

        when {
            series.isEmpty() ->
                MessageBox("No downloads yet. Open a series and tap the download icon to save chapters for offline reading.")

            selected == null -> LazyVerticalGrid(
                columns = GridCells.Adaptive(minSize = 112.dp),
                contentPadding = PaddingValues(12.dp),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                gridItems(series, key = { it.seriesId }) { s ->
                    Column(
                        Modifier
                            .clip(RoundedCornerShape(14.dp))
                            .clickable { selectedId = s.seriesId }
                            .padding(4.dp),
                    ) {
                        val cover = container.downloadStore.seriesCoverFile(s.seriesId)
                        Box(
                            Modifier
                                .fillMaxWidth()
                                .aspectRatio(2f / 3f)
                                .clip(RoundedCornerShape(12.dp))
                                .background(MaterialTheme.colorScheme.surfaceVariant),
                            contentAlignment = Alignment.Center,
                        ) {
                            if (cover.exists()) {
                                FileImage(container, cover, s.name, Modifier.fillMaxSize())
                            } else {
                                Text("No cover", color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                        Text(
                            s.name,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                            style = MaterialTheme.typography.bodyMedium,
                            modifier = Modifier.padding(top = 6.dp),
                        )
                        Text(
                            "${s.chapters.size} ch · offline",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
            }

            else -> LazyColumn(Modifier.fillMaxSize()) {
                items(selected.chapters, key = { it.chapterId }) { meta ->
                    DownloadedChapterRow(
                        meta = meta,
                        onOpen = { nav.navigate("reader/${meta.chapterId}") },
                        onDelete = {
                            container.downloadStore.deleteChapter(meta.chapterId)
                            refreshKey++
                            if (container.downloadStore.downloadedSeries().none { it.seriesId == selectedId }) {
                                selectedId = null
                            }
                        },
                    )
                    HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
                }
            }
        }
    }
}

@Composable
private fun DownloadedChapterRow(meta: DownloadMeta, onOpen: () -> Unit, onDelete: () -> Unit) {
    val label = buildString {
        if (meta.volumeNumber > 0f) append("Vol ${trimNumber(meta.volumeNumber)} · ")
        append("Ch ${trimNumber(meta.number)}")
        if (!meta.title.isNullOrBlank()) append(" — ${meta.title}")
    }
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onOpen).padding(start = 16.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), maxLines = 2)
        Text(
            "${meta.pageCount}p",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(horizontal = 8.dp),
        )
        IconButton(onClick = onDelete) {
            Icon(Icons.Filled.Delete, contentDescription = "Remove download", tint = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

private fun trimNumber(n: Float): String = if (n % 1f == 0f) n.toInt().toString() else n.toString()
