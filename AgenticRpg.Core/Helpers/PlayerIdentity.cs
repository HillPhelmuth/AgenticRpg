using System.Security.Claims;

namespace AgenticRpg.Core.Helpers;

public static class PlayerIdentity
{
    public static string? GetId(ClaimsPrincipal? user) => user?.Identity?.IsAuthenticated == true
        ? user.FindFirst("name")?.Value ?? user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity.Name
          ?? user.FindFirst("preferred_username")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("oid")?.Value
        : null;
}
