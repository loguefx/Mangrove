using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Mangrove.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/storage")]
[Authorize(Roles = "Admin")]
public sealed class StorageController : ControllerBase
{
    private readonly StorageProviderFactory _providers;

    public StorageController(StorageProviderFactory providers) => _providers = providers;

    /// <summary>
    /// Tests a Local or SMB connection by listing the top-level folder before a library is saved
    /// (spec §5 "Test Connection" button).
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult<StorageTestResponse>> Test(StorageTestRequest req, CancellationToken ct)
    {
        try
        {
            IStorageProvider provider = req.StorageKind == StorageKind.Smb
                ? _providers.ForSmb(req.Username ?? string.Empty, req.Password ?? string.Empty, req.Domain, "test")
                : _providers.Local();

            var entries = await provider.ListAsync(req.RootPath, ct);
            var top = entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(e => new StorageTestEntry(e.Name, e.IsDirectory, e.Size))
                .ToList();

            return Ok(new StorageTestResponse(true,
                $"Connected. Found {entries.Count} item(s) in the top-level folder.", top));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(new StorageTestResponse(false, $"Authentication failed: {ex.Message}", Array.Empty<StorageTestEntry>()));
        }
        catch (Exception ex)
        {
            return Ok(new StorageTestResponse(false, ex.Message, Array.Empty<StorageTestEntry>()));
        }
    }
}
