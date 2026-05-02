using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace ITAMS.Client.Models;

public sealed class LoginFormModel
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Identifier { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordFormModel
{
    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and verification password must match.")]
    public string VerifyPassword { get; set; } = string.Empty;
}

public sealed class AssetEditorModel : IValidatableObject
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(40, MinimumLength = 3)]
    public string AssetTag { get; set; } = string.Empty;

    [Required]
    [StringLength(80, MinimumLength = 3)]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    public string Type { get; set; } = "Laptop";

    [Required]
    [StringLength(60, MinimumLength = 1)]
    public string Manufacturer { get; set; } = string.Empty;

    [Required]
    [StringLength(80, MinimumLength = 1)]
    public string Model { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = "InStock";

    [Required]
    [StringLength(80, MinimumLength = 1)]
    public string Department { get; set; } = string.Empty;

    [Required]
    [StringLength(120, MinimumLength = 1)]
    public string Location { get; set; } = string.Empty;

    [Required]
    public DateOnly PurchaseDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required]
    public DateOnly WarrantyEndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddYears(1));

    [Required]
    public DateOnly EndOfLifeDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddYears(3));

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsExisting => !string.IsNullOrWhiteSpace(Id);

    public AssetRequest ToRequest() =>
        new()
        {
            AssetTag = AssetTag.Trim(),
            SerialNumber = SerialNumber.Trim(),
            Type = Type,
            Manufacturer = Manufacturer.Trim(),
            Model = Model.Trim(),
            Status = Status,
            Department = Department.Trim(),
            Location = Location.Trim(),
            PurchaseDate = ToUtcDate(PurchaseDate),
            WarrantyEndDate = ToUtcDate(WarrantyEndDate),
            EndOfLifeDate = ToUtcDate(EndOfLifeDate),
            CurrentAssignment = null,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AppCatalogs.AssetTypes.Contains(Type, StringComparer.Ordinal))
        {
            yield return new ValidationResult("Select a valid asset type.", [nameof(Type)]);
        }

        if (!AppCatalogs.AssetStatuses.Contains(Status, StringComparer.Ordinal))
        {
            yield return new ValidationResult("Select a valid asset status.", [nameof(Status)]);
        }

        if (WarrantyEndDate < PurchaseDate)
        {
            yield return new ValidationResult("Warranty end date must be on or after purchase date.", [nameof(WarrantyEndDate)]);
        }

        if (EndOfLifeDate < WarrantyEndDate)
        {
            yield return new ValidationResult("End-of-life date must be on or after warranty end date.", [nameof(EndOfLifeDate)]);
        }
    }

    public static AssetEditorModel CreateEmpty() => new();

    public static AssetEditorModel FromResponse(AssetResponse asset) =>
        new()
        {
            Id = asset.Id,
            AssetTag = asset.AssetTag,
            SerialNumber = asset.SerialNumber,
            Type = asset.Type,
            Manufacturer = asset.Manufacturer,
            Model = asset.Model,
            Status = asset.Status,
            Department = asset.Department,
            Location = asset.Location,
            PurchaseDate = DateOnly.FromDateTime(asset.PurchaseDate),
            WarrantyEndDate = DateOnly.FromDateTime(asset.WarrantyEndDate),
            EndOfLifeDate = DateOnly.FromDateTime(asset.EndOfLifeDate),
            Notes = asset.Notes
        };

    private static DateTime ToUtcDate(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}

public sealed class AssignmentEditorModel : IValidatableObject
{
    public string Id { get; set; } = string.Empty;

