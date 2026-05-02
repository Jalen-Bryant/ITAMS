using System.Security.Claims;
using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Services;
using ITAMS.Api.Validation;

namespace ITAMS.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Authentication");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .AllowAnonymous();

        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .RequireAuthorization(AuthorizationPolicies.Authenticated);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .RequireAuthorization(AuthorizationPolicies.Authenticated);

        group.MapPost("/change-password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .RequireAuthorization(AuthorizationPolicies.Authenticated);

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        AuthService authService,
        OperationContextService operationContextService,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var result = await authService.LoginAsync(
            request.Identifier!.Trim(),
            request.Password!,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);

        if (!result.Success || result.User is null)
        {
            return UnauthorizedWithMessage(result.Error ?? "The supplied credentials are invalid.");
        }

        return Results.Ok(new LoginResponse
        {
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken,
            AccessTokenExpiresAt = result.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = result.RefreshTokenExpiresAt,
            User = UserEndpoints.MapCurrentUserResponse(result.User)
        });
    }

    private static async Task<IResult> RefreshAsync(
        RefreshTokenRequest request,
        HttpContext httpContext,
        AuthService authService,
        OperationContextService operationContextService,
        CancellationToken cancellationToken)
    {
        var validationErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        RequestValidation.AddRequiredStringError(validationErrors, "refreshToken", request.RefreshToken);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var result = await authService.RefreshAsync(
            request.RefreshToken!.Trim(),
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);

        if (!result.Success)
        {
            return UnauthorizedWithMessage(result.Error ?? "The supplied refresh token is invalid.");
        }

        return Results.Ok(new RefreshTokenResponse
        {
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken,
            AccessTokenExpiresAt = result.AccessTokenExpiresAt,
            RefreshTokenExpiresAt = result.RefreshTokenExpiresAt
        });
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CurrentUserService currentUserService,
        CancellationToken cancellationToken)
    {
        var currentUser = await currentUserService.GetCurrentUserAsync(principal, cancellationToken);
        return currentUser is null ? Results.Unauthorized() : Results.Ok(UserEndpoints.MapCurrentUserResponse(currentUser));
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        OperationContextService operationContextService,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.TryGetUserId(principal, out var userId, out _))
        {
            return Results.Unauthorized();
        }

        if (!currentUserService.TryGetSessionId(principal, out var sessionId, out _))
        {
            return Results.Unauthorized();
        }

        await authService.LogoutAsync(
            userId,
            sessionId,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        OperationContextService operationContextService,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var validationErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        RequestValidation.AddPasswordError(validationErrors, "currentPassword", request.CurrentPassword);
        RequestValidation.AddPasswordError(validationErrors, "newPassword", request.NewPassword);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!currentUserService.TryGetUserId(principal, out var userId, out _))
        {
            return Results.Unauthorized();
        }

        var result = await authService.ChangePasswordAsync(
            userId,
            request.CurrentPassword!,
            request.NewPassword!,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);

        if (!result.Success)
        {
            return UnauthorizedWithMessage(result.Error ?? "The supplied credentials are invalid.");
        }

        return Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateRequest(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringError(errors, "identifier", request.Identifier);
        RequestValidation.AddPasswordError(errors, "password", request.Password);

        return errors;
    }

    private static IResult UnauthorizedWithMessage(string message) =>
        Results.Json(
            new { message },
            statusCode: StatusCodes.Status401Unauthorized);
}
