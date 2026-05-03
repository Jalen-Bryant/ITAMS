using System.Security.Claims;
using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using ITAMS.Api.Validation;
using MongoDB.Bson;

namespace ITAMS.Api.Endpoints;

public static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assignments").WithTags("Assignments");

        group.MapGet(string.Empty, GetAllAssignmentsAsync)
            .WithName("GetAssignments")
            .RequireAuthorization(AuthorizationPolicies.AssignmentRead);

        group.MapGet("/{id}", GetAssignmentByIdAsync)
            .WithName("GetAssignmentById")
            .RequireAuthorization(AuthorizationPolicies.AssignmentRead);

        group.MapPost(string.Empty, CreateAssignmentAsync)
            .WithName("CreateAssignment")
            .RequireAuthorization(AuthorizationPolicies.AssignmentWrite);

        group.MapPut("/{id}", UpdateAssignmentAsync)
            .WithName("UpdateAssignment")
            .RequireAuthorization(AuthorizationPolicies.AssignmentWrite);

        group.MapDelete("/{id}", DeleteAssignmentAsync)
            .WithName("DeleteAssignment")
            .RequireAuthorization(AuthorizationPolicies.AssignmentWrite);

        return app;
    }

    private static async Task<IResult> GetAllAssignmentsAsync(
        int? offset,
        int? limit,
        AssignmentsService assignmentsService,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(offset, limit, out var pageRequest, out var validationErrors))
        {
            return Results.ValidationProblem(validationErrors);
        }

        var assignments = await assignmentsService.GetAllAsync(pageRequest, cancellationToken);
        return Results.Ok(assignments.Select(MapResponse));
    }

    private static async Task<IResult> GetAssignmentByIdAsync(
        string id,
        AssignmentsService assignmentsService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAssignmentId(id, out var assignmentId, out var validationResult))
        {
            return validationResult;
        }

        var assignment = await assignmentsService.GetByIdAsync(assignmentId, cancellationToken);
        return assignment is null ? Results.NotFound() : Results.Ok(MapResponse(assignment));
    }

    private static async Task<IResult> CreateAssignmentAsync(
        CreateAssignmentRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        AssignmentMutationService assignmentMutationService,
        OperationContextService operationContextService,
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

        if (!TryParseReferenceIds(
                request.AssetId,
                request.UserId,
                out var assetId,
                out var userId,
                out var referenceIdError))
        {
            return referenceIdError;
        }

        var assignment = new AssignmentDocument
        {
            Id = ObjectId.GenerateNewId(),
            AssetId = assetId,
            UserId = userId,
            AssignedByUserId = actorUserId,
            StartDate = NormalizeToUtc(request.StartDate),
            EndDate = request.EndDate is null ? null : NormalizeToUtc(request.EndDate.Value),
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var mutationResult = await assignmentMutationService.CreateAsync(
            assignment,
            actorUserId,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);
        if (mutationResult.Errors.Count > 0)
        {
            return Results.ValidationProblem(mutationResult.Errors);
        }

        return Results.Created($"/assignments/{assignment.Id}", MapResponse(assignment));
    }

    private static async Task<IResult> UpdateAssignmentAsync(
        string id,
        UpdateAssignmentRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        AssignmentMutationService assignmentMutationService,
        OperationContextService operationContextService,
        AssignmentsService assignmentsService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAssignmentId(id, out var assignmentId, out var validationResult))
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

        var existingAssignment = await assignmentsService.GetByIdAsync(assignmentId, cancellationToken);
        if (existingAssignment is null)
        {
            return Results.NotFound();
        }

        if (!TryParseReferenceIds(
                request.AssetId,
                request.UserId,
                out var assetId,
                out var userId,
                out var referenceIdError))
        {
            return referenceIdError;
        }

        var updatedAssignment = new AssignmentDocument
        {
            Id = existingAssignment.Id,
            AssetId = assetId,
            UserId = userId,
            AssignedByUserId = actorUserId,
            StartDate = NormalizeToUtc(request.StartDate),
            EndDate = request.EndDate is null ? null : NormalizeToUtc(request.EndDate.Value),
            Notes = request.Notes?.Trim(),
            CreatedAt = EnsureUtc(existingAssignment.CreatedAt)
        };

        var mutationResult = await assignmentMutationService.ReplaceAsync(
            updatedAssignment,
            actorUserId,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);
        if (mutationResult.Errors.Count > 0)
        {
            return Results.ValidationProblem(mutationResult.Errors);
        }

        if (mutationResult.NotFound)
        {
            return Results.NotFound();
        }

        return Results.Ok(MapResponse(updatedAssignment));
    }

    private static async Task<IResult> DeleteAssignmentAsync(
        string id,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        OperationContextService operationContextService,
        AssignmentMutationService assignmentMutationService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAssignmentId(id, out var assignmentId, out var validationResult))
        {
            return validationResult;
        }

        if (!currentUserService.TryGetUserId(principal, out var actorUserId, out _))
        {
            return Results.Unauthorized();
        }

        var mutationResult = await assignmentMutationService.DeleteAsync(
            assignmentId,
            actorUserId,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);
        if (mutationResult.NotFound)
        {
            return Results.NotFound();
        }

        return Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateRequest(CreateAssignmentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringError(errors, "assetId", request.AssetId);
        RequestValidation.AddRequiredStringError(errors, "userId", request.UserId);
        RequestValidation.AddRequiredDateError(errors, "startDate", request.StartDate);
        RequestValidation.AddOptionalStringMaxLengthError(errors, "notes", request.Notes, 500);
        AddAssignmentBusinessRuleErrors(errors, request.StartDate, request.EndDate);

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRequest(UpdateAssignmentRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringError(errors, "assetId", request.AssetId);
        RequestValidation.AddRequiredStringError(errors, "userId", request.UserId);
        RequestValidation.AddRequiredDateError(errors, "startDate", request.StartDate);
        RequestValidation.AddOptionalStringMaxLengthError(errors, "notes", request.Notes, 500);
        AddAssignmentBusinessRuleErrors(errors, request.StartDate, request.EndDate);

        return errors;
    }

    private static bool TryParseAssignmentId(string rawId, out ObjectId assignmentId, out IResult validationResult)
    {
        if (ObjectId.TryParse(rawId, out assignmentId))
        {
            validationResult = Results.Empty;
            return true;
        }

        validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = ["The assignment id must be a valid MongoDB ObjectId."]
        });
        return false;
    }

    private static bool TryParseReferenceIds(
        string? rawAssetId,
        string? rawUserId,
        out ObjectId assetId,
        out ObjectId userId,
        out IResult validationResult)
    {
        if (!ObjectId.TryParse(rawAssetId, out assetId))
        {
            userId = ObjectId.Empty;
            validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["assetId"] = ["assetId must be a valid MongoDB ObjectId."]
            });
            return false;
        }

        if (!ObjectId.TryParse(rawUserId, out userId))
        {
            validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["userId must be a valid MongoDB ObjectId."]
            });
            return false;
        }

        validationResult = Results.Empty;
        return true;
    }

    private static DateTime NormalizeToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime()
        };

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static void AddAssignmentBusinessRuleErrors(
        IDictionary<string, string[]> errors,
        DateTime startDate,
        DateTime? endDate)
    {
        if (startDate == default || endDate is null)
        {
            return;
        }

        if (endDate.Value < startDate)
        {
            errors["endDate"] = ["endDate must be on or after startDate."];
        }
    }

    private static AssignmentResponse MapResponse(AssignmentDocument assignment) =>
        new()
        {
            Id = assignment.Id.ToString(),
            AssetId = assignment.AssetId.ToString(),
            UserId = assignment.UserId.ToString(),
            AssignedByUserId = assignment.AssignedByUserId.ToString(),
            StartDate = EnsureUtc(assignment.StartDate),
            EndDate = assignment.EndDate is null ? null : EnsureUtc(assignment.EndDate.Value),
            Notes = assignment.Notes,
            CreatedAt = EnsureUtc(assignment.CreatedAt)
        };
}
