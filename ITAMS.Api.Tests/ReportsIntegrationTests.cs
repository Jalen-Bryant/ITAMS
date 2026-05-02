using System.Net;
using System.Net.Http.Json;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using ITAMS.Api.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class ReportsIntegrationTests(ApiIntegrationTestFixture fixture) : ApiIntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReportsOverview_UsesReportsAuthorizationPolicy()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var manager = await CreateUserAsync(adminClient, "Manager", "ManagerReportsPass123!");
        var auditor = await CreateUserAsync(adminClient, "Auditor", "AuditorReportsPass123!");
        var technician = await CreateUserAsync(adminClient, "Technician", "TechnicianReportsPass123!");
        var standardUser = await CreateUserAsync(adminClient, "User", "UserReportsPass123!");

        var managerLogin = await LoginAsync(manager.Username, "ManagerReportsPass123!");
        var auditorLogin = await LoginAsync(auditor.Username, "AuditorReportsPass123!");
        var technicianLogin = await LoginAsync(technician.Username, "TechnicianReportsPass123!");
        var standardUserLogin = await LoginAsync(standardUser.Username, "UserReportsPass123!");

        using var managerClient = CreateAuthenticatedClient(managerLogin.AccessToken);
        using var auditorClient = CreateAuthenticatedClient(auditorLogin.AccessToken);
        using var technicianClient = CreateAuthenticatedClient(technicianLogin.AccessToken);
        using var userClient = CreateAuthenticatedClient(standardUserLogin.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/reports/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync("/reports/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditorClient.GetAsync("/reports/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technicianClient.GetAsync("/reports/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/reports/overview")).StatusCode);
    }

    [Fact]
    public async Task ReportsOverview_DefaultPresetReturnsExpectedSectionsAndMonthlyGranularity()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var seed = await SeedReportDataAsync();

        var response = await adminClient.GetFromJsonAsync<ReportsOverviewResponse>(
            $"/reports/overview?assetDepartment={Uri.EscapeDataString(seed.AssetDepartmentA)}&userDepartment={Uri.EscapeDataString(seed.UserDepartmentA)}");

        Assert.NotNull(response);
        Assert.Equal(ReportsService.Last12MonthsPreset, response!.Filters.Preset);
        Assert.Equal(ReportsService.MonthlyGranularity, response.Filters.TimeGranularity);
        Assert.Equal(seed.AssetDepartmentA, response.Filters.AssetDepartment);
        Assert.Equal(seed.UserDepartmentA, response.Filters.UserDepartment);
        Assert.Contains(seed.AssetDepartmentA, response.AvailableFilters.AssetDepartments);
        Assert.Contains(seed.UserDepartmentA, response.AvailableFilters.UserDepartments);

        Assert.Equal(2, response.Kpis.TotalAssets);
        Assert.Equal(1, response.Kpis.OpenAssignments);
        Assert.Equal(1, response.Kpis.TotalUsers);
        Assert.Equal(2, response.Kpis.WarrantiesExpiringSoon);

        Assert.Contains(response.AssetsByStatus, item => item.Label == "Assigned" && item.Value == 1);
        Assert.Contains(response.AssetsByStatus, item => item.Label == "InStock" && item.Value == 1);
        Assert.Contains(response.AssetsByType, item => item.Label == "Laptop" && item.Value == 1);
        Assert.Contains(response.AssetsByType, item => item.Label == "Monitor" && item.Value == 1);
        Assert.Contains(response.UsersByRole, item => item.Label == "Manager" && item.Value == 1);
        Assert.Contains(response.UsersByDepartment, item => item.Label == seed.UserDepartmentA && item.Value == 1);

        Assert.Equal(1, response.AssignmentsOverTime.Sum(point => point.Value));
        Assert.Equal(2, response.WarrantyExpirationsOverTime.Sum(point => point.Value));
        Assert.Equal(1, response.LifecycleActivityOverTime.Sum(point => point.Value));
    }

    [Fact]
    public async Task ReportsOverview_CustomRangeAppliesAssetAndUserDepartmentFiltersIndependently()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var seed = await SeedReportDataAsync();
        const string startDate = "2026-02-01";
        const string endDate = "2026-02-28";

        var baseline = await adminClient.GetFromJsonAsync<ReportsOverviewResponse>(
            $"/reports/overview?preset=custom&startDate={startDate}&endDate={endDate}&assetDepartment={Uri.EscapeDataString(seed.AssetDepartmentA)}&userDepartment={Uri.EscapeDataString(seed.UserDepartmentA)}");
        var userShifted = await adminClient.GetFromJsonAsync<ReportsOverviewResponse>(
            $"/reports/overview?preset=custom&startDate={startDate}&endDate={endDate}&assetDepartment={Uri.EscapeDataString(seed.AssetDepartmentA)}&userDepartment={Uri.EscapeDataString(seed.UserDepartmentB)}");
        var assetShifted = await adminClient.GetFromJsonAsync<ReportsOverviewResponse>(
            $"/reports/overview?preset=custom&startDate={startDate}&endDate={endDate}&assetDepartment={Uri.EscapeDataString(seed.AssetDepartmentB)}&userDepartment={Uri.EscapeDataString(seed.UserDepartmentA)}");

        Assert.NotNull(baseline);
        Assert.NotNull(userShifted);
        Assert.NotNull(assetShifted);

        Assert.Equal(ReportsService.DailyGranularity, baseline!.Filters.TimeGranularity);
        Assert.Equal(2, baseline.Kpis.TotalAssets);
        Assert.Equal(1, baseline.Kpis.TotalUsers);
        Assert.Equal(1, baseline.AssignmentsOverTime.Sum(point => point.Value));
        Assert.Equal(2, baseline.WarrantyExpirationsOverTime.Sum(point => point.Value));
        Assert.Equal(1, baseline.LifecycleActivityOverTime.Sum(point => point.Value));
        Assert.Contains(baseline.AssignmentsOverTime, point => point.Label == "2026-02-05" && point.Value == 1);
        Assert.Contains(baseline.LifecycleActivityOverTime, point => point.Label == "2026-02-07" && point.Value == 1);

        Assert.Equal(baseline.Kpis.TotalAssets, userShifted!.Kpis.TotalAssets);
        Assert.Equal(baseline.Kpis.OpenAssignments, userShifted.Kpis.OpenAssignments);
        Assert.Equal(baseline.WarrantyExpirationsOverTime.Sum(point => point.Value), userShifted.WarrantyExpirationsOverTime.Sum(point => point.Value));
        Assert.Equal(2, userShifted.Kpis.TotalUsers);
        Assert.Contains(userShifted.UsersByDepartment, item => item.Label == seed.UserDepartmentB && item.Value == 2);

        Assert.Equal(1, assetShifted!.Kpis.TotalAssets);
        Assert.Equal(baseline.Kpis.TotalUsers, assetShifted.Kpis.TotalUsers);
        Assert.Equal(1, assetShifted.AssignmentsOverTime.Sum(point => point.Value));
        Assert.Equal(1, assetShifted.WarrantyExpirationsOverTime.Sum(point => point.Value));
        Assert.Equal(1, assetShifted.LifecycleActivityOverTime.Sum(point => point.Value));
        Assert.Contains(assetShifted.AssetsByStatus, item => item.Label == "Retired" && item.Value == 1);
    }

    [Fact]
    public async Task ReportsOverview_RejectsInvalidCustomRanges()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var response = await adminClient.GetAsync("/reports/overview?preset=custom&startDate=2026-03-01&endDate=2026-02-01");
        var problemDetails = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problemDetails);
        Assert.True(problemDetails!.Errors.ContainsKey("endDate"));
    }

    private async Task<ReportSeedResult> SeedReportDataAsync()
    {
        var assetDepartmentA = CreateUniqueName("reports-assets-a");
        var assetDepartmentB = CreateUniqueName("reports-assets-b");
        var userDepartmentA = CreateUniqueName("reports-users-a");
        var userDepartmentB = CreateUniqueName("reports-users-b");
        var createdAt = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var userA = CreateUserDocument("reports_mgr", "Reports Manager", "Manager", userDepartmentA, createdAt);
        var userB = CreateUserDocument("reports_usr", "Reports User", "User", userDepartmentB, createdAt);
        var userC = CreateUserDocument("reports_aud", "Reports Auditor", "Auditor", userDepartmentB, createdAt);

        TrackUserId(userA.Id);
        TrackUserId(userB.Id);
        TrackUserId(userC.Id);

        await Fixture.UsersCollection.InsertManyAsync([userA, userB, userC]);

        var assetA = CreateAssetDocument(
            "reports-asset-a",
            "Laptop",
            "InStock",
            assetDepartmentA,
            new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
            currentAssignment: null,
            createdAt);
        var assetB = CreateAssetDocument(
            "reports-asset-b",
            "Monitor",
            "Assigned",
            assetDepartmentA,
            new DateTime(2026, 2, 18, 0, 0, 0, DateTimeKind.Utc),
            new AssetAssignmentDocument
            {
                UserId = userA.Id,
                AssignedOn = new DateTime(2026, 2, 5, 8, 0, 0, DateTimeKind.Utc)
            },
            createdAt);
        var assetC = CreateAssetDocument(
            "reports-asset-c",
            "Server",
            "Retired",
            assetDepartmentB,
            new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc),
            currentAssignment: null,
            createdAt);

        TrackAssetId(assetA.Id);
        TrackAssetId(assetB.Id);
        TrackAssetId(assetC.Id);

        await Fixture.AssetsCollection.InsertManyAsync([assetA, assetB, assetC]);

        var assignmentA = new AssignmentDocument
        {
            Id = ObjectId.GenerateNewId(),
            AssetId = assetB.Id,
            UserId = userA.Id,
            AssignedByUserId = userA.Id,
            StartDate = new DateTime(2026, 2, 5, 8, 0, 0, DateTimeKind.Utc),
            EndDate = null,
            Notes = "Scoped assignment A",
            CreatedAt = new DateTime(2026, 2, 5, 8, 0, 0, DateTimeKind.Utc)
        };
        var assignmentB = new AssignmentDocument
        {
            Id = ObjectId.GenerateNewId(),
            AssetId = assetC.Id,
            UserId = userB.Id,
            AssignedByUserId = userA.Id,
            StartDate = new DateTime(2026, 2, 20, 9, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 2, 22, 9, 0, 0, DateTimeKind.Utc),
            Notes = "Scoped assignment B",
            CreatedAt = new DateTime(2026, 2, 20, 9, 0, 0, DateTimeKind.Utc)
        };

        TrackAssignmentId(assignmentA.Id);
        TrackAssignmentId(assignmentB.Id);

        await Fixture.AssignmentsCollection.InsertManyAsync([assignmentA, assignmentB]);

        await Fixture.LifecycleEventsCollection.InsertManyAsync(
        [
            new LifecycleEventDocument
            {
                Id = ObjectId.GenerateNewId(),
                AssetId = assetB.Id,
                Changes =
                [
                    new LifecycleEventChangeDocument
                    {
                        Field = "status",
                        OldValue = "InStock",
                        NewValue = new BsonString("Assigned")
                    }
                ],
                EventType = "Assigned",
                Notes = "Lifecycle event for asset department A",
                PerformedByUserId = userA.Id,
                Timestamp = new DateTime(2026, 2, 7, 10, 0, 0, DateTimeKind.Utc)
            },
            new LifecycleEventDocument
            {
                Id = ObjectId.GenerateNewId(),
                AssetId = assetC.Id,
                Changes =
                [
                    new LifecycleEventChangeDocument
                    {
                        Field = "status",
                        OldValue = "Assigned",
                        NewValue = new BsonString("Retired")
                    }
                ],
                EventType = "Retired",
                Notes = "Lifecycle event for asset department B",
                PerformedByUserId = userA.Id,
                Timestamp = new DateTime(2026, 2, 24, 15, 0, 0, DateTimeKind.Utc)
            }
        ]);

        return new ReportSeedResult(assetDepartmentA, assetDepartmentB, userDepartmentA, userDepartmentB);
    }

    private static UserDocument CreateUserDocument(
        string prefix,
        string displayName,
        string role,
        string department,
        DateTime createdAt)
    {
        var uniqueName = CreateUniqueName(prefix);

        return new UserDocument
        {
            Id = ObjectId.GenerateNewId(),
            Username = uniqueName,
            DisplayName = displayName,
            Email = $"{uniqueName}@city.example",
            NormalizedUsername = uniqueName.ToUpperInvariant(),
            NormalizedEmail = $"{uniqueName}@city.example".ToUpperInvariant(),
            PasswordHash = null,
            PasswordChangedAt = createdAt,
            Role = role,
            Department = department,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static AssetDocument CreateAssetDocument(
        string prefix,
        string type,
        string status,
        string department,
        DateTime warrantyEndDate,
        AssetAssignmentDocument? currentAssignment,
        DateTime createdAt)
    {
        var uniqueName = CreateUniqueName(prefix);

        return new AssetDocument
        {
            Id = ObjectId.GenerateNewId(),
            AssetTag = $"AT-{uniqueName}",
            SerialNumber = $"SN-{uniqueName}",
            Type = type,
            Manufacturer = "Contoso",
            Model = $"{type} Model",
            Status = status,
            Department = department,
            Location = "Reporting Lab",
            PurchaseDate = new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            WarrantyEndDate = warrantyEndDate,
            EndOfLifeDate = warrantyEndDate.AddYears(2),
            CurrentAssignment = currentAssignment,
            Notes = $"Seeded asset {uniqueName}",
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private static string CreateUniqueName(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)].ToLowerInvariant();

    private sealed record ReportSeedResult(
        string AssetDepartmentA,
        string AssetDepartmentB,
        string UserDepartmentA,
        string UserDepartmentB);
}
