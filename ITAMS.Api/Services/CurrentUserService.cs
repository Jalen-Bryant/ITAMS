using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ITAMS.Api.Models;
using MongoDB.Bson;

namespace ITAMS.Api.Services;

public sealed class CurrentUserService(UsersService usersService)
{
    public bool TryGetUserId(ClaimsPrincipal principal, out ObjectId userId, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var rawUserId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrWhiteSpace(rawUserId))
        {
            userId = ObjectId.Empty;
            errors["authorization"] = ["The current access token does not contain a valid user identifier."];
            return false;
        }

        if (!ObjectId.TryParse(rawUserId, out userId))
        {
            errors["authorization"] = ["The current access token contains an invalid user identifier."];
            return false;
        }

        return true;
    }

    public bool TryGetSessionId(ClaimsPrincipal principal, out ObjectId sessionId, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var rawSessionId = principal.FindFirstValue(JwtRegisteredClaimNames.Sid);

        if (string.IsNullOrWhiteSpace(rawSessionId))
        {
            sessionId = ObjectId.Empty;
            errors["authorization"] = ["The current access token does not contain a valid session identifier."];
            return false;
        }

        if (!ObjectId.TryParse(rawSessionId, out sessionId))
        {
            errors["authorization"] = ["The current access token contains an invalid session identifier."];
            return false;
        }

        return true;
    }

    public async Task<UserDocument?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(principal, out var userId, out _))
        {
            return null;
        }

        return await usersService.GetByIdAsync(userId, cancellationToken);
    }
}
