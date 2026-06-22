package com.mangrove.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.repeatOnLifecycle
import coil.compose.AsyncImage
import kotlinx.coroutines.delay
import coil.request.ImageRequest
import com.mangrove.app.data.AppContainer

/** Loads an image from an API path (e.g. "api/series/3/cover") using the authenticated loader. */
@Composable
fun NetworkImage(
    container: AppContainer,
    path: String,
    contentDescription: String?,
    modifier: Modifier = Modifier,
    contentScale: ContentScale = ContentScale.Crop,
) {
    val loader = container.imageLoader
    val context = LocalContext.current
    if (loader == null) {
        Box(modifier)
        return
    }
    AsyncImage(
        model = ImageRequest.Builder(context).data(container.absoluteUrl(path)).build(),
        contentDescription = contentDescription,
        imageLoader = loader,
        modifier = modifier,
        contentScale = contentScale,
    )
}

/** A poster-style cover with title used across Home/Library/Series. */
@Composable
fun CoverCard(
    container: AppContainer,
    coverPath: String?,
    title: String,
    subtitle: String? = null,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    androidx.compose.foundation.layout.Column(
        modifier = modifier
            .clip(RoundedCornerShape(14.dp))
            .clickable(onClick = onClick)
            .padding(4.dp),
    ) {
        Box(
            Modifier
                .fillMaxWidth()
                .aspectRatio(2f / 3f)
                .clip(RoundedCornerShape(12.dp))
                .background(MaterialTheme.colorScheme.surfaceVariant),
            contentAlignment = Alignment.Center,
        ) {
            if (coverPath != null) {
                NetworkImage(container, coverPath, title, Modifier.fillMaxSize())
            } else {
                Text("No cover", color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        Text(
            title,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(top = 6.dp),
        )
        if (subtitle != null) {
            Text(
                subtitle,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(
        text.uppercase(),
        style = MaterialTheme.typography.labelMedium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = modifier.padding(horizontal = 16.dp, vertical = 8.dp),
    )
}

/** Loads an image from a local file (downloaded page/cover) for fully-offline display. */
@Composable
fun FileImage(
    container: AppContainer,
    file: java.io.File,
    contentDescription: String?,
    modifier: Modifier = Modifier,
    contentScale: ContentScale = ContentScale.Crop,
) {
    val context = LocalContext.current
    val loader = container.imageLoader
    AsyncImage(
        model = ImageRequest.Builder(context).data(file).build(),
        contentDescription = contentDescription,
        imageLoader = loader ?: coil.ImageLoader(context),
        modifier = modifier,
        contentScale = contentScale,
    )
}

@Composable
fun LoadingBox(modifier: Modifier = Modifier) {
    Box(modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        CircularProgressIndicator(color = MaterialTheme.colorScheme.secondary)
    }
}

@Composable
fun MessageBox(text: String, modifier: Modifier = Modifier) {
    Box(modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
        Text(
            text,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth(),
        )
    }
}

/**
 * Invokes [onResume] every time this destination is resumed. Inside a NavHost the
 * LifecycleOwner is the destination's NavBackStackEntry, so this fires on first display AND
 * whenever the user returns to the screen (back navigation or switching bottom-nav tabs) and
 * when the app comes back to the foreground — used to auto-refresh server data.
 */
@Composable
fun LifecycleResumeRefresh(onResume: () -> Unit) {
    val current = rememberUpdatedState(onResume)
    val owner = LocalLifecycleOwner.current
    DisposableEffect(owner) {
        val observer = LifecycleEventObserver { _, event ->
            if (event == Lifecycle.Event.ON_RESUME) current.value()
        }
        owner.lifecycle.addObserver(observer)
        onDispose { owner.lifecycle.removeObserver(observer) }
    }
}

/**
 * Periodically invokes [onTick] while this screen is resumed (foreground + visible), so newly-scanned
 * chapters/series show up automatically without the user pulling to refresh. Ticking pauses when the
 * screen is backgrounded and resumes when it returns.
 */
@Composable
fun AutoRefresh(intervalMs: Long = 45_000L, onTick: () -> Unit) {
    val current = rememberUpdatedState(onTick)
    val owner = LocalLifecycleOwner.current
    LaunchedEffect(owner) {
        owner.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            while (true) {
                delay(intervalMs)
                current.value()
            }
        }
    }
}
