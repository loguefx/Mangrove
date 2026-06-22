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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
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
import com.mangrove.app.data.SeriesDetailDto

private data class ChapterRow(val chapter: ChapterDto, val volumeNumber: Float)

@Composable
fun SeriesScreen(container: AppContainer, nav: NavController, seriesId: Int) {
    var detail by remember { mutableStateOf<SeriesDetailDto?>(null) }
    var failed by remember { mutableStateOf(false) }

    LaunchedEffect(seriesId) {
        runCatching { container.seriesDetail(seriesId) }
            .onSuccess { detail = it }
            .onFailure { failed = true }
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
                        SectionTitle("Chapters")
                    }

                    items(rows, key = { it.chapter.id }) { row ->
                        ChapterListItem(row) { nav.navigate("reader/${row.chapter.id}") }
                        HorizontalDivider(color = MaterialTheme.colorScheme.surfaceVariant)
                    }
                }
            }
        }
    }
}

@Composable
private fun ChapterListItem(row: ChapterRow, onClick: () -> Unit) {
    val c = row.chapter
    val label = buildString {
        if (row.volumeNumber > 0f) append("Vol ${trimNum(row.volumeNumber)} · ")
        append("Ch ${trimNum(c.number)}")
        if (!c.title.isNullOrBlank()) append(" — ${c.title}")
    }
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClick = onClick)
            .padding(horizontal = 16.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), maxLines = 2)
        Text(
            "${c.pageCount}p",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(start = 8.dp),
        )
    }
}

private fun trimNum(n: Float): String = if (n % 1f == 0f) n.toInt().toString() else n.toString()
