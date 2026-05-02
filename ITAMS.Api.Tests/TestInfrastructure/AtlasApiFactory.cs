using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ITAMS.Api.Tests.TestInfrastructure;

public sealed class AtlasApiFactory : WebApplicationFactory<Program>
{
    public const string BootstrapUsername = "bootstrap_api_tests";
    public const string BootstrapDisplayName = "Bootstrap API Tests";
    public const string BootstrapEmail = "bootstrap_api_tests@city.example";
    public const string BootstrapDepartment = "IT";
    public const string BootstrapPassword = "BootstrapAdminPass123!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "ThisIsATestJwtSigningKeyForITAMS123!",
                ["BootstrapAdmin:Username"] = BootstrapUsername,
                ["BootstrapAdmin:DisplayName"] = BootstrapDisplayName,
                ["BootstrapAdmin:Email"] = BootstrapEmail,
                ["BootstrapAdmin:Department"] = BootstrapDepartment,
                ["BootstrapAdmin:Password"] = BootstrapPassword
            });
        });
    }
}
