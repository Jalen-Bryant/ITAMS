using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MongoDB.Bson;

namespace ITAMS.Api.Services;

public sealed class SessionValidationService(
    UserSessionsService userSessionsService,
    UsersService usersService)
{
    public async Task<bool> IsPrincipalActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var rawUserId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var rawSessionId = principal.FindFirstValue(JwtRegisteredClaimNames.Sid);

        if (!ObjectId.TryParse(rawUserId, out var userId) || !ObjectId.TryParse(rawSessionId, out var sessionId))
        {
            return false;
        }

        var user = await usersService.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return false;
        }

        return await userSessionsService.IsActiveSessionAsync(userId, sessionId, DateTime.UtcNow, cancellationToken);
    }
}
