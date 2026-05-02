using ITAMS.Api.Configuration;
using ITAMS.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace ITAMS.Api.Tests.TestInfrastructure;

public sealed class ApiIntegrationTestFixture : IAsyncLifetime
{
    public AtlasApiFactory Factory { get; } = new();

    public IMongoCollection<AssetDocument> AssetsCollection { get; private set; } = default!;
    public IMongoCollection<AssignmentDocument> AssignmentsCollection { get; private set; } = default!;
    public IMongoCollection<AuditLogDocument> AuditLogsCollection { get; private set; } = default!;
    public IMongoCollection<LifecycleEventDocument> LifecycleEventsCollection { get; private set; } = default!;
    public IMongoCollection<UserSessionDocument> UserSessionsCollection { get; private set; } = default!;
    public IMongoCollection<UserDocument> UsersCollection { get; private set; } = default!;

    public HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    public Task InitializeAsync()
    {
        _ = CreateClient();

        using var scope = Factory.Services.CreateScope();
        var mongoClient = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;
        var database = mongoClient.GetDatabase(settings.DatabaseName);

        AssetsCollection = database.GetCollection<AssetDocument>(settings.AssetsCollectionName);
        AssignmentsCollection = database.GetCollection<AssignmentDocument>(settings.AssignmentsCollectionName);
        AuditLogsCollection = database.GetCollection<AuditLogDocument>(settings.AuditLogsCollectionName);
        LifecycleEventsCollection = database.GetCollection<LifecycleEventDocument>(settings.LifecycleEventsCollectionName);
        UserSessionsCollection = database.GetCollection<UserSessionDocument>(settings.UserSessionsCollectionName);
        UsersCollection = database.GetCollection<UserDocument>(settings.UsersCollectionName);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
