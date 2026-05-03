using System.Text.RegularExpressions;

namespace ITAMS.Api.Validation;

public static partial class RequestValidation
{
    public const int PasswordMinLength = 12;
    public const int PasswordMaxLength = 128;

    private static readonly string[] CommonPasswords =
    [
        "123456789012",
        "admin123456",
        "changeme123!",
        "letmein123!",
        "password",
        "password1",
        "password123",
        "password123!",
        "qwerty12345",
        "welcome123"
    ];

    public static readonly string[] AssetTypes =
    [
        "Laptop",
        "Desktop",
        "Monitor",
        "Mobile",
        "Tablet",
        "Dock",
        "Printer",
        "Server",
        "Scanner",
        "Peripheral"
    ];

    public static readonly string[] AssetStatuses =
    [
        "InStock",
        "Assigned",
        "Active",
        "InRepair",
        "Retired",
        "Lost"
    ];

    public static readonly string[] UserRoles =
    [
        "Admin",
        "Manager",
        "Technician",
        "User",
        "Auditor"
    ];

    public static readonly string[] AuditEntityTypes =
    [
        "Asset",
        "User",
        "Assignment",
        "LifecycleEvent"
    ];

    public static readonly string[] AuditActions =
    [
        "LOGIN",
        "LOGOUT",
        "CREATE",
        "READ",
        "UPDATE",
        "DELETE",
        "IMPORT",
        "EXPORT",
        "DISABLE",
        "ENABLE"
    ];

    public static readonly string[] LifecycleEventTypes =
    [
        "Registered",
        "Imaged",
        "Assigned",
        "Unassigned",
        "RepairOpened",
        "RepairClosed",
        "StatusChanged",
        "Retired",
        "LocationUpdated",
        "Updated"
    ];

    public static void AddRequiredStringError(
        IDictionary<string, string[]> errors,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        errors[key] = [$"{key} is required."];
    }

    public static void AddRequiredStringLengthError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        int minLength,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = [$"{key} is required."];
            return;
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length >= minLength && trimmedValue.Length <= maxLength)
        {
            return;
        }

        errors[key] = [$"{key} must be between {minLength} and {maxLength} characters."];
    }

    public static void AddOptionalStringMaxLengthError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        int maxLength)
    {
        if (value is null)
        {
            return;
        }

        if (value.Trim().Length <= maxLength)
        {
            return;
        }

        errors[key] = [$"{key} must be {maxLength} characters or fewer."];
    }

    public static void AddRequiredDateError(
        IDictionary<string, string[]> errors,
        string key,
        DateTime value)
    {
        if (value != default)
        {
            return;
        }

        errors[key] = [$"{key} is required."];
    }

    public static void AddRequiredBoolError(
        IDictionary<string, string[]> errors,
        string key,
        bool? value)
    {
        if (value is not null)
        {
            return;
        }

        errors[key] = [$"{key} is required."];
    }

    public static void AddAllowedValuesError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        IReadOnlyList<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmedValue = value.Trim();
        if (allowedValues.Contains(trimmedValue))
        {
            return;
        }

        errors[key] = [$"{key} must be one of the following values: {string.Join(", ", allowedValues)}."];
    }

    public static void AddEmailError(
        IDictionary<string, string[]> errors,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = [$"{key} is required."];
            return;
        }

        if (EmailRegex().IsMatch(value.Trim()))
        {
            return;
        }

        errors[key] = [$"{key} must be a valid email address."];
    }

    public static void AddRequiredCollectionError<T>(
        IDictionary<string, string[]> errors,
        string key,
        IReadOnlyCollection<T>? values)
    {
        if (values is not null)
        {
            return;
        }

        errors[key] = [$"{key} is required."];
    }

    public static void AddCollectionMinItemsError<T>(
        IDictionary<string, string[]> errors,
        string key,
        IReadOnlyCollection<T>? values,
        int minItems)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count >= minItems)
        {
            return;
        }

        errors[key] = [$"{key} must contain at least {minItems} item{(minItems == 1 ? string.Empty : "s")}."];
    }

    public static void AddPasswordError(
        IDictionary<string, string[]> errors,
        string key,
        string? value)
    {
        AddRequiredStringLengthError(errors, key, value, PasswordMinLength, PasswordMaxLength);
        if (errors.ContainsKey(key) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmedValue = value.Trim();
        if (CommonPasswords.Contains(trimmedValue, StringComparer.OrdinalIgnoreCase))
        {
            errors[key] = [$"{key} is too common. Choose a less predictable password."];
            return;
        }

        var categories = 0;
        if (trimmedValue.Any(char.IsLower))
        {
            categories++;
        }

        if (trimmedValue.Any(char.IsUpper))
        {
            categories++;
        }

        if (trimmedValue.Any(char.IsDigit))
        {
            categories++;
        }

        if (trimmedValue.Any(character => !char.IsLetterOrDigit(character)))
        {
            categories++;
        }

        if (categories < 3)
        {
            errors[key] = [$"{key} must include at least three of uppercase letters, lowercase letters, numbers, and symbols."];
        }
    }

    [GeneratedRegex(@"^\S+@\S+\.\S+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
