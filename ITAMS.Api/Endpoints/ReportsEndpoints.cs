using System.Globalization;
using ITAMS.Api.Authorization;
using ITAMS.Api.Contracts;
using ITAMS.Api.Services;
using ITAMS.Api.Validation;

namespace ITAMS.Api.Endpoints;

public static class ReportsEndpoints
{
    private const int MaxCustomRangeDays = 730;

    public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports");

        group.MapGet("/overview", GetReportsOverviewAsync)
            .WithName("GetReportsOverview")
            .RequireAuthorization(AuthorizationPolicies.ReportsRead);

        return app;
    }

    private static async Task<IResult> GetReportsOverviewAsync(
        string? preset,
        string? startDate,
        string? endDate,
        string? assetDepartment,
        string? userDepartment,
        ReportsService reportsService,
        CancellationToken cancellationToken)
    {
        var request = new ReportsOverviewRequest
        {
            Preset = preset,
            StartDate = startDate,
            EndDate = endDate,
            AssetDepartment = assetDepartment,
            UserDepartment = userDepartment
        };

        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var report = await reportsService.GetOverviewAsync(request, cancellationToken);
        return Results.Ok(report);
    }

    private static Dictionary<string, string[]> ValidateRequest(ReportsOverviewRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var preset = request.Preset?.Trim();

        if (!string.IsNullOrWhiteSpace(preset) &&
            !ReportsService.SupportedPresets.Contains(preset, StringComparer.OrdinalIgnoreCase))
        {
            errors["preset"] = [$"preset must be one of the following values: {string.Join(", ", ReportsService.SupportedPresets)}."];
        }

        RequestValidation.AddOptionalStringMaxLengthError(errors, "assetDepartment", request.AssetDepartment, 80);
        RequestValidation.AddOptionalStringMaxLengthError(errors, "userDepartment", request.UserDepartment, 80);

        if (!string.Equals(preset, ReportsService.CustomPreset, StringComparison.OrdinalIgnoreCase))
        {
            return errors;
        }

        DateOnly? parsedStartDate = null;
        DateOnly? parsedEndDate = null;

        if (string.IsNullOrWhiteSpace(request.StartDate))
        {
            errors["startDate"] = ["startDate is required when preset is custom."];
        }
        else if (!TryParseDateOnly(request.StartDate, out var parsedStartDateValue))
        {
            errors["startDate"] = ["startDate must use the yyyy-MM-dd format."];
        }
        else
        {
            parsedStartDate = parsedStartDateValue;
        }

        if (string.IsNullOrWhiteSpace(request.EndDate))
        {
            errors["endDate"] = ["endDate is required when preset is custom."];
        }
        else if (!TryParseDateOnly(request.EndDate, out var parsedEndDateValue))
        {
            errors["endDate"] = ["endDate must use the yyyy-MM-dd format."];
        }
        else
        {
            parsedEndDate = parsedEndDateValue;
        }

        if (parsedStartDate is not null &&
            parsedEndDate is not null &&
            parsedEndDate.Value < parsedStartDate.Value)
        {
            errors["endDate"] = ["endDate must be on or after startDate."];
        }
        else if (parsedStartDate is not null &&
                 parsedEndDate is not null &&
                 parsedEndDate.Value.DayNumber - parsedStartDate.Value.DayNumber > MaxCustomRangeDays)
        {
            errors["endDate"] = [$"custom report ranges cannot exceed {MaxCustomRangeDays} days."];
        }

        return errors;
    }

    private static bool TryParseDateOnly(string? value, out DateOnly? parsedValue)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            parsedValue = date;
            return true;
        }

        parsedValue = null;
        return false;
    }
}
