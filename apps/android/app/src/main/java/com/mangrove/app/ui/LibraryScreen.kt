package com.mangrove.app.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ArrowDropDown
import androidx.compose.material3.AssistChip
import androidx.compose.material3.AssistChipDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
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

private val SORT_OPTIONS = listOf(
    "name" to "Name",
    "added" to "Recently added",
    "updated" to "Recently updated",
    "chapters" to "Most chapters",
)

private val STATUS_OPTIONS = listOf(
    "all" to "All",
    "unread" to "Unread",
    "reading" to "In progress",
    "completed" to "Completed",
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LibraryScreen(container: AppContainer, nav: NavController, libraryId: Int) {
    var series by remember { mutableStateOf<List<SeriesDto>?>(null) }
    var title by remember { mutableStateOf("Library") }
    var refreshing by remember { mutableStateOf(false) }
    var firstLoadDone by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    var sort by remember { mutableStateOf(container.prefs.librarySort) }
    var status by remember { mutableStateOf(container.prefs.libraryStatus) }
    var genre by remember { mutableStateOf("") }
    var genres by remember { mutableStateOf<List<String>>(emptyList()) }

    suspend fun fetchSeries() {
        series = runCatching {
            container.series(
                libraryId,
                null,
                sort,
                genre.ifEmpty { null },
                status.takeIf { it != "all" },
            )
        }.getOrDefault(emptyList())
    }

    LaunchedEffect(libraryId) {
        runCatching { container.libraries() }.getOrNull()
            ?.firstOrNull { it.id == libraryId }?.let { title = it.name }
        genres = runCatching { container.libraryGenres(libraryId) }.getOrDefault(emptyList())
    }

    // Refetch whenever the library or any filter changes.
    LaunchedEffect(libraryId, sort, status, genre) {
        series = null
        fetchSeries()
        firstLoadDone = true
    }

    LifecycleResumeRefresh {
        if (firstLoadDone) scope.launch { refreshing = true; fetchSeries(); refreshing = false }
    }
    AutoRefresh {
        if (firstLoadDone) scope.launch { fetchSeries() }
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

        // Browse controls: status filter chips + sort/genre pickers.
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState())
                .padding(horizontal = 12.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            STATUS_OPTIONS.forEach { (value, label) ->
                FilterChip(
                    selected = status == value,
                    onClick = {
                        status = value
                        container.prefs.libraryStatus = value
                    },
                    label = { Text(label) },
                )
            }
        }
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            DropdownPicker(
                label = "Sort",
                options = SORT_OPTIONS,
                selected = sort,
                onSelect = {
                    sort = it
                    container.prefs.librarySort = it
                },
            )
            if (genres.isNotEmpty()) {
                DropdownPicker(
                    label = "Genre",
                    options = listOf("" to "All genres") + genres.map { it to it },
                    selected = genre,
                    onSelect = { genre = it },
                )
            }
        }

        PullToRefreshBox(
            isRefreshing = refreshing,
            onRefresh = { scope.launch { refreshing = true; fetchSeries(); refreshing = false } },
            modifier = Modifier.fillMaxSize(),
        ) {
            val list = series
            when {
                list == null -> LoadingBox()
                list.isEmpty() -> MessageBox(
                    if (status != "all" || genre.isNotEmpty()) "No series match your filters."
                    else "No series in this library yet.",
                )
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
                            readChapters = s.readChapters,
                            chapterCount = s.chapterCount,
                            overlay = true,
                        )
                    }
                }
            }
        }
    }
}

/** A compact label + dropdown menu used for the sort and genre pickers. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun DropdownPicker(
    label: String,
    options: List<Pair<String, String>>,
    selected: String,
    onSelect: (String) -> Unit,
) {
    var expanded by remember { mutableStateOf(false) }
    val current = options.firstOrNull { it.first == selected }?.second ?: label
    Box {
        AssistChip(
            onClick = { expanded = true },
            label = { Text("$label: $current") },
            trailingIcon = {
                Icon(Icons.Filled.ArrowDropDown, contentDescription = null)
            },
            colors = AssistChipDefaults.assistChipColors(),
        )
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            options.forEach { (value, text) ->
                DropdownMenuItem(
                    text = { Text(text) },
                    onClick = {
                        expanded = false
                        onSelect(value)
                    },
                )
            }
        }
    }
}
