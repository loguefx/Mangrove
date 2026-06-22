using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Mangrove.Server.Tests;

// Phase 3 (spec §7): per-library access + age-rating restrictions filter what a user can browse.
public class AccessServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MangroveDbContext _db;
    private readonly AccessService _access;

    public AccessServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<MangroveDbContext>().UseSqlite(_connection).Options;
        _db = new MangroveDbContext(options);
        _db.Database.EnsureCreated();
        _access = new AccessService(_db);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("Everyone", 1)]
    [InlineData("Teen", 3)]
    [InlineData("Mature 17+", 4)]
    [InlineData("Adults Only 18+", 5)]
    [InlineData("something weird", 0)]
    public void AgeRatingMap_MapsKnownRatingsToTiers(string? rating, int expected)
    {
        Assert.Equal(expected, AgeRatingMap.Tier(rating));
    }

    [Fact]
    public async Task NonAdmin_OnlySeesGrantedLibraries()
    {
        var (lib1, lib2) = await SeedTwoLibrariesAsync();
        var user = await SeedUserAsync(grantLibraryId: lib1);

        var ids = await _access.AccessibleLibraryIdsAsync(user, isAdmin: false);

        Assert.Contains(lib1, ids);
        Assert.DoesNotContain(lib2, ids);
    }

    [Fact]
    public async Task Admin_SeesAllLibraries()
    {
        var (lib1, lib2) = await SeedTwoLibrariesAsync();
        var ids = await _access.AccessibleLibraryIdsAsync(userId: 999, isAdmin: true);
        Assert.Contains(lib1, ids);
        Assert.Contains(lib2, ids);
    }

    [Fact]
    public async Task AgeRestriction_HidesTooMatureSeries_AndUnknownsWhenExcluded()
    {
        var (lib1, _) = await SeedTwoLibrariesAsync();
        var user = await SeedUserAsync(grantLibraryId: lib1);

        var everyone = await AddSeriesAsync(lib1, "Kid Comic", tier: 1);
        var mature = await AddSeriesAsync(lib1, "Mature Comic", tier: 4);
        var unrated = await AddSeriesAsync(lib1, "Unrated Comic", tier: 0);

        // Restrict to Teen (tier 3), excluding unknowns.
        var restriction = new AgeRestriction { UserId = user, MaxAgeRating = 3, IncludeUnknowns = false };
        _db.AgeRestrictions.Add(restriction);
        await _db.SaveChangesAsync();

        var libIds = await _access.AccessibleLibraryIdsAsync(user, isAdmin: false);
        var visible = await _access.FilterSeries(_db.Series, libIds, restriction)
            .Select(s => s.Id).ToListAsync();

        Assert.Contains(everyone, visible);
        Assert.DoesNotContain(mature, visible);
        Assert.DoesNotContain(unrated, visible);
    }

    [Fact]
    public async Task AgeRestriction_IncludesUnknownsWhenAllowed()
    {
        var (lib1, _) = await SeedTwoLibrariesAsync();
        var user = await SeedUserAsync(grantLibraryId: lib1);
        var unrated = await AddSeriesAsync(lib1, "Unrated Comic", tier: 0);

        var restriction = new AgeRestriction { UserId = user, MaxAgeRating = 3, IncludeUnknowns = true };
        var libIds = await _access.AccessibleLibraryIdsAsync(user, isAdmin: false);
        var visible = await _access.FilterSeries(_db.Series, libIds, restriction)
            .Select(s => s.Id).ToListAsync();

        Assert.Contains(unrated, visible);
    }

    private async Task<(int lib1, int lib2)> SeedTwoLibrariesAsync()
    {
        var lib1 = new Library { Name = "Lib1", RootPath = "/a" };
        var lib2 = new Library { Name = "Lib2", RootPath = "/b" };
        _db.Libraries.AddRange(lib1, lib2);
        await _db.SaveChangesAsync();
        return (lib1.Id, lib2.Id);
    }

    private async Task<int> SeedUserAsync(int grantLibraryId)
    {
        var user = new User { Username = "reader", PasswordHash = "x" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.LibraryAccess.Add(new LibraryAccess { UserId = user.Id, LibraryId = grantLibraryId });
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> AddSeriesAsync(int libraryId, string name, int tier)
    {
        var s = new Series { LibraryId = libraryId, Name = name, SortName = name, AgeRatingTier = tier };
        _db.Series.Add(s);
        await _db.SaveChangesAsync();
        return s.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
