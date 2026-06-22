package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import androidx.navigation.NavController
import com.mangrove.app.data.AppContainer
import com.mangrove.app.data.ChapterManifestDto
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.launch

@Composable
fun ReaderScreen(container: AppContainer, nav: NavController, chapterId: Int) {
    var manifest by remember { mutableStateOf<ChapterManifestDto?>(null) }
    var ready by remember { mutableStateOf(false) }
    var offline by remember { mutableStateOf(false) }
    var rtl by remember { mutableStateOf(false) }
    var dirPref by remember { mutableStateOf("auto") }
    var startPage by remember { mutableIntStateOf(0) }
    var menuOpen by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(chapterId) {
        // Prefer locally downloaded pages so the reader works with no connection.
        val localMeta = container.downloadStore.readMeta(chapterId)?.takeIf { it.complete }
        offline = localMeta != null
        val m = if (localMeta != null) {
            ChapterManifestDto(chapterId, localMeta.pageCount, localMeta.readingDirection, "", "image")
        } else {
            runCatching { container.manifest(chapterId) }.getOrNull()
        }
        manifest = m
        if (m != null) {
            val prefsMap = runCatching { container.getPreferences() }.getOrNull()
            dirPref = prefsMap?.get("reader.dir") ?: container.prefs.readerDir ?: "auto"
            rtl = directionToRtl(dirPref, m.readingDirection)
            val saved = container.progress(chapterId)?.page ?: 0
            startPage = saved.coerceIn(0, (m.pageCount - 1).coerceAtLeast(0))
        }
        ready = true
    }

    if (!ready) {
        Box(Modifier.fillMaxSize().background(Color.Black)) { LoadingBox() }
        return
    }
    val m = manifest
    if (m == null || m.pageCount <= 0) {
        Box(Modifier.fillMaxSize().background(Color.Black)) {
            MessageBox("This chapter can't be opened in the image reader.")
        }
        return
    }

    val pagerState = rememberPagerState(initialPage = startPage.coerceIn(0, m.pageCount - 1)) { m.pageCount }

    // Pinch-to-zoom / pan state for the current page. Reset when the page changes.
    var scale by remember { mutableFloatStateOf(1f) }
    var offset by remember { mutableStateOf(Offset.Zero) }
    LaunchedEffect(pagerState.currentPage) {
        scale = 1f
        offset = Offset.Zero
    }

    // Persist progress whenever the settled page changes.
    LaunchedEffect(pagerState, chapterId) {
        snapshotFlow { pagerState.currentPage }
            .distinctUntilChanged()
            .collect { page -> container.saveProgress(chapterId, page) }
    }

    Box(Modifier.fillMaxSize().background(Color.Black)) {
        HorizontalPager(
            state = pagerState,
            reverseLayout = rtl,
            // While zoomed in, lock paging so horizontal drags pan the page instead.
            userScrollEnabled = scale <= 1.01f,
            modifier = Modifier.fillMaxSize(),
        ) { page ->
            ZoomableImage(
                scale = scale,
                offset = offset,
                onChange = { s, o -> scale = s; offset = o },
            ) {
                if (offline) {
                    FileImage(
                        container = container,
                        file = container.downloadStore.pageFile(chapterId, page),
                        contentDescription = "Page ${page + 1}",
                        modifier = Modifier.fillMaxSize(),
                        contentScale = ContentScale.Fit,
                    )
                } else {
                    NetworkImage(
                        container = container,
                        path = "api/chapters/$chapterId/pages/$page",
                        contentDescription = "Page ${page + 1}",
                        modifier = Modifier.fillMaxSize(),
                        contentScale = ContentScale.Fit,
                    )
                }
            }
        }

        // Top chrome: back, page indicator, direction menu.
        Row(
            Modifier
                .fillMaxWidth()
                .background(Color(0xCC000000))
                .padding(horizontal = 4.dp, vertical = 2.dp)
                .align(Alignment.TopCenter),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = { nav.popBackStack() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back", tint = Color.White)
            }
            Text(
                "Page ${pagerState.currentPage + 1} / ${m.pageCount} · ${if (rtl) "RTL" else "LTR"}" +
                    if (offline) " · Offline" else "",
                color = Color.White,
                modifier = Modifier.weight(1f),
            )
            Box {
                IconButton(onClick = { menuOpen = true }) {
                    Icon(Icons.Filled.Settings, contentDescription = "Reading settings", tint = Color.White)
                }
                DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                    DirectionItem("Right to left (manga)", "rtl", dirPref) { choose ->
                        applyDirection(choose, m, container, scope) { newPref, newRtl ->
                            dirPref = newPref; rtl = newRtl; menuOpen = false
                        }
                    }
                    DirectionItem("Left to right", "ltr", dirPref) { choose ->
                        applyDirection(choose, m, container, scope) { newPref, newRtl ->
                            dirPref = newPref; rtl = newRtl; menuOpen = false
                        }
                    }
                    DirectionItem("Match series", "auto", dirPref) { choose ->
                        applyDirection(choose, m, container, scope) { newPref, newRtl ->
                            dirPref = newPref; rtl = newRtl; menuOpen = false
                        }
                    }
                }
            }
        }
    }
}

