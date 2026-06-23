package com.mangrove.app.data

import android.content.Context
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.File

/** Metadata persisted alongside a downloaded chapter's pages (downloads/ch_<id>/meta.json). */
@Serializable
data class DownloadMeta(
    val chapterId: Int,
    val seriesId: Int,
    val seriesName: String,
    val volumeNumber: Float = 0f,
    val number: Float = 0f,
    val title: String? = null,
    val pageCount: Int = 0,
    val readingDirection: String = "ltr",
    val downloadedPages: Int = 0,
    val complete: Boolean = false,
)

/** A downloaded series as shown in the offline library (derived by grouping chapter metas). */
data class DownloadedSeries(
    val seriesId: Int,
    val name: String,
    val chapters: List<DownloadMeta>,
)

/**
 * File-based storage for offline downloads. Each chapter gets a folder with its page images and a
 * meta.json; series covers are cached next to them. No database needed — the index is rebuilt by
 * scanning the downloads directory, which keeps offline mode dead-simple and self-healing.
 */
class DownloadStore(context: Context) {
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true }
    private val root = File(context.filesDir, "downloads")

    fun chapterDir(chapterId: Int) = File(root, "ch_$chapterId")
    fun metaFile(chapterId: Int) = File(chapterDir(chapterId), "meta.json")
    fun pageFile(chapterId: Int, page: Int) = File(chapterDir(chapterId), "p%05d.img".format(page))
    fun seriesCoverFile(seriesId: Int) = File(root, "series_$seriesId.jpg")

    fun readMeta(chapterId: Int): DownloadMeta? {
        val f = metaFile(chapterId)
        if (!f.exists()) return null
        return runCatching { json.decodeFromString<DownloadMeta>(f.readText()) }.getOrNull()
    }

    fun writeMeta(meta: DownloadMeta) {
        chapterDir(meta.chapterId).mkdirs()
        metaFile(meta.chapterId).writeText(json.encodeToString(DownloadMeta.serializer(), meta))
    }

    fun isComplete(chapterId: Int): Boolean = readMeta(chapterId)?.complete == true

    fun allMetas(): List<DownloadMeta> {
        val dirs = root.listFiles { f -> f.isDirectory && f.name.startsWith("ch_") } ?: return emptyList()
        return dirs.mapNotNull { readMeta(it.name.removePrefix("ch_").toIntOrNull() ?: return@mapNotNull null) }
    }

    /** Completed downloads grouped into series, each sorted by volume then chapter number. */
    fun downloadedSeries(): List<DownloadedSeries> =
        allMetas()
            .filter { it.complete }
            .groupBy { it.seriesId }
            .map { (id, metas) ->
                DownloadedSeries(
                    seriesId = id,
                    name = metas.first().seriesName,
                    chapters = metas.sortedWith(compareBy({ it.volumeNumber }, { it.number })),
                )
            }
            .sortedBy { it.name.lowercase() }

    fun hasAnyDownloads(): Boolean = allMetas().any { it.complete }

    fun deleteChapter(chapterId: Int) {
        chapterDir(chapterId).deleteRecursively()
    }

    fun deleteSeries(seriesId: Int) {
        allMetas().filter { it.seriesId == seriesId }.forEach { deleteChapter(it.chapterId) }
        seriesCoverFile(seriesId).delete()
    }

    private fun File.dirSize(): Long =
        if (exists()) walkTopDown().filter { it.isFile }.sumOf { it.length() } else 0L

    /** Total bytes used by all downloads (pages + covers + metadata). */
    fun totalBytes(): Long = root.dirSize()

    /** Bytes used by a single downloaded series (its chapters + cached cover). */
    fun seriesBytes(seriesId: Int): Long {
        val chapters = allMetas().filter { it.seriesId == seriesId }
            .sumOf { chapterDir(it.chapterId).dirSize() }
        val cover = seriesCoverFile(seriesId).let { if (it.exists()) it.length() else 0L }
        return chapters + cover
    }
}
