package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.DashboardDto
import com.mangrove.app.data.LibraryDto
import com.mangrove.app.ui.theme.TealMint
import kotlinx.coroutines.launch

@Composable
fun HomeScreen(container: AppContainer, nav: NavController) {
    var dashboard by remember { mutableStateOf<DashboardDto?>(null) }
    var libraries by remember { mutableStateOf<List<LibraryDto>>(emptyList()) }
    var loading by remember { mutableStateOf(true) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) {
        dashboard = runCatching { container.dashboard() }.getOrNull()
        libraries = runCatching { container.libraries() }.getOrDefault(emptyList())
        loading = false
    }

    Column(Modifier.fillMaxSize()) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text("Mangrove", fontSize = 22.sp, fontWeight = FontWeight.SemiBold)
            TextButton(onClick = {
                scope.launch {
                    container.logout()
                    nav.navigate("login") { popUpTo("home") { inclusive = true } }
                }
            }) { Text("Sign out", color = TealMint) }
        }

        if (loading) {
            LoadingBox()
            return@Column
        }

        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 24.dp)) {
            if (libraries.isNotEmpty()) {
                SectionTitle("Libraries")
                LazyRow(
                    contentPadding = PaddingValues(horizontal = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    items(libraries, key = { it.id }) { lib ->
                        Box(
                            Modifier
                                .clip(RoundedCornerShape(14.dp))
                                .background(MaterialTheme.colorScheme.surface)
                                .clickable { nav.navigate("library/${lib.id}") }
                                .padding(horizontal = 18.dp, vertical = 14.dp),
                        ) {
                            Column {
                                Text(lib.name, fontWeight = FontWeight.Medium)
                                Text(
                                    "${lib.seriesCount} series",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    }
                }
            }

            val cont = dashboard?.continueReading ?: emptyList()
            if (cont.isNotEmpty()) {
                SectionTitle("Continue reading")
                LazyRow(
                    contentPadding = PaddingValues(horizontal = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    items(cont, key = { it.chapterId }) { c ->
                        CoverCard(
                            container = container,
                            coverPath = if (c.hasCover) "api/chapters/${c.chapterId}/cover" else null,
                            title = c.seriesName,
                            subtitle = if (c.pageCount > 0) "Page ${c.page + 1}/${c.pageCount}" else "In progress",
                            onClick = { nav.navigate("reader/${c.chapterId}") },
                            modifier = Modifier.width(124.dp),
                        )
                    }
                }
            }

            val recent = dashboard?.recentlyAdded ?: emptyList()
            if (recent.isNotEmpty()) {
                SectionTitle("Recently added")
                LazyRow(
                    contentPadding = PaddingValues(horizontal = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                ) {
                    items(recent, key = { it.id }) { s ->
                        CoverCard(
                            container = container,
                            coverPath = if (s.hasCover) "api/series/${s.id}/cover" else null,
                            title = s.name,
                            onClick = { nav.navigate("series/${s.id}") },
                            modifier = Modifier.width(124.dp),
                        )
                    }
                }
            }

            if (libraries.isEmpty() && cont.isEmpty() && recent.isEmpty()) {
                MessageBox("Nothing here yet. Add and scan a library from the web app.", Modifier.height(300.dp))
            }
        }
    }
}