/**
 * Wraps a page image with pinch-to-zoom, drag-to-pan, and double-tap-to-toggle-zoom.
 * Zoom/pan state is hoisted so the pager can disable swiping while the page is zoomed.
 */
@Composable
private fun ZoomableImage(
    scale: Float,
    offset: Offset,
    onChange: (Float, Offset) -> Unit,
    content: @Composable () -> Unit,
) {
    val curScale by rememberUpdatedState(scale)
    val curOffset by rememberUpdatedState(offset)
    BoxWithConstraints(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        val maxW = constraints.maxWidth.toFloat()
        val maxH = constraints.maxHeight.toFloat()
        Box(
            Modifier
                .fillMaxSize()
                .graphicsLayer {
                    scaleX = scale
                    scaleY = scale
                    translationX = offset.x
                    translationY = offset.y
                }
                .pointerInput(Unit) {
                    detectTransformGestures { _, pan, zoom, _ ->
                        val newScale = (curScale * zoom).coerceIn(1f, 5f)
                        val newOffset = if (newScale <= 1f) {
                            Offset.Zero
                        } else {
                            val maxX = maxW * (newScale - 1f) / 2f
                            val maxY = maxH * (newScale - 1f) / 2f
                            Offset(
                                (curOffset.x + pan.x).coerceIn(-maxX, maxX),
                                (curOffset.y + pan.y).coerceIn(-maxY, maxY),
                            )
                        }
                        onChange(newScale, newOffset)
                    }
                }
                .pointerInput(Unit) {
                    detectTapGestures(
                        onDoubleTap = {
                            if (curScale > 1f) onChange(1f, Offset.Zero) else onChange(2.5f, Offset.Zero)
                        },
                    )
                },
            contentAlignment = Alignment.Center,
        ) { content() }
    }
}

@Composable
private fun DirectionItem(label: String, value: String, current: String, onPick: (String) -> Unit) {
    DropdownMenuItem(
        text = { Text(if (current == value) "✓ $label" else label) },
        onClick = { onPick(value) },
    )
}

private fun applyDirection(
    value: String,
    manifest: ChapterManifestDto,
    container: AppContainer,
    scope: kotlinx.coroutines.CoroutineScope,
    update: (String, Boolean) -> Unit,
) {
    update(value, directionToRtl(value, manifest.readingDirection))
    scope.launch { container.savePreference("reader.dir", value) }
}

private fun directionToRtl(pref: String, manifestDirection: String): Boolean = when (pref) {
    "rtl" -> true
    "ltr" -> false
    else -> manifestDirection.equals("rtl", ignoreCase = true)
}
