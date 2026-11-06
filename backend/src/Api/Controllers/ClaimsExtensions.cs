using System.Security.Claims;
using Application.Common;

namespace Api.Controllers;

public static class ClaimsExtensions
{
    /// <summary>The authenticated user's id from the JWT <c>sub</c> claim. Never read it from the request body or route.</summary>
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
            ? id
            : throw new UnauthorizedException("Invalid token.");
}
