using System.Net;
using System.Net.Http.Json;
using ITAMS.Api.Contracts;
using ITAMS.Api.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class SecurityHardeningIntegrationTests(ApiIntegrationTestFixture fixture) : ApiIntegrationTestBase(fixture)
{
    [Fact]
    public async Task LoginFailuresUseGenericResponse_ForUnknownAndKnownUsers()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);
        var createdUser = await CreateUserAsync(adminClient, "User", "EnumerationPass123!");

        using var anonymousClient = Fixture.CreateClient();
        var knownUserFailure = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "WrongPassword123!"
        });
        var unknownUserFailure = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = $"missing_{Guid.NewGuid():N}",
            Password = "WrongPassword123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, knownUserFailure.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUserFailure.StatusCode);
        Assert.Equal(await ReadBodyAsync(knownUserFailure), await ReadBodyAsync(unknownUserFailure));
    }

    [Fact]
    public async Task RepeatedFailedLoginsLockAccount_AndCreateAuditEvent()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);
        var createdUser = await CreateUserAsync(adminClient, "User", "LockoutPass123!");
        var userId = ObjectId.Parse(createdUser.Id);

        using var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["Security:AuthLockout:MaxFailedAttempts"] = "2",
            ["Security:AuthLockout:BaseLockoutMinutes"] = "15",
            ["Security:AuthRateLimit:PermitLimit"] = "100",
            ["Security:GeneralRateLimit:PermitLimit"] = "1000"
        });
        using var client = CreateClient(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failedLogin = await client.PostAsJsonAsync("/auth/login", new LoginRequest
            {
                Identifier = createdUser.Username,
                Password = "WrongPassword123!"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
        }

        var correctPasswordAfterLockout = await client.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "LockoutPass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, correctPasswordAfterLockout.StatusCode);

        var lockedUser = await Fixture.UsersCollection
            .Find(user => user.Id == userId)
            .FirstOrDefaultAsync();
        Assert.NotNull(lockedUser);
        Assert.True(lockedUser!.LockoutEndAt > DateTime.UtcNow);
        Assert.Equal(2, lockedUser.FailedLoginCount);

        var lockoutAudit = await Fixture.AuditLogsCollection
            .Find(auditLog =>
                auditLog.EntityId == userId &&
                auditLog.Action == "LOGIN" &&
                auditLog.Details != null &&
                auditLog.Details.Result == "LOCKED_OUT")
            .FirstOrDefaultAsync();
        Assert.NotNull(lockoutAudit);
    }

    [Fact]
    public async Task AuthEndpointsAreRateLimited()
    {
        using var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["Security:AuthRateLimit:PermitLimit"] = "2",
            ["Security:AuthRateLimit:WindowSeconds"] = "60",
            ["Security:GeneralRateLimit:PermitLimit"] = "1000"
        });
        using var client = CreateClient(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest
            {
                Identifier = $"missing_{Guid.NewGuid():N}",
                Password = "WrongPassword123!"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var rateLimitedResponse = await client.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = $"missing_{Guid.NewGuid():N}",
            Password = "WrongPassword123!"
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);
    }

    [Fact]
    public async Task ApiResponsesIncludeSecurityHeaders()
    {
        using var client = Fixture.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        Assert.True(response.Headers.Contains("Content-Security-Policy-Report-Only"));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task CollectionEndpointsValidatePageSize_AndReturnCappedResults()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);
        var firstAsset = await CreateAssetAsync(adminClient, "Paged asset one");
        var secondAsset = await CreateAssetAsync(adminClient, "Paged asset two");

        var oneItemResponse = await adminClient.GetAsync("/assets?limit=1");
        var invalidLimitResponse = await adminClient.GetAsync("/assets?limit=501");

        Assert.Equal(HttpStatusCode.OK, oneItemResponse.StatusCode);
        var assets = await oneItemResponse.Content.ReadFromJsonAsync<List<AssetResponse>>();
        Assert.NotNull(assets);
        Assert.Single(assets!);
        Assert.Contains(assets[0].Id, new[] { firstAsset.Id, secondAsset.Id });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimitResponse.StatusCode);
    }

    [Fact]
    public async Task OversizedRequestBodiesAreRejectedBeforeEndpointProcessing()
    {
        using var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["Security:MaxRequestBodyBytes"] = "1024",
            ["Security:GeneralRateLimit:PermitLimit"] = "1000"
        });
        using var client = CreateClient(factory);

        using var content = new StringContent(new string('a', 2048));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/auth/login", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task StoredXssPayloadsRemainDataInApiResponses()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);
        const string payload = "<script>alert(1)</script>";
        var createdAsset = await CreateAssetAsync(adminClient, payload);

        var response = await adminClient.GetAsync($"/assets/{createdAsset.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>();
        Assert.NotNull(asset);
        Assert.Equal(payload, asset!.Notes);
    }

    private WebApplicationFactory<Program> CreateFactoryWithConfiguration(
        IReadOnlyDictionary<string, string?> configuration) =>
        Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(configuration);
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
}
