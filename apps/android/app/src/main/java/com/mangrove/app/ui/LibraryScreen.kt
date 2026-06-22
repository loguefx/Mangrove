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
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.SeriesDto

@Composable
fun LibraryScreen(container: AppContainer, nav: NavController, libraryId: Int) {
    var series by remember { mutableStateOf<List<SeriesDto>?>(null) }
    var title by remember { mutableStateOf("Library") }

    LaunchedEffect(libraryId) {
        runCatching { container.libraries() }.getOrNull()
            ?.firstOrNull { it.id == libraryId }?.let { title = it.name }
        series = runCatching { container.series(libraryId, null) }.getOrDefault(emptyList())
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

        when {
            series == null -> LoadingBox()
            series!!.isEmpty() -> MessageBox("No series in this library yet.")
            else -> LazyVerticalGrid(
                columns = GridCells.Adaptive(minSize = 112.dp),
                contentPadding = PaddingValues(12.dp),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                items(series!!, key = { it.id }) { s ->
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
