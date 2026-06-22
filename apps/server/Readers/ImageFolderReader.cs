using Mangrove.Server.Storage;

namespace Mangrove.Server.Readers;

/// <summary>
/// Treats a folder of raw images as a chapter (spec §4 "raw image folders"). Pages are the image
/// files directly inside the folder, in natural order; read straight through the storage provider.
/// </summary>
public sealed class ImageFolderReader
{
    public async Task<IReadOnlyList<string>> ListPagesAsync(
        string folderPath, IStorageProvider provider, CancellationToken ct = default)
    {
        var entries = await provider.ListAsync(folderPath, ct);
        return entries
            .Where(e => !e.IsDirectory && ImageFormats.IsImage(e.Name))
            .OrderBy(e => e.Name, NaturalComparer.Instance)
            .Select(e => e.FullPath)
            .ToList();
    }

    public async Task<int> CountPagesAsync(
        string folderPath, IStorageProvider provider, CancellationToken ct = default) =>
        (await ListPagesAsync(folderPath, provider, ct)).Count;

    public async Task<(byte[] Bytes, string ContentType)?> ReadPageAsync(
        string folderPath, IStorageProvider provider, int index, CancellationToken ct = default)
    {
        var pages = await ListPagesAsync(folderPath, provider, ct);
        if (index < 0 || index >= pages.Count) return null;

        var pagePath = pages[index];
        await using var stream = await provider.OpenReadAsync(pagePath, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return (ms.ToArray(), ImageFormats.ContentType(pagePath));
    }

    public Task<(byte[] Bytes, string ContentType)?> ReadCoverAsync(
        string folderPath, IStorageProvider provider, CancellationToken ct = default) =>
        ReadPageAsync(folderPath, provider, 0, ct);
}
