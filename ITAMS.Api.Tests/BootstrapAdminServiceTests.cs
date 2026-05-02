using ITAMS.Api.Configuration;
using ITAMS.Api.Services;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class BootstrapAdminServiceTests
{
    [Fact]
    public void EnsureBootstrapCanRun_Throws_WhenNoLoginCapableUsersAndConfigurationIsMissing()
    {
        var settings = new BootstrapAdminSettings();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BootstrapAdminService.EnsureBootstrapCanRun(0, settings));

        Assert.Contains("Configure BootstrapAdmin settings", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureBootstrapCanRun_DoesNotThrow_WhenBootstrapConfigurationIsPresent()
    {
        var settings = new BootstrapAdminSettings
        {
            Username = "bootstrap_api_tests",
            DisplayName = "Bootstrap API Tests",
            Email = "bootstrap_api_tests@city.example",
            Department = "IT",
            Password = "BootstrapAdminPass123!"
        };

        BootstrapAdminService.EnsureBootstrapCanRun(0, settings);
    }
}
