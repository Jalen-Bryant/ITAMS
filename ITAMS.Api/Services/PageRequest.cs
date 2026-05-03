namespace ITAMS.Api.Services;

public readonly record struct PageRequest(int Offset, int Limit)
{
    public const int DefaultLimit = 250;
    public const int MaxLimit = 500;

    public static bool TryCreate(
        int? offset,
        int? limit,
        out PageRequest pageRequest,
        out Dictionary<string, string[]> validationErrors)
    {
        validationErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var resolvedOffset = offset ?? 0;
        var resolvedLimit = limit ?? DefaultLimit;

        if (resolvedOffset < 0)
        {
            validationErrors["offset"] = ["offset must be 0 or greater."];
        }

        if (resolvedLimit is < 1 or > MaxLimit)
        {
            validationErrors["limit"] = [$"limit must be between 1 and {MaxLimit}."];
        }

        pageRequest = validationErrors.Count == 0
            ? new PageRequest(resolvedOffset, resolvedLimit)
            : default;

        return validationErrors.Count == 0;
    }
}
