package com.mangrove.app.data

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import java.util.concurrent.ConcurrentHashMap

data class DownloadRequest(
    val chapterId: Int,
    val seriesId: Int,
    val seriesName: String,
    val volumeNumber: Float,
    val number: Float,
    val title: String?,
)

enum class DownloadState { Queued, Running, Done, Failed }

data class DownloadProgress(
    val chapterId: Int,
    val done: Int,
    val total: Int,
    val state: DownloadState,
)

/**
 * In-process download worker: page images are fetched (with auth) and written to the DownloadStore,
 * one chapter at a time. Incomplete downloads resume automatically on launch. Progress is published
 * as a StateFlow the UI observes. (A future enhancement is WorkManager for true background work.)
 */
class DownloadManager(
    private val app: AppContainer,
    private val store: DownloadStore,
    private val scope: CoroutineScope,
) {
    private val _progress = MutableStateFlow<Map<Int, DownloadProgress>>(emptyMap())
    val progress: StateFlow<Map<Int, DownloadProgress>> = _progress

    private val seeds = ConcurrentHashMap<Int, DownloadRequest>()
    private val queued = ConcurrentHashMap.newKeySet<Int>()
    private val queue = Channel<Int>(Channel.UNLIMITED)

    init {
        scope.launch {
            for (chapterId in queue) {
                runCatching { downloadChapter(chapterId) }
                    .onFailure { setProgress(chapterId) { it.copy(state = DownloadState.Failed) } }
                queued.remove(chapterId)
            }
        }
    }

    fun enqueue(req: DownloadRequest) {
        if (store.isComplete(req.chapterId)) return
        if (!queued.add(req.chapterId)) return
        seeds[req.chapterId] = req
        update(req.chapterId, DownloadProgress(req.chapterId, 0, 0, DownloadState.Queued))
        queue.trySend(req.chapterId)
    }

    fun isQueuedOrRunning(chapterId: Int): Boolean {
        val state = _progress.value[chapterId]?.state
        return state == DownloadState.Queued || state == DownloadState.Running
    }

    /** Re-queues any chapters whose download was interrupted. Call once the backend is ready. */
    fun resume() {
        store.allMetas().filter { !it.complete }.forEach { meta ->
            enqueue(
                DownloadRequest(
                    chapterId = meta.chapterId,
                    seriesId = meta.seriesId,
                    seriesName = meta.seriesName,
                    volumeNumber = meta.volumeNumber,
                    number = meta.number,
                    title = meta.title,
                ),
            )
        }
    }

    private suspend fun downloadChapter(chapterId: Int) {
        val seed = seeds[chapterId] ?: store.readMeta(chapterId)?.let {
            DownloadRequest(it.chapterId, it.seriesId, it.seriesName, it.volumeNumber, it.number, it.title)
        } ?: return

        update(chapterId, DownloadProgress(chapterId, 0, 0, DownloadState.Running))

        val manifest = app.manifest(chapterId)
        if (!manifest.mediaType.equals("image", ignoreCase = true) || manifest.pageCount <= 0) {
            // EPUB/unsupported in the image reader — mark failed so the UI can show it.
            update(chapterId, DownloadProgress(chapterId, 0, manifest.pageCount, DownloadState.Failed))
            return
        }

        var meta = DownloadMeta(
            chapterId = chapterId,
            seriesId = seed.seriesId,
            seriesName = seed.seriesName,
            volumeNumber = seed.volumeNumber,
            number = seed.number,
            title = seed.title,
            pageCount = manifest.pageCount,
            readingDirection = manifest.readingDirection,
            downloadedPages = 0,
            complete = false,
        )
        store.writeMeta(meta)

        // Cache the series cover for the offline library (best-effort).
        val coverFile = store.seriesCoverFile(seed.seriesId)
        if (!coverFile.exists()) {
            app.fetchBytes("api/series/${seed.seriesId}/cover")?.let { coverFile.writeBytes(it) }
        }

        var done = 0
        for (n in 0 until manifest.pageCount) {
            val pageFile = store.pageFile(chapterId, n)
            if (!pageFile.exists() || pageFile.length() == 0L) {
                val bytes = app.fetchBytes("api/chapters/$chapterId/pages/$n")
                    ?: throw IllegalStateException("Failed to download page $n of chapter $chapterId")
                pageFile.writeBytes(bytes)
            }
            done = n + 1
            if (done % 3 == 0 || done == manifest.pageCount) {
                meta = meta.copy(downloadedPages = done)
                store.writeMeta(meta)
                update(chapterId, DownloadProgress(chapterId, done, manifest.pageCount, DownloadState.Running))
            }
        }

        store.writeMeta(meta.copy(downloadedPages = done, complete = true))
        update(chapterId, DownloadProgress(chapterId, done, manifest.pageCount, DownloadState.Done))
    }

    private fun update(chapterId: Int, progress: DownloadProgress) {
        _progress.value = _progress.value.toMutableMap().apply { put(chapterId, progress) }
    }

    private fun setProgress(chapterId: Int, transform: (DownloadProgress) -> DownloadProgress) {
        val current = _progress.value[chapterId] ?: DownloadProgress(chapterId, 0, 0, DownloadState.Queued)
        update(chapterId, transform(current))
    }
}
