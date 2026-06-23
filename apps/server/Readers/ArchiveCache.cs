using System.Collections.Concurrent;
using Mangrove.Server.Storage;
using SharpCompress.Archives;

namespace Mangrove.Server.Readers;

/// <summary>
/// Keeps recently-read comic archives (CBZ/ZIP) buffered in memory so that reading a chapter pulls
/// the file from the NAS exactly once instead of once per page. Concurrent requests for the same
/// archive share a single download, and the cache is bounded by a total-byte budget with
/// least-recently-used eviction. This is the difference between "download the whole CBZ 30 times"
/// and "download it once", which is what makes page turns feel instant.
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

    /// <summary>Reads a single page (0-based, natural order) using the cached archive bytes.</summary>
    public async Task<(byte[] Bytes, string ContentType)?> ReadPageAsync(
        string path, IStorageProvider provider, int index, CancellationToken ct)
    {
        var item = _map.GetOrAdd(path, p => new Item
        {
            Data = new Lazy<Task<byte[]>>(
                () => DownloadAsync(p, provider, ct), LazyThreadSafetyMode.ExecutionAndPublication),
            LastAccess = DateTime.UtcNow.Ticks,
        });

        var data = await item.Data.Value.ConfigureAwait(false);
        item.Size = data.Length;
        item.LastAccess = DateTime.UtcNow.Ticks;
        EvictIfNeeded(path);

        await using var ms = new MemoryStream(data, writable: false);
        using var archive = ArchiveFactory.OpenArchive(ms);

        var keys = item.Keys ??= archive.Entries
            .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) && ImageFormats.IsImage(e.Key!))
            .Select(e => e.Key!)
            .OrderBy(k => k, NaturalComparer.Instance)
            .ToList();

        if (index < 0 || index >= keys.Count) return null;
        var key = keys[index];
        var entry = archive.Entries.First(e => e.Key == key);

        using var es = entry.OpenEntryStream();
        using var outMs = new MemoryStream();
        es.CopyTo(outMs);
        return (outMs.ToArray(), ImageFormats.ContentType(key));
    }

    private async Task<byte[]> DownloadAsync(string path, IStorageProvider provider, CancellationToken ct)
    {
        await using var stream = await provider.OpenReadAsync(path, ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
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
