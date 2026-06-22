using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Mangrove.Server.Tests;

// Tests for the auth flow (spec §7 + the "auth flow" test requirement). Uses a real SQLite
// schema (in-memory) so EF mappings and unique constraints are exercised end-to-end.
public class AuthServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MangroveDbContext _db;
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MangroveDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new MangroveDbContext(options);
        _db.Database.EnsureCreated();

        var jwt = new JwtTokenService(new JwtOptions
        {
            Secret = "test-secret-test-secret-test-secret-1234",
        });
        _auth = new AuthService(_db, new PasswordHasher(), jwt);
    }

    [Fact]
    public async Task RegisterFirstAdmin_CreatesAdminAndIssuesTokens()
    {
        Assert.False(await _auth.AdminExistsAsync());

        var result = await _auth.RegisterFirstAdminAsync("admin", "a@b.c", "password123");

        Assert.Equal("admin", result.User.Username);
        Assert.Contains("Admin", result.Roles);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.True(await _auth.AdminExistsAsync());
    }

    [Fact]
    public async Task RegisterFirstAdmin_FailsWhenAdminAlreadyExists()
    {
        await _auth.RegisterFirstAdminAsync("admin", null, "password123");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _auth.RegisterFirstAdminAsync("second", null, "password123"));
    }

    [Fact]
    public async Task RegisterFirstAdmin_RejectsUsernameWithSpaces()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _auth.RegisterFirstAdminAsync("bad name", null, "password123"));
    }

    [Fact]
    public async Task Login_SucceedsWithCorrectPassword()
    {
        await _auth.RegisterFirstAdminAsync("admin", null, "password123");
        var result = await _auth.LoginAsync("admin", "password123");
        Assert.Equal("admin", result.User.Username);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
    }

    [Fact]
    public async Task Login_FailsWithWrongPassword()
    {
        await _auth.RegisterFirstAdminAsync("admin", null, "password123");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.LoginAsync("admin", "wrong"));
    }

    [Fact]
    public async Task Login_UnknownUserThrowsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.LoginAsync("nobody", "whatever"));
    }

    [Fact]
    public async Task Login_LocksAccountAfterRepeatedFailures()
    {
        await _auth.RegisterFirstAdminAsync("admin", null, "password123");

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _auth.LoginAsync("admin", "wrong"));

        // 6th attempt is rejected as locked even though we now use the correct password.
        await Assert.ThrowsAsync<AccountLockedException>(
            () => _auth.LoginAsync("admin", "password123"));
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldOneNoLongerValid()
    {
        var login = await _auth.RegisterFirstAdminAsync("admin", null, "password123");
        var original = login.RefreshToken;

        var refreshed = await _auth.RefreshAsync(original);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.NotEqual(original, refreshed.RefreshToken);

        // The rotated (old) token must be rejected.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _auth.RefreshAsync(original));
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var login = await _auth.RegisterFirstAdminAsync("admin", null, "password123");
        await _auth.LogoutAsync(login.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _auth.RefreshAsync(login.RefreshToken));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
