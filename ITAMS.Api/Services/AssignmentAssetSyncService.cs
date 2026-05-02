using ITAMS.Api.Models;
using MongoDB.Bson;

namespace ITAMS.Api.Services;

public sealed class AssignmentAssetSyncService(
    AssignmentsService assignmentsService,
    AssetsService assetsService)
{
    public async Task SyncAssetCurrentAssignmentAsync(
        ObjectId assetId,
        CancellationToken cancellationToken = default)
    {
        var currentAssignment = await assignmentsService.GetCurrentByAssetIdAsync(
            assetId,
            DateTime.UtcNow,
            cancellationToken);

        var assetAssignment = currentAssignment is null
            ? null
            : new AssetAssignmentDocument
            {
                UserId = currentAssignment.UserId,
                AssignedOn = EnsureUtc(currentAssignment.StartDate)
            };

        await assetsService.SetCurrentAssignmentAsync(assetId, assetAssignment, cancellationToken);
    }

    public async Task SyncAssetCurrentAssignmentsAsync(
        IEnumerable<ObjectId> assetIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var assetId in assetIds.Distinct())
        {
            await SyncAssetCurrentAssignmentAsync(assetId, cancellationToken);
        }
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
