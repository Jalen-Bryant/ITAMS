namespace ITAMS.Api.Contracts;

public sealed class CreateAssetRequest
{
    public string? AssetTag { get; init; }

    public string? SerialNumber { get; init; }

    public string? Type { get; init; }

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? Status { get; init; }

    public string? Department { get; init; }

    public string? Location { get; init; }

    public DateTime PurchaseDate { get; init; }

    public DateTime WarrantyEndDate { get; init; }

    public DateTime EndOfLifeDate { get; init; }

    public AssetAssignmentRequest? CurrentAssignment { get; init; }

    public string? Notes { get; init; }
}
