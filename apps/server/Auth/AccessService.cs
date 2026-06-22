using Mangrove.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Auth;

/// <summary>
/// Central authorization helper for browsing: which libraries a user may see (per-library access)
/// and which series are visible under their age restriction (spec §7). Admins see everything.
/// </summary>
public sealed class AccessService
{
    private readonly MangroveDbContext _db;
    public AccessService(MangroveDbContext db) => _db = db;

    public async Task<List<int>> AccessibleLibraryIdsAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        if (isAdmin)
            return await _db.Libraries.Select(l => l.Id).ToListAsync(ct);
        return await _db.LibraryAccess
            .Where(a => a.UserId == userId)
            .Select(a => a.LibraryId)
            .ToListAsync(ct);
    }

    public async Task<AgeRestriction?> GetRestrictionAsync(int userId, bool isAdmin, CancellationToken ct = default)
    {
        if (isAdmin) return null; // admins are never age-restricted
        return await _db.AgeRestrictions.FirstOrDefaultAsync(a => a.UserId == userId, ct);
    }

    /// <summary>Applies per-library access and age-rating filtering to a Series query.</summary>
    public IQueryable<Series> FilterSeries(IQueryable<Series> query, IReadOnlyCollection<int> libraryIds, AgeRestriction? restriction)
    {
        query = query.Where(s => libraryIds.Contains(s.LibraryId));
        if (restriction is { MaxAgeRating: > 0 })
        {
            var max = restriction.MaxAgeRating;
            var includeUnknown = restriction.IncludeUnknowns;
            query = query.Where(s =>
                (s.AgeRatingTier > 0 && s.AgeRatingTier <= max) ||
                (s.AgeRatingTier == 0 && includeUnknown));
        }
        return query;
    }

    /// <summary>True if the user may view the given series (library access + age gate).</summary>
    public async Task<bool> CanAccessSeriesAsync(int userId, bool isAdmin, int seriesId, CancellationToken ct = default)
    {
        var libIds = await AccessibleLibraryIdsAsync(userId, isAdmin, ct);
        var restriction = await GetRestrictionAsync(userId, isAdmin, ct);
        return await FilterSeries(_db.Series.Where(s => s.Id == seriesId), libIds, restriction).AnyAsync(ct);
    }

    public async Task<bool> CanAccessChapterAsync(int userId, bool isAdmin, int chapterId, CancellationToken ct = default)
    {
        var seriesId = await _db.Chapters.Where(c => c.Id == chapterId)
            .Select(c => (int?)c.Volume.SeriesId).FirstOrDefaultAsync(ct);
        return seriesId is { } sid && await CanAccessSeriesAsync(userId, isAdmin, sid, ct);
    }
}
