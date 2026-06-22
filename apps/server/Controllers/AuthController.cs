using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mangrove.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookie = "mangrove_refresh";

    private readonly AuthService _auth;
    private readonly MangroveDbContext _db;

    public AuthController(AuthService auth, MangroveDbContext db)
    {
        _auth = auth;
        _db = db;
    }

    [HttpGet("setup-status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusResponse>> SetupStatus(CancellationToken ct)
    {
        var adminExists = await _auth.AdminExistsAsync(ct);
        return Ok(new SetupStatusResponse(adminExists, AppConstants.AppName));
    }

    [HttpPost("register-first")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> RegisterFirst(RegisterFirstRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _auth.RegisterFirstAdminAsync(req.Username, req.Email, req.Password, ct);
            return Ok(BuildResponse(result));
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _auth.LoginAsync(req.Username, req.Password, ct);
            return Ok(BuildResponse(result));
        }
        catch (AccountLockedException ex) { return StatusCode(StatusCodes.Status423Locked, new { error = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { error = ex.Message }); }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var raw) || string.IsNullOrEmpty(raw))
            return Unauthorized(new { error = "No refresh token." });
        try
        {
            var result = await _auth.RefreshAsync(raw, ct);
            return Ok(BuildResponse(result));
        }
        catch (UnauthorizedAccessException ex)
        {
            ClearRefreshCookie();
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrEmpty(raw))
            await _auth.LogoutAsync(raw, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("/api/me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Unauthorized();

        return Ok(new UserDto(user.Id, user.Username, user.Email, AuthService.RoleNames(user)));
    }

    private AuthResponse BuildResponse(AuthResult result)
    {
        SetRefreshCookie(result.RefreshToken);
        var user = new UserDto(result.User.Id, result.User.Username, result.User.Email, result.Roles);
        return new AuthResponse(result.AccessToken, 1800, user);
    }

    private void SetRefreshCookie(string token)
    {
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });
    }

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth" });
}
