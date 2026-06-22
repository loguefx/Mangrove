package com.mangrove.app.data

import android.content.Context
import androidx.work.Constraints
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.workDataOf
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

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
 * Schedules and tracks offline downloads. Actual work runs in [DownloadWorker] via WorkManager, so
 * it continues in the background (foreground service) even if the app is closed, survives reboots,
 * and is unlimited — you can queue as many chapters/series as you want. Files are written only to
 * the app's private storage (DownloadStore), never to the gallery or shared storage.
 */
class DownloadManager(
    private val app: AppContainer,
    private val context: Context,
    private val store: DownloadStore,
    @Suppress("unused") private val scope: CoroutineScope,
) {
    private val wm = WorkManager.getInstance(context)

    private val _progress = MutableStateFlow<Map<Int, DownloadProgress>>(emptyMap())
    val progress: StateFlow<Map<Int, DownloadProgress>> = _progress.asStateFlow()

    fun enqueue(req: DownloadRequest) {
        if (store.isComplete(req.chapterId)) return

        // Seed metadata so the worker knows the series info even before fetching the manifest.
        val existing = store.readMeta(req.chapterId)
        val seed = (existing ?: DownloadMeta(chapterId = req.chapterId, seriesId = req.seriesId, seriesName = req.seriesName))
            .copy(
                seriesId = req.seriesId,
                seriesName = req.seriesName,
                volumeNumber = req.volumeNumber,
                number = req.number,
                title = req.title,
                complete = false,
            )
        store.writeMeta(seed)

        update(req.chapterId, DownloadProgress(req.chapterId, seed.downloadedPages, seed.pageCount, DownloadState.Queued))
        startWorker(ExistingWorkPolicy.KEEP)
    }

    /** Re-queues any interrupted downloads. Safe to call on launch / after the server is set. */
    fun resume() {
        if (store.allMetas().any { !it.complete }) startWorker(ExistingWorkPolicy.KEEP)
    }

    /** Apply a changed Wi-Fi-only setting to the in-flight batch. */
    fun rescheduleForConstraintChange() {
        if (store.allMetas().any { !it.complete }) startWorker(ExistingWorkPolicy.REPLACE)
    }

    fun isQueuedOrRunning(chapterId: Int): Boolean {
        val state = _progress.value[chapterId]?.state
        return state == DownloadState.Queued || state == DownloadState.Running
    }

    private fun startWorker(policy: ExistingWorkPolicy) {
        val constraints = Constraints.Builder()
            .setRequiredNetworkType(if (app.prefs.wifiOnly) NetworkType.UNMETERED else NetworkType.CONNECTED)
            .build()
        val request = OneTimeWorkRequestBuilder<DownloadWorker>()
            .setConstraints(constraints)
            .addTag(TAG)
            .build()
        wm.enqueueUniqueWork(WORK_NAME, policy, request)
    }

    // ---- Called by DownloadWorker ----

    /** The next chapter that still needs downloading, or null when the batch is done. */
    fun nextPending(): DownloadMeta? = store.allMetas().firstOrNull { !it.complete }

    internal fun update(chapterId: Int, progress: DownloadProgress) {
        _progress.value = _progress.value.toMutableMap().apply { put(chapterId, progress) }
    }

    /**
     * Downloads one chapter's pages. Throws on network/transport failure so the worker can retry.
     * [onProgress] is invoked as pages complete (for the notification).
     */
    suspend fun runDownload(chapterId: Int, onProgress: (DownloadMeta, Int, Int) -> Unit = { _, _, _ -> }) {
        val seed = store.readMeta(chapterId) ?: return
        if (seed.complete) return

        update(chapterId, DownloadProgress(chapterId, seed.downloadedPages, seed.pageCount, DownloadState.Running))

        val manifest = app.manifest(chapterId)
        if (!manifest.mediaType.equals("image", ignoreCase = true) || manifest.pageCount <= 0) {
            // EPUB/unsupported in the image reader: mark complete=false but skip so it won't block the batch.
            store.deleteChapter(chapterId)
            update(chapterId, DownloadProgress(chapterId, 0, manifest.pageCount, DownloadState.Failed))
            return
        }

        var meta = seed.copy(
            pageCount = manifest.pageCount,
            readingDirection = manifest.readingDirection,
            complete = false,
        )
        store.writeMeta(meta)

        val coverFile = store.seriesCoverFile(meta.seriesId)
        if (!coverFile.exists()) {
            app.fetchBytes("api/series/${meta.seriesId}/cover")?.let { coverFile.writeBytes(it) }
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
                onProgress(meta, done, manifest.pageCount)
            }
        }

        store.writeMeta(meta.copy(downloadedPages = done, complete = true))
        update(chapterId, DownloadProgress(chapterId, done, manifest.pageCount, DownloadState.Done))
    }

    companion object {
        const val WORK_NAME = "mangrove-downloads"
        const val TAG = "mangrove-download"
    }
}
