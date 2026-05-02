using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ITAMS.Api.Contracts;
using ITAMS.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace ITAMS.Api.Tests.TestInfrastructure;

public abstract class ApiIntegrationTestBase(ApiIntegrationTestFixture fixture) : IClassFixture<ApiIntegrationTestFixture>, IAsyncLifetime
{
    private readonly HashSet<ObjectId> _trackedActorIds = [];
    private readonly HashSet<ObjectId> _trackedAssetIds = [];
    private readonly HashSet<ObjectId> _trackedAssignmentIds = [];
    private readonly HashSet<ObjectId> _trackedUserIds = [];
    private DateTime _testStartedAtUtc;

    protected ApiIntegrationTestFixture Fixture { get; } = fixture;

    public Task InitializeAsync()
    {
        _testStartedAtUtc = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await CleanupAsync();
    }

    protected async Task<LoginResponse> LoginAsBootstrapAdminAsync()
    {
        var login = await LoginAsync(AtlasApiFactory.BootstrapUsername, AtlasApiFactory.BootstrapPassword);
        _trackedActorIds.Add(ObjectId.Parse(login.User.Id));
        return login;
    }

    protected async Task<LoginResponse> LoginAsync(string identifier, string password)
    {
        using var client = Fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = identifier,
            Password = password
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(payload);
        _trackedActorIds.Add(ObjectId.Parse(payload.User.Id));
        return payload;
    }

    protected HttpClient CreateAuthenticatedClient(string accessToken, string? spoofedActorUserId = null)
    {
        var client = Fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (!string.IsNullOrWhiteSpace(spoofedActorUserId))
        {
            client.DefaultRequestHeaders.Add("X-Actor-User-Id", spoofedActorUserId);
        }

        return client;
    }

