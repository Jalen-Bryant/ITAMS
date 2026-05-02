using System.Net;
using ITAMS.Api.Tests.TestInfrastructure;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class CorsIntegrationTests(ApiIntegrationTestFixture fixture) : IClassFixture<ApiIntegrationTestFixture>
{
    [Fact]
    public async Task PreflightRequest_AllowsConfiguredFrontendOrigin()
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/assets");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("GET", response.Headers.GetValues("Access-Control-Allow-Methods").Single(), StringComparison.OrdinalIgnoreCase);
    }
}
