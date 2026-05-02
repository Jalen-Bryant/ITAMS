namespace ITAMS.Api.Services;

public sealed class MutationResult<T>
{
    public T? Value { get; init; }

    public bool NotFound { get; init; }

    public Dictionary<string, string[]> Errors { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Success => !NotFound && Errors.Count == 0 && Value is not null;
}