    protected async Task<UserResponse> CreateUserAsync(
        HttpClient adminClient,
        string role,
        string? password = null,
        bool isActive = true)
    {
        var uniquePrefix = CreateUniquePrefix(role, 18);
        var response = await adminClient.PostAsJsonAsync("/users", new CreateUserRequest
        {
            Username = $"{uniquePrefix}_user",
            DisplayName = $"{role} Test User",
            Email = $"{uniquePrefix}@city.example",
            Password = password ?? "TestPassword123!",
            Role = role,
            Department = "IT",
            IsActive = isActive
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(payload);
        _trackedUserIds.Add(ObjectId.Parse(payload.Id));
        return payload;
    }

    protected async Task<AssetResponse> CreateAssetAsync(HttpClient client, string? note = null)
    {
        var uniquePrefix = CreateUniquePrefix("asset", 12);
        var response = await client.PostAsJsonAsync("/assets", new CreateAssetRequest
        {
            AssetTag = $"AT-{uniquePrefix}",
            SerialNumber = $"SN-{uniquePrefix}",
            Type = "Laptop",
            Manufacturer = "Lenovo",
            Model = "ThinkPad T14",
            Status = "InStock",
            Department = "IT",
            Location = "API Integration Lab",
            PurchaseDate = DateTime.UtcNow.Date.AddDays(-30),
            WarrantyEndDate = DateTime.UtcNow.Date.AddYears(2),
            EndOfLifeDate = DateTime.UtcNow.Date.AddYears(4),
            CurrentAssignment = null,
            Notes = note ?? $"Asset {uniquePrefix}"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssetResponse>();
        Assert.NotNull(payload);
        _trackedAssetIds.Add(ObjectId.Parse(payload.Id));
        return payload;
    }

    protected async Task<AssignmentResponse> CreateAssignmentAsync(
        HttpClient client,
        string assetId,
        string userId,
        string? note = null)
    {
        var response = await client.PostAsJsonAsync("/assignments", new CreateAssignmentRequest
        {
            AssetId = assetId,
            UserId = userId,
            StartDate = DateTime.UtcNow.AddMinutes(-5),
            EndDate = null,
            Notes = note ?? $"Assignment {CreateUniquePrefix("assignment", 12)}"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<AssignmentResponse>();
        Assert.NotNull(payload);
        _trackedAssignmentIds.Add(ObjectId.Parse(payload.Id));
        return payload;
    }

    protected async Task<AuditLogDocument?> FindAuditLogAsync(ObjectId entityId, string action)
    {
        return await Fixture.AuditLogsCollection
            .Find(auditLog => auditLog.EntityId == entityId && auditLog.Action == action)
            .SortByDescending(auditLog => auditLog.Timestamp)
            .FirstOrDefaultAsync();
    }

    protected async Task<LifecycleEventDocument?> FindLifecycleEventAsync(ObjectId assetId, string eventType)
    {
        return await Fixture.LifecycleEventsCollection
            .Find(lifecycleEvent => lifecycleEvent.AssetId == assetId && lifecycleEvent.EventType == eventType)
            .SortByDescending(lifecycleEvent => lifecycleEvent.Timestamp)
            .FirstOrDefaultAsync();
    }

    protected static async Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    protected void TrackAssetId(ObjectId assetId) => _trackedAssetIds.Add(assetId);

    protected void TrackAssignmentId(ObjectId assignmentId) => _trackedAssignmentIds.Add(assignmentId);

    protected void TrackUserId(ObjectId userId) => _trackedUserIds.Add(userId);

    protected void TrackActorId(ObjectId actorId) => _trackedActorIds.Add(actorId);

    private async Task CleanupAsync()
    {
        var trackedEntityIds = _trackedUserIds
            .Concat(_trackedAssetIds)
            .Concat(_trackedAssignmentIds)
            .Distinct()
            .ToArray();

        var trackedActorIds = _trackedActorIds.Distinct().ToArray();
        var trackedAssetIds = _trackedAssetIds.Distinct().ToArray();
        var trackedAssignmentIds = _trackedAssignmentIds.Distinct().ToArray();
        var trackedUserIds = _trackedUserIds.Distinct().ToArray();

        if (trackedActorIds.Length > 0)
        {
            var sessionFilter = Builders<UserSessionDocument>.Filter.And(
                Builders<UserSessionDocument>.Filter.In(session => session.UserId, trackedActorIds),
                Builders<UserSessionDocument>.Filter.Gte(session => session.CreatedAt, _testStartedAtUtc));
            await Fixture.UserSessionsCollection.DeleteManyAsync(sessionFilter);
        }

        if (trackedEntityIds.Length > 0 || trackedActorIds.Length > 0)
        {
            var auditFilters = new List<FilterDefinition<AuditLogDocument>>
            {
                Builders<AuditLogDocument>.Filter.Gte(auditLog => auditLog.Timestamp, _testStartedAtUtc)
            };

            var identityFilters = new List<FilterDefinition<AuditLogDocument>>();
            if (trackedEntityIds.Length > 0)
            {
                identityFilters.Add(Builders<AuditLogDocument>.Filter.In(auditLog => auditLog.EntityId, trackedEntityIds));
            }

            if (trackedActorIds.Length > 0)
            {
                identityFilters.Add(Builders<AuditLogDocument>.Filter.In(auditLog => auditLog.ActorUserId, trackedActorIds));
            }

            auditFilters.Add(Builders<AuditLogDocument>.Filter.Or(identityFilters));
            await Fixture.AuditLogsCollection.DeleteManyAsync(Builders<AuditLogDocument>.Filter.And(auditFilters));
        }

        if (trackedAssetIds.Length > 0)
        {
            var lifecycleFilter = Builders<LifecycleEventDocument>.Filter.And(
                Builders<LifecycleEventDocument>.Filter.In(lifecycleEvent => lifecycleEvent.AssetId, trackedAssetIds),
                Builders<LifecycleEventDocument>.Filter.Gte(lifecycleEvent => lifecycleEvent.Timestamp, _testStartedAtUtc));
            await Fixture.LifecycleEventsCollection.DeleteManyAsync(lifecycleFilter);
        }

        if (trackedAssignmentIds.Length > 0)
        {
            await Fixture.AssignmentsCollection.DeleteManyAsync(
                Builders<AssignmentDocument>.Filter.In(assignment => assignment.Id, trackedAssignmentIds));
        }

        if (trackedAssetIds.Length > 0)
        {
            await Fixture.AssetsCollection.DeleteManyAsync(
                Builders<AssetDocument>.Filter.In(asset => asset.Id, trackedAssetIds));
        }

        if (trackedUserIds.Length > 0)
        {
            await Fixture.UsersCollection.DeleteManyAsync(
                Builders<UserDocument>.Filter.In(user => user.Id, trackedUserIds));
        }
    }

    private static string CreateUniquePrefix(string prefix, int maxLength)
    {
        var slug = $"{prefix}_{Guid.NewGuid():N}".ToLowerInvariant();
        return slug.Length <= maxLength ? slug : slug[..maxLength];
    }
}