    [Required]
    public string AssetId { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? EndDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsExisting => !string.IsNullOrWhiteSpace(Id);

    public AssignmentRequest ToRequest() =>
        new()
        {
            AssetId = AssetId.Trim(),
            UserId = UserId.Trim(),
            StartDate = ToUtcDate(StartDate),
            EndDate = EndDate is null ? null : ToUtcDate(EndDate.Value),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
        };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate is not null && EndDate.Value < StartDate)
        {
            yield return new ValidationResult("End date must be on or after start date.", [nameof(EndDate)]);
        }
    }

    public static AssignmentEditorModel CreateEmpty() => new();

    public static AssignmentEditorModel FromResponse(AssignmentResponse assignment) =>
        new()
        {
            Id = assignment.Id,
            AssetId = assignment.AssetId,
            UserId = assignment.UserId,
            StartDate = DateOnly.FromDateTime(assignment.StartDate),
            EndDate = assignment.EndDate is null ? null : DateOnly.FromDateTime(assignment.EndDate.Value),
            Notes = assignment.Notes
        };

    private static DateTime ToUtcDate(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}

public sealed class UserCreateFormModel
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "User";

    [Required]
    [StringLength(80, MinimumLength = 1)]
    public string Department { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public CreateUserRequest ToRequest() =>
        new()
        {
            Username = Username.Trim(),
            DisplayName = DisplayName.Trim(),
            Email = Email.Trim(),
            Password = Password,
            Role = Role,
            Department = Department.Trim(),
            IsActive = IsActive
        };

    public static UserCreateFormModel CreateEmpty() => new();
}

public sealed class UserEditFormModel
{
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "User";

    [Required]
    [StringLength(80, MinimumLength = 1)]
    public string Department { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public UpdateUserRequest ToRequest() =>
        new()
        {
            Username = Username.Trim(),
            DisplayName = DisplayName.Trim(),
            Email = Email.Trim(),
            Role = Role,
            Department = Department.Trim(),
            IsActive = IsActive
        };

    public static UserEditFormModel CreateEmpty() => new();

    public static UserEditFormModel FromResponse(UserResponse user) =>
        new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            Department = user.Department,
            IsActive = user.IsActive
        };
}

public sealed class ReportsFilterFormModel
{
    public string Preset { get; set; } = ReportPresets.Last12Months;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string AssetDepartment { get; set; } = string.Empty;
    public string UserDepartment { get; set; } = string.Empty;

    public ReportsOverviewQuery ToQuery() =>
        new()
        {
            Preset = Preset,
            StartDate = string.Equals(Preset, ReportPresets.Custom, StringComparison.Ordinal)
                ? FormatDate(StartDate)
                : null,
            EndDate = string.Equals(Preset, ReportPresets.Custom, StringComparison.Ordinal)
                ? FormatDate(EndDate)
                : null,
            AssetDepartment = string.IsNullOrWhiteSpace(AssetDepartment) ? null : AssetDepartment.Trim(),
            UserDepartment = string.IsNullOrWhiteSpace(UserDepartment) ? null : UserDepartment.Trim()
        };

    public static ReportsFilterFormModel CreateDefault() => new();

    public static ReportsFilterFormModel FromQuery(
        string? preset,
        string? startDate,
        string? endDate,
        string? assetDepartment,
        string? userDepartment) =>
        new()
        {
            Preset = ReportPresets.IsSupported(preset) ? preset!.Trim() : ReportPresets.Last12Months,
            StartDate = ParseDate(startDate),
            EndDate = ParseDate(endDate),
            AssetDepartment = assetDepartment?.Trim() ?? string.Empty,
            UserDepartment = userDepartment?.Trim() ?? string.Empty
        };

    public static ReportsFilterFormModel FromResponse(ReportsFilterStateResponse filters) =>
        new()
        {
            Preset = filters.Preset,
            StartDate = DateOnly.FromDateTime(filters.StartDate),
            EndDate = DateOnly.FromDateTime(filters.EndDate),
            AssetDepartment = filters.AssetDepartment ?? string.Empty,
            UserDepartment = filters.UserDepartment ?? string.Empty
        };

    private static DateOnly? ParseDate(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate)
            ? parsedDate
            : null;

    private static string? FormatDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
