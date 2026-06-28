using Mangrove.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Auth;

public sealed class AuthResult
{
    public required User User { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed class AccountLockedException : Exception
{
    public AccountLockedException(string message) : base(message) { }
}

/// <summary>
/// First-run admin setup, login (with lockout), and rotating refresh tokens (spec §6, §7).
/// Designed so an OIDC provider could be layered in later without reworking it.
/// </summary>
public sealed class AuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly MangroveDbContext _db;
    private readonly PasswordHasher _hasher;
    private readonly JwtTokenService _tokens;

    public AuthService(MangroveDbContext db, PasswordHasher hasher, JwtTokenService tokens)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
    }

    public Task<bool> AdminExistsAsync(CancellationToken ct = default) =>
        _db.UserRoles.AnyAsync(ur => ur.Role.Type == RoleType.Admin, ct);

    public static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username) && !username.Any(char.IsWhiteSpace) && username.Length <= 64;

    /// <summary>Creates the first admin. Only valid until an admin already exists (spec §9).</summary>
    public async Task<AuthResult> RegisterFirstAdminAsync(
        string username, string? email, string password, CancellationToken ct = default)
    {
        if (await AdminExistsAsync(ct))
            throw new InvalidOperationException("An administrator already exists.");
        if (!IsValidUsername(username))
            throw new ArgumentException("Username cannot contain spaces.", nameof(username));
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            throw new ArgumentException("Password must be at least 6 characters.", nameof(password));

        var adminRole = await EnsureRoleAsync(RoleType.Admin, ct);

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = _hasher.Hash(password),
        };
        user.UserRoles.Add(new UserRole { Role = adminRole });
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return await IssueAsync(user, ct);
    }

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
            throw new UnauthorizedAccessException("Invalid username or password.");

        if (user.IsLocked || (user.LockoutEndsAt is { } until && until > DateTime.UtcNow))
            throw new AccountLockedException("Account is temporarily locked. Try again later.");

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndsAt = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;
        user.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await IssueAsync(user, ct);
    }

    /// <summary>
    /// Validates a refresh token and issues a fresh access token, keeping the SAME refresh token and
    /// sliding its expiry. We deliberately do NOT rotate (revoke + reissue) here: rotation creates a
    /// race where parallel refreshes (e.g. several API calls firing at once after the access token
    /// expires, or multiple tabs) revoke each other's token and bounce the user back to the login
    /// screen. Reusing the token keeps an active device signed in until it explicitly logs out.
    /// </summary>
    public async Task<AuthResult> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenService.HashRefreshToken(rawRefreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || !token.IsActive)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");

        // Slide the expiry forward so a device that keeps being used never times out.
        token.ExpiresAt = DateTime.UtcNow.Add(_tokens.Options.RefreshTokenLifetime);
        token.User.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var roles = RoleNames(token.User);
        var access = _tokens.CreateAccessToken(token.User, roles);
        return new AuthResult
        {
            User = token.User,
            AccessToken = access,
            RefreshToken = rawRefreshToken, // unchanged — re-sent so the cookie's expiry slides too
            Roles = roles,
        };
    }

    public async Task LogoutAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenService.HashRefreshToken(rawRefreshToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Verifies a username/password without issuing tokens (used for OPDS Basic auth).</summary>
    public async Task<User?> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || user.IsLocked) return null;
        if (user.LockoutEndsAt is { } until && until > DateTime.UtcNow) return null;
        return _hasher.Verify(password, user.PasswordHash) ? user : null;
    }

    public static IReadOnlyList<string> RoleNames(User user) =>
        user.UserRoles.Select(ur => ur.Role.Type.ToString()).ToList();

    private async Task<AuthResult> IssueAsync(User user, CancellationToken ct)
    {
        var roles = RoleNames(user);
        var access = _tokens.CreateAccessToken(user, roles);
        var (raw, refreshHash) = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = DateTime.UtcNow.Add(_tokens.Options.RefreshTokenLifetime),
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResult { User = user, AccessToken = access, RefreshToken = raw, Roles = roles };
    }

    private async Task<Role> EnsureRoleAsync(RoleType type, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Type == type, ct);
        if (role is not null) return role;

        role = new Role
        {
            Type = type,
            Name = type.ToString(),
            CanDownload = type != RoleType.ReadOnly,
            CanManageLibraries = type == RoleType.Admin,
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return role;
    }
}
