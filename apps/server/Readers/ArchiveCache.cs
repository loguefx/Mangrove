using System.Collections.Concurrent;
using Mangrove.Server.Storage;
using SharpCompress.Archives;

namespace Mangrove.Server.Readers;

/// <summary>
/// Keeps recently-read comic archives (CBZ/ZIP) buffered in memory so that reading a chapter pulls
/// the file from the NAS at most once instead of once per page. To avoid making the very first page
/// wait for the whole (possibly large) archive to download, a single requested page is also served
/// directly via random access from the seekable storage stream — reading just that entry — while the
/// full archive finishes buffering in the background for instant subsequent page turns. Concurrent
/// requests share a single background download, bounded by a byte budget with LRU eviction.
/// </summary>
public sealed class ArchiveCache
{
    private sealed class Item
    {
        public required Lazy<Task<byte[]>> Data;
        public IReadOnlyList<string>? Keys;
        public long LastAccess;
        public long Size;
    }

    private readonly ConcurrentDictionary<string, Item> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _evictLock = new();

    // Hold up to this many bytes of archives in RAM before evicting the oldest.
    private const long MaxBytes = 512L * 1024 * 1024;

    private Item GetItem(string path, IStorageProvider provider)
    {
        var item = _map.GetOrAdd(path, p => new Item
        {
            // The background download isn't tied to any single request's cancellation token, so a
            // cancelled prefetch (or the user navigating away) can't poison the shared archive bytes.
            Data = new Lazy<Task<byte[]>>(
                () => DownloadAsync(p, provider), LazyThreadSafetyMode.ExecutionAndPublication),
            LastAccess = DateTime.UtcNow.Ticks,
        });
        item.LastAccess = DateTime.UtcNow.Ticks;
        return item;
    }

    /// <summary>
    /// Starts buffering the archive in the background (best-effort, fire-and-forget) so that page
    /// reads which arrive shortly after are served from RAM. Safe to call repeatedly.
    /// </summary>
    public void Warm(string path, IStorageProvider provider)
    {
        var item = GetItem(path, provider);
        var task = item.Data.Value;
        // Observe faults so a failed warm-up doesn't surface as an unobserved task exception.
        _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>Reads a single page (0-based, natural order).</summary>
    public async Task<(byte[] Bytes, string ContentType)?> ReadPageAsync(
        string path, IStorageProvider provider, int index, CancellationToken ct)
    {
        var item = GetItem(path, provider);
        var dataTask = item.Data.Value; // ensures the background download is running

        // Fast path: the whole archive is already buffered in RAM.
        if (dataTask.IsCompletedSuccessfully)
        {
            var data = dataTask.Result;
            item.Size = data.Length;
            EvictIfNeeded(path);
            return ExtractFromBytes(data, item, index);
        }

        // Otherwise serve just this page directly from the (seekable) storage stream so the reader
        // doesn't have to wait for the entire archive to finish downloading.
        try
        {
            var direct = await ReadPageDirectAsync(path, provider, item, index, ct).ConfigureAwait(false);
            if (direct is not null) return direct;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* fall through to the buffered copy */ }

        // Last resort (e.g. a non-seekable stream): wait for the buffered download and use it.
        var buffered = await dataTask.ConfigureAwait(false);
        item.Size = buffered.Length;
        EvictIfNeeded(path);
        return ExtractFromBytes(buffered, item, index);
    }

    /// <summary>Extracts a page from the fully-buffered archive bytes.</summary>
    private static (byte[] Bytes, string ContentType)? ExtractFromBytes(byte[] data, Item item, int index)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var archive = ArchiveFactory.OpenArchive(ms);
        var keys = item.Keys ??= ImageKeys(archive);
        if (index < 0 || index >= keys.Count) return null;
        var key = keys[index];
        var entry = archive.Entries.First(e => e.Key == key);
        using var es = entry.OpenEntryStream();
        using var outMs = new MemoryStream();
        es.CopyTo(outMs);
        return (outMs.ToArray(), ImageFormats.ContentType(key));
    }

    /// <summary>
    /// Extracts a single page directly from a seekable storage stream, reading only that entry's bytes
    /// (plus the archive's central directory) instead of the whole file. Returns null if the stream
    /// isn't seekable, so the caller can fall back to the buffered copy.
    /// </summary>
    private async Task<(byte[] Bytes, string ContentType)?> ReadPageDirectAsync(
        string path, IStorageProvider provider, Item item, int index, CancellationToken ct)
    {
        var stream = await provider.OpenReadAsync(path, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            if (!stream.CanSeek) return null; // need random access for a cheap single-entry read

            using var archive = ArchiveFactory.OpenArchive(stream);
            var keys = item.Keys ??= ImageKeys(archive);
            if (index < 0 || index >= keys.Count) return null;
            var key = keys[index];
            var entry = archive.Entries.First(e => e.Key == key);
            using var es = entry.OpenEntryStream();
            using var outMs = new MemoryStream();
            await es.CopyToAsync(outMs, ct).ConfigureAwait(false);
            return (outMs.ToArray(), ImageFormats.ContentType(key));
        }
    }

    private static IReadOnlyList<string> ImageKeys(IArchive archive) =>
        archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) && ImageFormats.IsImage(e.Key!))
            .Select(e => e.Key!)
            .OrderBy(k => k, NaturalComparer.Instance)
            .ToList();

    private async Task<byte[]> DownloadAsync(string path, IStorageProvider provider)
    {
        await using var stream = await provider.OpenReadAsync(path, CancellationToken.None).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private void EvictIfNeeded(string keep)
    {
        lock (_evictLock)
        {
            long total = 0;
            foreach (var kv in _map) total += kv.Value.Size;
            if (total <= MaxBytes) return;

            foreach (var kv in _map.OrderBy(k => k.Value.LastAccess))
            {
                if (total <= MaxBytes) break;
                if (kv.Key == keep) continue;
                if (_map.TryRemove(kv.Key, out var removed)) total -= removed.Size;
            }
        }
    }
}
