using System.Security.Claims;
using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using ITAMS.Api.Validation;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ITAMS.Api.Endpoints;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/assets").WithTags("Assets");

        group.MapGet(string.Empty, GetAllAssetsAsync)
            .WithName("GetAssets")
            .RequireAuthorization(AuthorizationPolicies.AssetRead);

        group.MapGet("/{id}", GetAssetByIdAsync)
            .WithName("GetAssetById")
            .RequireAuthorization(AuthorizationPolicies.AssetRead);

        group.MapPost(string.Empty, CreateAssetAsync)
            .WithName("CreateAsset")
            .RequireAuthorization(AuthorizationPolicies.AssetWrite);

        group.MapPut("/{id}", UpdateAssetAsync)
            .WithName("UpdateAsset")
            .RequireAuthorization(AuthorizationPolicies.AssetWrite);

        group.MapDelete("/{id}", DeleteAssetAsync)
            .WithName("DeleteAsset")
            .RequireAuthorization(AuthorizationPolicies.AssetWrite);

        return app;
    }

    private static async Task<IResult> GetAllAssetsAsync(
        AssetsService assetsService,
        CancellationToken cancellationToken)
    {
        var assets = await assetsService.GetAllAsync(cancellationToken);
        return Results.Ok(assets.Select(MapResponse));
    }

    private static async Task<IResult> GetAssetByIdAsync(
        string id,
        AssetsService assetsService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAssetId(id, out var assetId, out var validationResult))
        {
            return validationResult;
        }

        var asset = await assetsService.GetByIdAsync(assetId, cancellationToken);
        return asset is null ? Results.NotFound() : Results.Ok(MapResponse(asset));
    }

    private static async Task<IResult> CreateAssetAsync(
        CreateAssetRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        AssetMutationService assetMutationService,
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

        var now = DateTime.UtcNow;
        var asset = new AssetDocument
        {
            Id = ObjectId.GenerateNewId(),
            AssetTag = request.AssetTag!.Trim(),
            SerialNumber = request.SerialNumber!.Trim(),
            Type = request.Type!.Trim(),
            Manufacturer = request.Manufacturer!.Trim(),
            Model = request.Model!.Trim(),
            Status = request.Status!.Trim(),
            Department = request.Department!.Trim(),
            Location = request.Location!.Trim(),
            PurchaseDate = NormalizeToUtc(request.PurchaseDate),
            WarrantyEndDate = NormalizeToUtc(request.WarrantyEndDate),
            EndOfLifeDate = NormalizeToUtc(request.EndOfLifeDate),
            CurrentAssignment = null,
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await assetMutationService.CreateAsync(
                asset,
                actorUserId,
                operationContextService.GetClientIp(httpContext),
                operationContextService.GetUserAgent(httpContext),
                cancellationToken);
        }
        catch (MongoWriteException exception) when (AssetsService.IsDuplicateKey(exception))
        {
            return Results.Conflict(new
            {
                message = AssetsService.GetDuplicateKeyMessage(exception)
            });
        }

        return Results.Created($"/assets/{asset.Id}", MapResponse(asset));
    }

    private static async Task<IResult> UpdateAssetAsync(
        string id,
        UpdateAssetRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        AssetMutationService assetMutationService,
        OperationContextService operationContextService,
        AssetsService assetsService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAssetId(id, out var assetId, out var validationResult))
        {
            return validationResult;
        }

        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var existingAsset = await assetsService.GetByIdAsync(assetId, cancellationToken);
        if (existingAsset is null)
        {
            return Results.NotFound();
        }

        AddAssetStatusConsistencyErrors(validationErrors, request.Status, existingAsset.CurrentAssignment is not null);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        if (!currentUserService.TryGetUserId(principal, out var actorUserId, out _))
        {
            return Results.Unauthorized();
        }

        var updatedAsset = new AssetDocument
        {
            Id = existingAsset.Id,
            AssetTag = request.AssetTag!.Trim(),
            SerialNumber = request.SerialNumber!.Trim(),
            Type = request.Type!.Trim(),
            Manufacturer = request.Manufacturer!.Trim(),
            Model = request.Model!.Trim(),
            Status = request.Status!.Trim(),
            Department = request.Department!.Trim(),
            Location = request.Location!.Trim(),
            PurchaseDate = NormalizeToUtc(request.PurchaseDate),
            WarrantyEndDate = NormalizeToUtc(request.WarrantyEndDate),
            EndOfLifeDate = NormalizeToUtc(request.EndOfLifeDate),
            CurrentAssignment = existingAsset.CurrentAssignment,
            Notes = request.Notes?.Trim(),
            CreatedAt = EnsureUtc(existingAsset.CreatedAt),
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            var mutationResult = await assetMutationService.ReplaceAsync(
                updatedAsset,
                actorUserId,
                operationContextService.GetClientIp(httpContext),
                operationContextService.GetUserAgent(httpContext),
                cancellationToken);
            if (mutationResult.NotFound)
            {
                return Results.NotFound();
            }
        }
        catch (MongoWriteException exception) when (AssetsService.IsDuplicateKey(exception))
        {
            return Results.Conflict(new
            {
                message = AssetsService.GetDuplicateKeyMessage(exception)
            });
        }

        return Results.Ok(MapResponse(updatedAsset));
    }

    private static async Task<IResult> DeleteAssetAsync(
        string id,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        CurrentUserService currentUserService,
        AssetMutationService assetMutationService,
        OperationContextService operationContextService,
        ReferenceIntegrityService referenceIntegrityService,
        CancellationToken cancellationToken)
    {
        if (!TryParseAssetId(id, out var assetId, out var validationResult))
        {
            return validationResult;
        }

        if (!currentUserService.TryGetUserId(principal, out var actorUserId, out _))
        {
            return Results.Unauthorized();
        }

        var referenceErrors = await referenceIntegrityService.ValidateAssetDeletionAsync(assetId, cancellationToken);
        if (referenceErrors.Count > 0)
        {
            return Results.ValidationProblem(referenceErrors);
        }

        var mutationResult = await assetMutationService.DeleteAsync(
            assetId,
            actorUserId,
            operationContextService.GetClientIp(httpContext),
            operationContextService.GetUserAgent(httpContext),
            cancellationToken);

        return mutationResult.NotFound ? Results.NotFound() : Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateRequest(CreateAssetRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringLengthError(errors, "assetTag", request.AssetTag, 3, 40);
        RequestValidation.AddRequiredStringLengthError(errors, "serialNumber", request.SerialNumber, 3, 80);
        RequestValidation.AddRequiredStringLengthError(errors, "type", request.Type, 1, 40);
        RequestValidation.AddAllowedValuesError(errors, "type", request.Type, RequestValidation.AssetTypes);
        RequestValidation.AddRequiredStringLengthError(errors, "manufacturer", request.Manufacturer, 1, 60);
        RequestValidation.AddRequiredStringLengthError(errors, "model", request.Model, 1, 80);
        RequestValidation.AddRequiredStringLengthError(errors, "status", request.Status, 1, 40);
        RequestValidation.AddAllowedValuesError(errors, "status", request.Status, RequestValidation.AssetStatuses);
        RequestValidation.AddRequiredStringLengthError(errors, "department", request.Department, 1, 80);
        RequestValidation.AddRequiredStringLengthError(errors, "location", request.Location, 1, 120);
        RequestValidation.AddRequiredDateError(errors, "purchaseDate", request.PurchaseDate);
        RequestValidation.AddRequiredDateError(errors, "warrantyEndDate", request.WarrantyEndDate);
        RequestValidation.AddRequiredDateError(errors, "endOfLifeDate", request.EndOfLifeDate);
        RequestValidation.AddOptionalStringMaxLengthError(errors, "notes", request.Notes, 500);
        AddCommonAssetBusinessRuleErrors(errors, request.PurchaseDate, request.WarrantyEndDate, request.EndOfLifeDate, request.CurrentAssignment);
        AddAssetStatusConsistencyErrors(errors, request.Status, false);

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRequest(UpdateAssetRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        RequestValidation.AddRequiredStringLengthError(errors, "assetTag", request.AssetTag, 3, 40);
        RequestValidation.AddRequiredStringLengthError(errors, "serialNumber", request.SerialNumber, 3, 80);
        RequestValidation.AddRequiredStringLengthError(errors, "type", request.Type, 1, 40);
        RequestValidation.AddAllowedValuesError(errors, "type", request.Type, RequestValidation.AssetTypes);
        RequestValidation.AddRequiredStringLengthError(errors, "manufacturer", request.Manufacturer, 1, 60);
        RequestValidation.AddRequiredStringLengthError(errors, "model", request.Model, 1, 80);
        RequestValidation.AddRequiredStringLengthError(errors, "status", request.Status, 1, 40);
        RequestValidation.AddAllowedValuesError(errors, "status", request.Status, RequestValidation.AssetStatuses);
        RequestValidation.AddRequiredStringLengthError(errors, "department", request.Department, 1, 80);
        RequestValidation.AddRequiredStringLengthError(errors, "location", request.Location, 1, 120);
        RequestValidation.AddRequiredDateError(errors, "purchaseDate", request.PurchaseDate);
        RequestValidation.AddRequiredDateError(errors, "warrantyEndDate", request.WarrantyEndDate);
        RequestValidation.AddRequiredDateError(errors, "endOfLifeDate", request.EndOfLifeDate);
        RequestValidation.AddOptionalStringMaxLengthError(errors, "notes", request.Notes, 500);
        AddCommonAssetBusinessRuleErrors(errors, request.PurchaseDate, request.WarrantyEndDate, request.EndOfLifeDate, request.CurrentAssignment);

        return errors;
    }

    private static bool TryParseAssetId(string rawId, out ObjectId assetId, out IResult validationResult)
    {
        if (ObjectId.TryParse(rawId, out assetId))
        {
            validationResult = Results.Empty;
            return true;
        }

        validationResult = Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["id"] = ["The asset id must be a valid MongoDB ObjectId."]
        });
        return false;
    }

    private static void AddCommonAssetBusinessRuleErrors(
        IDictionary<string, string[]> errors,
        DateTime purchaseDate,
        DateTime warrantyEndDate,
        DateTime endOfLifeDate,
        AssetAssignmentRequest? currentAssignment)
    {
        if (purchaseDate != default && warrantyEndDate != default && warrantyEndDate < purchaseDate)
        {
            errors["warrantyEndDate"] = ["warrantyEndDate must be on or after purchaseDate."];
        }

        if (warrantyEndDate != default && endOfLifeDate != default && endOfLifeDate < warrantyEndDate)
        {
            errors["endOfLifeDate"] = ["endOfLifeDate must be on or after warrantyEndDate."];
        }

        if (currentAssignment is not null)
        {
            errors["currentAssignment"] = ["currentAssignment is managed by the assignments API and cannot be set directly."];
        }
    }

    private static void AddAssetStatusConsistencyErrors(
        IDictionary<string, string[]> errors,
        string? status,
        bool hasDerivedCurrentAssignment)
    {
        var trimmedStatus = status?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedStatus))
        {
            return;
        }

        if (string.Equals(trimmedStatus, "Assigned", StringComparison.OrdinalIgnoreCase) && !hasDerivedCurrentAssignment)
        {
            errors["status"] = ["status cannot be set to Assigned directly. Create an assignment instead."];
            return;
        }

        if (string.Equals(trimmedStatus, "InStock", StringComparison.OrdinalIgnoreCase) && hasDerivedCurrentAssignment)
        {
            errors["status"] = ["status cannot be InStock while the asset has a current assignment."];
        }
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

    private static AssetResponse MapResponse(AssetDocument asset) =>
        new()
        {
            Id = asset.Id.ToString(),
            AssetTag = asset.AssetTag,
            SerialNumber = asset.SerialNumber,
            Type = asset.Type,
            Manufacturer = asset.Manufacturer,
            Model = asset.Model,
            Status = asset.Status,
            Department = asset.Department,
            Location = asset.Location,
            PurchaseDate = EnsureUtc(asset.PurchaseDate),
            WarrantyEndDate = EnsureUtc(asset.WarrantyEndDate),
            EndOfLifeDate = EnsureUtc(asset.EndOfLifeDate),
            CurrentAssignment = asset.CurrentAssignment is null
                ? null
                : new AssetAssignmentResponse
                {
                    UserId = asset.CurrentAssignment.UserId.ToString(),
                    AssignedOn = EnsureUtc(asset.CurrentAssignment.AssignedOn)
                },
            Notes = asset.Notes,
            CreatedAt = EnsureUtc(asset.CreatedAt),
            UpdatedAt = EnsureUtc(asset.UpdatedAt)
        };
}
