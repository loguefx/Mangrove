package com.mangrove.app.data

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import android.content.pm.ServiceInfo
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.work.CoroutineWorker
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import com.mangrove.app.MangroveApp
import com.mangrove.app.R

/**
 * Drains all pending chapter downloads as a single foreground (data-sync) service so large batches
 * complete even when the app is closed. On transient failures it returns retry() and WorkManager
 * re-runs it (honoring the network constraint), so downloads are effectively unlimited.
 */
class DownloadWorker(
    context: Context,
    params: WorkerParameters,
) : CoroutineWorker(context, params) {

    private val manager get() = (applicationContext as MangroveApp).container.downloadManager
    private val notifier = NotificationManagerCompatFor(applicationContext)

    override suspend fun getForegroundInfo(): ForegroundInfo = foregroundInfo("Preparing downloads…", 0, 0)

    override suspend fun doWork(): Result {
        ensureChannel(applicationContext)
        setForeground(getForegroundInfo())

        var completedChapters = 0
        try {
            while (true) {
                val next = manager.nextPending() ?: break
                val label = buildString {
                    append(next.seriesName)
                    append(" · Ch ")
                    append(if (next.number % 1f == 0f) next.number.toInt().toString() else next.number.toString())
                }
                setForeground(foregroundInfo(label, 0, next.pageCount))
                manager.runDownload(next.chapterId) { _, done, total ->
                    notifier.notify(foregroundNotification(label, done, total))
                }
                completedChapters++
            }
        } catch (e: Exception) {
            // Network/transport blip: let WorkManager retry the whole batch (respects constraints).
            return if (runAttemptCount < MAX_ATTEMPTS) Result.retry() else Result.failure()
        }
        return Result.success()
    }

    private fun foregroundInfo(text: String, done: Int, total: Int): ForegroundInfo {
        val notification = foregroundNotification(text, done, total)
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ForegroundInfo(NOTIF_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            ForegroundInfo(NOTIF_ID, notification)
        }
    }

    private fun foregroundNotification(text: String, done: Int, total: Int): Notification {
        val builder = NotificationCompat.Builder(applicationContext, CHANNEL_ID)
            .setContentTitle("Downloading for offline")
            .setContentText(text)
            .setSmallIcon(R.drawable.ic_launcher_foreground)
            .setOngoing(true)
            .setOnlyAlertOnce(true)
        if (total > 0) builder.setProgress(total, done, false)
        return builder.build()
    }

    companion object {
        private const val CHANNEL_ID = "downloads"
        private const val NOTIF_ID = 4711
        private const val MAX_ATTEMPTS = 10

        fun ensureChannel(context: Context) {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
            val mgr = context.getSystemService(NotificationManager::class.java)
            if (mgr.getNotificationChannel(CHANNEL_ID) == null) {
                mgr.createNotificationChannel(
                    NotificationChannel(CHANNEL_ID, "Downloads", NotificationManager.IMPORTANCE_LOW).apply {
                        description = "Offline download progress"
                    },
                )
            }
        }
    }

    /** Thin wrapper so we can post progress updates to the same notification id as the FGS. */
    private class NotificationManagerCompatFor(private val context: Context) {
        private val mgr = context.getSystemService(NotificationManager::class.java)
        fun notify(notification: Notification) = mgr.notify(NOTIF_ID, notification)
    }
}
