using System.Net;
using ITAMS.Api.Configuration;
using ITAMS.Api.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class CorsIntegrationTests(ApiIntegrationTestFixture fixture) : IClassFixture<ApiIntegrationTestFixture>
{
    [Fact]
    public async Task PreflightRequest_AllowsConfiguredFrontendOrigin()
    {
        using var client = fixture.CreateClient();
        using var request = CreatePreflightRequest("http://localhost:5173");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("GET", response.Headers.GetValues("Access-Control-Allow-Methods").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionPreflight_AllowsOnlyProductionOrigins()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var canonicalRequest = CreatePreflightRequest("https://itams.app");
        using var canonicalResponse = await client.SendAsync(canonicalRequest);
        Assert.Equal(HttpStatusCode.NoContent, canonicalResponse.StatusCode);
        Assert.Equal("https://itams.app", canonicalResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var wwwRequest = CreatePreflightRequest("https://www.itams.app");
        using var wwwResponse = await client.SendAsync(wwwRequest);
        Assert.Equal(HttpStatusCode.NoContent, wwwResponse.StatusCode);
        Assert.Equal("https://www.itams.app", wwwResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var localhostRequest = CreatePreflightRequest("http://localhost:5173");
        using var localhostResponse = await client.SendAsync(localhostRequest);
        Assert.Equal(HttpStatusCode.NoContent, localhostResponse.StatusCode);
        Assert.False(localhostResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private WebApplicationFactory<Program> CreateProductionFactory()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var mongoDbSettings = scope.ServiceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;

        return fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MongoDb:ConnectionString"] = mongoDbSettings.ConnectionString,
                    ["Security:GeneralRateLimit:PermitLimit"] = "1000"
                });
            });
        });
    }

    private static HttpRequestMessage CreatePreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/assets");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }
}
