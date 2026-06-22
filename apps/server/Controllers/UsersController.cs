using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public sealed class UsersController : ControllerBase
{
    private readonly MangroveDbContext _db;
    private readonly PasswordHasher _hasher;

    public UsersController(MangroveDbContext db, PasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminUserDto>>> List(CancellationToken ct)
    {
        var users = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.LibraryAccess)
            .ToListAsync(ct);

        var restrictions = await _db.AgeRestrictions.ToDictionaryAsync(a => a.UserId, ct);

        return Ok(users.Select(u => ToDto(u, restrictions.GetValueOrDefault(u.Id))).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> Create(CreateUserRequest req, CancellationToken ct)
    {
        if (!AuthService.IsValidUsername(req.Username))
            return BadRequest(new { error = "Username cannot contain spaces." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters." });
        if (await _db.Users.AnyAsync(u => u.Username == req.Username, ct))
            return Conflict(new { error = "Username already exists." });

        var user = new User
        {
            Username = req.Username,
            Email = req.Email,
            PasswordHash = _hasher.Hash(req.Password),
        };
        await ApplyRolesAsync(user, req.Roles ?? new[] { "User" }, ct);

        // Optionally grant per-library access at creation time. Empty/omitted = no access yet
        // (admins always see everything regardless of these rows).
        if (req.LibraryIds is { Count: > 0 })
        {
            foreach (var libId in req.LibraryIds.Distinct())
                user.LibraryAccess.Add(new LibraryAccess { LibraryId = libId });
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(await ReloadAsync(user.Id, ct), null));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminUserDto>> Update(int id, UpdateUserRequest req, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.LibraryAccess)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();

        if (req.Email is not null) user.Email = req.Email;
        if (req.IsLocked is { } locked)
        {
            // Don't allow locking the last admin out.
            if (locked && await IsLastAdminAsync(user, ct))
                return BadRequest(new { error = "Cannot lock the last administrator." });
            user.IsLocked = locked;
            if (!locked) { user.LockoutEndsAt = null; user.FailedLoginCount = 0; }
        }

        if (req.Roles is not null)
        {
            if (await IsLastAdminAsync(user, ct) && !req.Roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { error = "Cannot remove the last administrator's Admin role." });
            user.UserRoles.Clear();
            await ApplyRolesAsync(user, req.Roles, ct);
        }

        if (req.LibraryIds is not null)
        {
            user.LibraryAccess.Clear();
            foreach (var libId in req.LibraryIds.Distinct())
                user.LibraryAccess.Add(new LibraryAccess { LibraryId = libId });
        }

        if (req.MaxAgeRating is not null)
        {
            var restriction = await _db.AgeRestrictions.FirstOrDefaultAsync(a => a.UserId == id, ct);
            if (req.MaxAgeRating <= 0)
            {
                if (restriction is not null) _db.AgeRestrictions.Remove(restriction);
            }
            else
            {
                restriction ??= new AgeRestriction { UserId = id };
                restriction.MaxAgeRating = req.MaxAgeRating.Value;
                restriction.IncludeUnknowns = req.IncludeUnknowns ?? true;
                if (restriction.Id == 0) _db.AgeRestrictions.Add(restriction);
            }
        }

        await _db.SaveChangesAsync(ct);
        var restr = await _db.AgeRestrictions.FirstOrDefaultAsync(a => a.UserId == id, ct);
        return Ok(ToDto(await ReloadAsync(id, ct), restr));
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync(new object[] { id }, ct);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters." });

        user.PasswordHash = _hasher.Hash(req.Password);
        // Invalidate existing sessions.
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound();
        if (await IsLastAdminAsync(user, ct))
            return BadRequest(new { error = "Cannot delete the last administrator." });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task ApplyRolesAsync(User user, IEnumerable<string> roleNames, CancellationToken ct)
    {
        foreach (var name in roleNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<RoleType>(name, ignoreCase: true, out var type)) continue;
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Type == type, ct);
            if (role is not null) user.UserRoles.Add(new UserRole { RoleId = role.Id });
        }
    }

    private async Task<bool> IsLastAdminAsync(User user, CancellationToken ct)
    {
        var isAdmin = user.UserRoles.Any(ur => ur.Role?.Type == RoleType.Admin)
            || await _db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.Role.Type == RoleType.Admin, ct);
        if (!isAdmin) return false;
        var adminCount = await _db.UserRoles.CountAsync(ur => ur.Role.Type == RoleType.Admin, ct);
        return adminCount <= 1;
    }

    private Task<User> ReloadAsync(int id, CancellationToken ct) =>
        _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.LibraryAccess)
            .FirstAsync(u => u.Id == id, ct);

    private static AdminUserDto ToDto(User u, AgeRestriction? restriction) => new(
        u.Id, u.Username, u.Email,
        u.UserRoles.Select(ur => ur.Role.Type.ToString()).ToList(),
        u.IsLocked, u.CreatedAt, u.LastActiveAt,
        u.LibraryAccess.Select(la => la.LibraryId).ToList(),
        restriction?.MaxAgeRating, restriction?.IncludeUnknowns ?? true);
}
