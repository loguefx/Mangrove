package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3Api
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.FavoriteUnread
import com.mangrove.app.data.SeriesDto
import com.mangrove.app.ui.theme.BgDark
import com.mangrove.app.ui.theme.TealDeep
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FavoritesScreen(container: AppContainer, nav: NavController) {
    var favorites by remember { mutableStateOf<List<SeriesDto>>(emptyList()) }
    var unread by remember { mutableStateOf<List<FavoriteUnread>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    var refreshing by remember { mutableStateOf(false) }
    var firstLoadDone by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    suspend fun fetch() {
        favorites = runCatching { container.wantToRead() }.getOrDefault(emptyList())
        unread = runCatching { container.favoritesUnread() }.getOrDefault(emptyList())
    }

    LaunchedEffect(Unit) {
        fetch()
        loading = false
        firstLoadDone = true
    }
    LifecycleResumeRefresh { if (firstLoadDone) scope.launch { refreshing = true; fetch(); refreshing = false } }
    AutoRefresh { if (firstLoadDone) scope.launch { fetch() } }

    Column(
        Modifier
            .fillMaxSize()
            .background(Brush.verticalGradient(listOf(BgDark, TealDeep))),
    ) {
        Box(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 16.dp)) {
            Text("Favorites", fontSize = 24.sp, fontWeight = FontWeight.SemiBold)
        }

        PullToRefreshBox(
            isRefreshing = refreshing,
            onRefresh = { scope.launch { refreshing = true; fetch(); refreshing = false } },
            modifier = Modifier.fillMaxSize(),
        ) {
            if (loading) {
                LoadingBox()
                return@PullToRefreshBox
            }
            if (favorites.isEmpty() && unread.isEmpty()) {
                MessageBox(
                    "No favorites yet. Open any series and tap the star to add it here.",
                    Modifier.height(300.dp),
                )
                return@PullToRefreshBox
            }

            val newBySeries = unread.associateBy { it.seriesId }

            LazyVerticalGrid(
                columns = GridCells.Fixed(3),
                contentPadding = PaddingValues(horizontal = 8.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.spacedBy(4.dp),
            ) {
                if (unread.isNotEmpty()) {
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        SectionTitle("Catch up — new in favorites")
                    }
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        LazyRow(
                            contentPadding = PaddingValues(horizontal = 4.dp),
                            horizontalArrangement = Arrangement.spacedBy(4.dp),
                        ) {
                            items(unread, key = { it.seriesId }) { c ->
                                Box(Modifier.width(124.dp)) {
                                    CoverCard(
                                        container = container,
                                        coverPath = if (c.hasCover) "api/series/${c.seriesId}/cover" else null,
                                        title = c.seriesName,
                                        subtitle = "Ch. ${fmtNum(c.nextChapterNumber)}",
                                        onClick = { nav.navigate("reader/${c.nextChapterId}") },
                                        modifier = Modifier.fillMaxWidth(),
                                    )
                                    UnreadBadge(c.newChapters, Modifier.align(Alignment.TopEnd))
                                }
                            }
                        }
                    }
                    item(span = { GridItemSpan(maxLineSpan) }) {
                        SectionTitle("All favorites")
                    }
                }

                items(favorites, key = { it.id }) { s ->
                    Box {
                        CoverCard(
                            container = container,
                            coverPath = if (s.hasCover) "api/series/${s.id}/cover" else null,
                            title = s.name,
                            onClick = { nav.navigate("series/${s.id}") },
                            overlay = true,
                        )
                        newBySeries[s.id]?.let { UnreadBadge(it.newChapters, Modifier.align(Alignment.TopEnd)) }
                    }
                }
            }
        }
    }
}

@Composable
fun UnreadBadge(count: Int, modifier: Modifier = Modifier) {
    if (count <= 0) return
    Box(
        modifier
            .padding(8.dp)
            .clip(RoundedCornerShape(50))
            .background(Color(0xFFE11D48))
            .padding(horizontal = 7.dp, vertical = 2.dp),
    ) {
        Text(
            if (count > 99) "99+" else count.toString(),
            color = Color.White,
            fontSize = 11.sp,
            fontWeight = FontWeight.SemiBold,
        )
    }
}

/** Formats a chapter number without a trailing ".0" (e.g. 12.0 -> "12", 12.5 -> "12.5"). */
fun fmtNum(n: Float): String =
    if (n % 1f == 0f) n.toInt().toString() else n.toString()
