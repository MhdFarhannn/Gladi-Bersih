using System.Security.Claims;
using Gladi_Bersih.Common;

namespace Gladi_Bersih.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static uint GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!uint.TryParse(value, out var userId))
        {
            throw new AppException(StatusCodes.Status401Unauthorized, "Token pengguna tidak valid.");
        }

        return userId;
    }

    public static string GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    }
}
