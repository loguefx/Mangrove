using System.Security.Claims;

namespace Mangrove.Server.Controllers;

public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole("Admin");
}
