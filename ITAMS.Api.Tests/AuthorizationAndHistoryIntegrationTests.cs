using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ITAMS.Api.Contracts;
using ITAMS.Api.Tests.TestInfrastructure;
using MongoDB.Bson;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class AuthorizationAndHistoryIntegrationTests(ApiIntegrationTestFixture fixture) : ApiIntegrationTestBase(fixture)
{
    [Fact]
    public async Task RoleBasedAuthorizationIsEnforced()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var technician = await CreateUserAsync(adminClient, "Technician", "TechnicianPass123!");
        var auditor = await CreateUserAsync(adminClient, "Auditor", "AuditorPass123!");
        var standardUser = await CreateUserAsync(adminClient, "User", "StandardPass123!");

        var technicianLogin = await LoginAsync(technician.Username, "TechnicianPass123!");
        var auditorLogin = await LoginAsync(auditor.Username, "AuditorPass123!");
        var userLogin = await LoginAsync(standardUser.Username, "StandardPass123!");

        using var technicianClient = CreateAuthenticatedClient(technicianLogin.AccessToken);
        using var auditorClient = CreateAuthenticatedClient(auditorLogin.AccessToken);
        using var userClient = CreateAuthenticatedClient(userLogin.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await technicianClient.GetAsync("/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await technicianClient.GetAsync("/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await technicianClient.GetAsync("/assets")).StatusCode);

        var technicianAsset = await CreateAssetAsync(technicianClient, "Technician-owned test asset");
        Assert.False(string.IsNullOrWhiteSpace(technicianAsset.Id));

        Assert.Equal(HttpStatusCode.OK, (await auditorClient.GetAsync("/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await auditorClient.GetAsync("/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditorClient.PostAsJsonAsync("/assets", new CreateAssetRequest
        {
            AssetTag = $"AT-FORBIDDEN-{Guid.NewGuid():N}",
            SerialNumber = $"SN-FORBIDDEN-{Guid.NewGuid():N}",
            Type = "Laptop",
            Manufacturer = "Lenovo",
            Model = "ThinkPad T14",
            Status = "InStock",
            Department = "IT",
            Location = "Unauthorized",
            PurchaseDate = DateTime.UtcNow.Date.AddDays(-1),
            WarrantyEndDate = DateTime.UtcNow.Date.AddYears(1),
            EndOfLifeDate = DateTime.UtcNow.Date.AddYears(3),
            CurrentAssignment = null,
            Notes = "Should be forbidden"
        })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/assets")).StatusCode);
    }

    [Fact]
    public async Task HistoryWriteRoutesAreReadOnly_And_ActorIdentityComesFromJwtClaims()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var manager = await CreateUserAsync(adminClient, "Manager", "ManagerPass123!");
        var assignee = await CreateUserAsync(adminClient, "User", "AssigneePass123!");

        var managerLogin = await LoginAsync(manager.Username, "ManagerPass123!");
        using var managerClient = CreateAuthenticatedClient(managerLogin.AccessToken, spoofedActorUserId: assignee.Id);

        var asset = await CreateAssetAsync(managerClient, "Server-owned history asset");
        var assignment = await CreateAssignmentAsync(managerClient, asset.Id, assignee.Id, "Server-owned history assignment");

        var assetCreateAudit = await FindAuditLogAsync(ObjectId.Parse(asset.Id), "CREATE");
        var assignmentCreateAudit = await FindAuditLogAsync(ObjectId.Parse(assignment.Id), "CREATE");
        var registeredLifecycle = await FindLifecycleEventAsync(ObjectId.Parse(asset.Id), "Registered");
        var assignedLifecycle = await FindLifecycleEventAsync(ObjectId.Parse(asset.Id), "Assigned");

        Assert.NotNull(assetCreateAudit);
        Assert.NotNull(assignmentCreateAudit);
        Assert.NotNull(registeredLifecycle);
        Assert.NotNull(assignedLifecycle);
        Assert.Equal(manager.Id, assetCreateAudit!.ActorUserId.ToString());
        Assert.Equal(manager.Id, assignmentCreateAudit!.ActorUserId.ToString());
        Assert.Equal(manager.Id, registeredLifecycle!.PerformedByUserId.ToString());
        Assert.Equal(manager.Id, assignedLifecycle!.PerformedByUserId.ToString());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await adminClient.PostAsJsonAsync("/audit-logs", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await adminClient.PutAsJsonAsync($"/audit-logs/{assetCreateAudit.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await adminClient.DeleteAsync($"/audit-logs/{assetCreateAudit.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await adminClient.PostAsJsonAsync("/lifecycle-events", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await adminClient.PutAsJsonAsync($"/lifecycle-events/{registeredLifecycle.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await adminClient.DeleteAsync($"/lifecycle-events/{registeredLifecycle.Id}")).StatusCode);
    }

    [Fact]
    public async Task SwaggerDocumentMarksProtectedEndpointsWithBearerSecurity()
    {
        using var client = Fixture.CreateClient();

        var swaggerDocument = await client.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json");

        Assert.NotNull(swaggerDocument);

        var paths = swaggerDocument["paths"]!.AsObject();
        var topLevelSecurity = swaggerDocument["security"]!.AsArray();
        var loginPost = paths["/auth/login"]!["post"]!.AsObject();

        Assert.True(topLevelSecurity.Count > 0);
        Assert.NotNull(topLevelSecurity[0]!["Bearer"]);
        Assert.NotNull(loginPost["security"]);
        Assert.Empty(loginPost["security"]!.AsArray());
    }
}
