using System.Security.Claims;
using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using ITAMS.Api.Validation;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users").WithTags("Users");

        group.MapGet(string.Empty, GetAllUsersAsync)
            .WithName("GetUsers")
            .RequireAuthorization(AuthorizationPolicies.UserRead);

        group.MapGet("/{id}", GetUserByIdAsync)
            .WithName("GetUserById")
            .RequireAuthorization(AuthorizationPolicies.UserRead);

        group.MapPost(string.Empty, CreateUserAsync)
            .WithName("CreateUser")
            .RequireAuthorization(AuthorizationPolicies.UserWrite);

        group.MapPut("/{id}", UpdateUserAsync)
            .WithName("UpdateUser")
            .RequireAuthorization(AuthorizationPolicies.UserWrite);

        group.MapDelete("/{id}", DeleteUserAsync)
            .WithName("DeleteUser")
            .RequireAuthorization(AuthorizationPolicies.UserWrite);

        return app;
    }

    private static async Task<IResult> GetAllUsersAsync(
        int? offset,
        int? limit,
        UsersService usersService,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(offset, limit, out var pageRequest, out var validationErrors))
        {
            return Results.ValidationProblem(validationErrors);
        }

        var users = await usersService.GetAllAsync(pageRequest, cancellationToken);
        return Results.Ok(users.Select(MapResponse));
    }

    private static async Task<IResult> GetUserByIdAsync(
        string id,
        UsersService usersService,
        CancellationToken cancellationToken)
    {
        if (!TryParseUserId(id, out var userId, out var validationResult))
        {
            return validationResult;
        }

        var user = await usersService.GetByIdAsync(userId, cancellationToken);
        return user is null ? Results.NotFound() : Results.Ok(MapResponse(user));
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        OperationContextService operationContextService,
        UserMutationService userMutationService,
        IPasswordHasher<UserDocument> passwordHasher,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!currentUserService.TryGetUserId(principal, out var actorUserId, out _))
        {
            return Results.Unauthorized();
        }

        var now = DateTime.UtcNow;
        var user = new UserDocument
        {
            Id = ObjectId.GenerateNewId(),
            Username = request.Username!.Trim(),
            DisplayName = request.DisplayName!.Trim(),
            Email = request.Email!.Trim(),
            NormalizedUsername = UsersService.NormalizeValue(request.Username!),
            NormalizedEmail = UsersService.NormalizeValue(request.Email!),
            PasswordHash = null,
            PasswordChangedAt = now,
            Role = request.Role!.Trim(),
            Department = request.Department!.Trim(),
            IsActive = request.IsActive!.Value,
            CreatedAt = now,
            UpdatedAt = now
        };

        user = new UserDocument
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            NormalizedUsername = user.NormalizedUsername,
            NormalizedEmail = user.NormalizedEmail,
            PasswordHash = passwordHasher.HashPassword(user, request.Password!),
            PasswordChangedAt = user.PasswordChangedAt,
            Role = user.Role,
            Department = user.Department,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };

        try
        {
            await userMutationService.CreateAsync(
                user,
                actorUserId,
                operationContextService.GetClientIp(httpContext),
                operationContextService.GetUserAgent(httpContext),
                cancellationToken);
        }
        catch (MongoWriteException exception) when (UsersService.IsDuplicateKey(exception))
        {
            return Results.Conflict(new
            {
                message = UsersService.GetDuplicateKeyMessage(exception)
            });
        }

        return Results.Created($"/users/{user.Id}", MapResponse(user));
    }

    private static async Task<IResult> UpdateUserAsync(
        string id,
        UpdateUserRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        OperationContextService operationContextService,
        UserMutationService userMutationService,
        UsersService usersService,
        CancellationToken cancellationToken)
    {
        if (!TryParseUserId(id, out var userId, out var validationResult))
        {
            return validationResult;
        }

        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!currentUserService.TryGetUserId(principal, out var actorUserId, out _))
        {
            return Results.Unauthorized();
        }

        var existingUser = await usersService.GetByIdAsync(userId, cancellationToken);
        if (existingUser is null)
        {
            return Results.NotFound();
        }

        var updatedUser = new UserDocument
        {
            Id = existingUser.Id,
            Username = request.Username!.Trim(),
            DisplayName = request.DisplayName!.Trim(),
            Email = request.Email!.Trim(),
            NormalizedUsername = UsersService.NormalizeValue(request.Username!),
            NormalizedEmail = UsersService.NormalizeValue(request.Email!),
            PasswordHash = existingUser.PasswordHash,
            PasswordChangedAt = existingUser.PasswordChangedAt,
            FailedLoginCount = existingUser.FailedLoginCount,
            FailedLoginWindowStartedAt = existingUser.FailedLoginWindowStartedAt,
            LastFailedLoginAt = existingUser.LastFailedLoginAt,
            LockoutEndAt = existingUser.LockoutEndAt,
            Role = request.Role!.Trim(),
            Department = request.Department!.Trim(),
            IsActive = request.IsActive!.Value,
            CreatedAt = EnsureUtc(existingUser.CreatedAt),
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            var mutationResult = await userMutationService.ReplaceAsync(
                updatedUser,
                actorUserId,
                operationContextService.GetClientIp(httpContext),
                operationContextService.GetUserAgent(httpContext),
                cancellationToken);
            return mutationResult.NotFound ? Results.NotFound() : Results.Ok(MapResponse(updatedUser));
        }
        catch (MongoWriteException exception) when (UsersService.IsDuplicateKey(exception))
        {
            return Results.Conflict(new
            {
                message = UsersService.GetDuplicateKeyMessage(exception)
            });
        }
    }

    private static async Task<IResult> DeleteUserAsync(
        string id,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        OperationContextService operationContextService,
        ReferenceIntegrityService referenceIntegrityService,
        UserMutationService userMutationService,
        CancellationToken cancellationToken)
    {
        if (!TryParseUserId(id, out var userId, out var validationResult))
        {
            return validationResult;
        }

        if (!currentUserService.TryGetUserId(principal, out var actorUserId, out _))
        {
            return Results.Unauthorized();
        }

        var referenceErrors = await referenceIntegrityService.ValidateUserDeletionAsync(userId, cancellationToken);
        if (referenceErrors.Count > 0)
        {
            return Results.ValidationProblem(referenceErrors);
        }

        var mutationResult = await userMutationService.DeleteAsync(
            userId,
            actorUserId,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);

        return mutationResult.NotFound ? Results.NotFound() : Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateRequest(CreateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringLengthError(errors, "username", request.Username, 3, 50);
        RequestValidation.AddRequiredStringLengthError(errors, "displayName", request.DisplayName, 1, 100);
        RequestValidation.AddEmailError(errors, "email", request.Email);
        RequestValidation.AddPasswordError(errors, "password", request.Password);
        RequestValidation.AddRequiredStringLengthError(errors, "role", request.Role, 1, 40);
        RequestValidation.AddAllowedValuesError(errors, "role", request.Role, RequestValidation.UserRoles);
        RequestValidation.AddRequiredStringLengthError(errors, "department", request.Department, 1, 80);
        RequestValidation.AddRequiredBoolError(errors, "isActive", request.IsActive);

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRequest(UpdateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringLengthError(errors, "username", request.Username, 3, 50);
        RequestValidation.AddRequiredStringLengthError(errors, "displayName", request.DisplayName, 1, 100);
        RequestValidation.AddEmailError(errors, "email", request.Email);
        RequestValidation.AddRequiredStringLengthError(errors, "role", request.Role, 1, 40);
        RequestValidation.AddAllowedValuesError(errors, "role", request.Role, RequestValidation.UserRoles);
        RequestValidation.AddRequiredStringLengthError(errors, "department", request.Department, 1, 80);
        RequestValidation.AddRequiredBoolError(errors, "isActive", request.IsActive);

        return errors;
    }

    private static bool TryParseUserId(string rawId, out ObjectId userId, out IResult validationResult)
    {
        if (ObjectId.TryParse(rawId, out userId))
        {
            validationResult = Results.Empty;
            return true;
        }

        validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = ["The user id must be a valid MongoDB ObjectId."]
        });
        return false;
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    internal static UserResponse MapResponse(UserDocument user) =>
        new()
        {
            Id = user.Id.ToString(),
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            Department = user.Department,
            IsActive = user.IsActive,
            CreatedAt = EnsureUtc(user.CreatedAt),
            UpdatedAt = EnsureUtc(user.UpdatedAt)
        };

    internal static CurrentUserResponse MapCurrentUserResponse(UserDocument user) =>
        new()
        {
            Id = user.Id.ToString(),
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            Department = user.Department,
            IsActive = user.IsActive
        };
}
