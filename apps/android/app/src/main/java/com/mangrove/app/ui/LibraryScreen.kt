package com.mangrove.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.SeriesDto
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LibraryScreen(container: AppContainer, nav: NavController, libraryId: Int) {
    var series by remember { mutableStateOf<List<SeriesDto>?>(null) }
    var title by remember { mutableStateOf("Library") }
    var refreshing by remember { mutableStateOf(false) }
    var firstLoadDone by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    suspend fun fetch() {
        runCatching { container.libraries() }.getOrNull()
            ?.firstOrNull { it.id == libraryId }?.let { title = it.name }
        series = runCatching { container.series(libraryId, null) }.getOrDefault(emptyList())
    }

    LaunchedEffect(libraryId) {
        fetch()
        firstLoadDone = true
    }
    // Auto-refresh when returning to this library so newly scanned series appear.
    LifecycleResumeRefresh {
        if (firstLoadDone) scope.launch { refreshing = true; fetch(); refreshing = false }
    }

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { nav.popBackStack() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
            }
            Text(title, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
        }

        PullToRefreshBox(
            isRefreshing = refreshing,
            onRefresh = { scope.launch { refreshing = true; fetch(); refreshing = false } },
            modifier = Modifier.fillMaxSize(),
        ) {
            val list = series
            when {
                list == null -> LoadingBox()
                list.isEmpty() -> MessageBox("No series in this library yet.")
                else -> LazyVerticalGrid(
                    columns = GridCells.Adaptive(minSize = 112.dp),
                    contentPadding = PaddingValues(12.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                    modifier = Modifier.fillMaxSize(),
                ) {
                    items(list, key = { it.id }) { s ->
                        CoverCard(
                            container = container,
                            coverPath = if (s.hasCover) "api/series/${s.id}/cover" else null,
                            title = s.name,
                            subtitle = "${s.chapterCount} ch",
                            onClick = { nav.navigate("series/${s.id}") },
                        )
                    }
                }
            }
        }
    }
}
