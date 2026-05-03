using System.Net;
using System.Net.Http.Json;
using ITAMS.Api.Contracts;
using ITAMS.Api.Tests.TestInfrastructure;
using MongoDB.Bson;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class AuthenticationIntegrationTests(ApiIntegrationTestFixture fixture) : ApiIntegrationTestBase(fixture)
{
    [Fact]
    public async Task UserCreateRequiresPassword_And_UserCanLoginWithUsernameOrEmail()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var missingPasswordResponse = await adminClient.PostAsJsonAsync("/users", new
        {
            username = $"missing_password_{Guid.NewGuid():N}",
            displayName = "Missing Password User",
            email = $"missing_password_{Guid.NewGuid():N}@city.example",
            role = "User",
            department = "IT",
            isActive = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, missingPasswordResponse.StatusCode);

        var createdUser = await CreateUserAsync(adminClient, "User", "UserPassword123!");

        var usernameLogin = await LoginAsync(createdUser.Username, "UserPassword123!");
        var emailLogin = await LoginAsync(createdUser.Email, "UserPassword123!");

        using var userClient = CreateAuthenticatedClient(usernameLogin.AccessToken);
        var meResponse = await userClient.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Equal(createdUser.Id, usernameLogin.User.Id);
        Assert.Equal(createdUser.Id, emailLogin.User.Id);

        using var anonymousClient = Fixture.CreateClient();
        var badPasswordResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "WrongPassword123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, badPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesTokens_And_LogoutRevokesCurrentSession()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var client = Fixture.CreateClient();

        var refreshResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = adminLogin.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var refreshedLogin = await refreshResponse.Content.ReadFromJsonAsync<RefreshTokenResponse>();
        Assert.NotNull(refreshedLogin);
        Assert.NotEqual(adminLogin.RefreshToken, refreshedLogin.RefreshToken);
        Assert.NotEqual(adminLogin.AccessToken, refreshedLogin.AccessToken);

        var oldRefreshResponse = await client.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = adminLogin.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshResponse.StatusCode);

        using var refreshedClient = CreateAuthenticatedClient(refreshedLogin.AccessToken);
        var logoutResponse = await refreshedClient.PostAsync("/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var meResponse = await refreshedClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordRevokesExistingSessions_And_OldPasswordStopsWorking()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var createdUser = await CreateUserAsync(adminClient, "User", "OriginalPassword123!");
        var userLogin = await LoginAsync(createdUser.Username, "OriginalPassword123!");

        using var userClient = CreateAuthenticatedClient(userLogin.AccessToken);
        var changePasswordResponse = await userClient.PostAsJsonAsync("/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = "OriginalPassword123!",
            NewPassword = "UpdatedPassword123!"
        });

        Assert.Equal(HttpStatusCode.NoContent, changePasswordResponse.StatusCode);

        var meResponseWithOldToken = await userClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseWithOldToken.StatusCode);

        using var anonymousClient = Fixture.CreateClient();
        var oldPasswordResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "OriginalPassword123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);

        var newPasswordResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Email,
            Password = "UpdatedPassword123!"
        });

        Assert.Equal(HttpStatusCode.OK, newPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task AdminCanResetUserPassword_And_TargetSessionsAreRevoked()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var createdUser = await CreateUserAsync(adminClient, "User", "OriginalResetPass123!");
        var technician = await CreateUserAsync(adminClient, "Technician", "TechnicianResetPass123!");
        var userLogin = await LoginAsync(createdUser.Username, "OriginalResetPass123!");
        var technicianLogin = await LoginAsync(technician.Username, "TechnicianResetPass123!");

        using var technicianClient = CreateAuthenticatedClient(technicianLogin.AccessToken);
        var forbiddenResetResponse = await technicianClient.PostAsJsonAsync(
            $"/users/{createdUser.Id}/password",
            new ResetUserPasswordRequest
            {
                NewPassword = "BlockedResetPass123!"
            });

        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResetResponse.StatusCode);

        using var userClient = CreateAuthenticatedClient(userLogin.AccessToken);
        var resetPasswordResponse = await adminClient.PostAsJsonAsync(
            $"/users/{createdUser.Id}/password",
            new ResetUserPasswordRequest
            {
                NewPassword = "UpdatedResetPass123!"
            });

        Assert.Equal(HttpStatusCode.NoContent, resetPasswordResponse.StatusCode);

        var meResponseWithOldToken = await userClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseWithOldToken.StatusCode);

        using var anonymousClient = Fixture.CreateClient();
        var oldPasswordResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "OriginalResetPass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResponse.StatusCode);

        var newPasswordResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Email,
            Password = "UpdatedResetPass123!"
        });

        Assert.Equal(HttpStatusCode.OK, newPasswordResponse.StatusCode);

        var auditLog = await FindAuditLogAsync(ObjectId.Parse(createdUser.Id), "UPDATE");
        Assert.NotNull(auditLog);
        Assert.Equal(adminLogin.User.Id, auditLog!.ActorUserId.ToString());
        Assert.Contains("password reset", auditLog.Details?.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserRoleChangeRevokesExistingSessions_AndRefreshToken()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var createdUser = await CreateUserAsync(adminClient, "User", "RoleChangePass123!");
        var userLogin = await LoginAsync(createdUser.Username, "RoleChangePass123!");

        var updateResponse = await adminClient.PutAsJsonAsync($"/users/{createdUser.Id}", new UpdateUserRequest
        {
            Username = createdUser.Username,
            DisplayName = createdUser.DisplayName,
            Email = createdUser.Email,
            Role = "Auditor",
            Department = createdUser.Department,
            IsActive = createdUser.IsActive
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        using var userClient = CreateAuthenticatedClient(userLogin.AccessToken);
        var meResponseWithOldToken = await userClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseWithOldToken.StatusCode);

        using var anonymousClient = Fixture.CreateClient();
        var oldRefreshResponse = await anonymousClient.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = userLogin.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshResponse.StatusCode);

        var updatedLogin = await LoginAsync(createdUser.Username, "RoleChangePass123!");
        Assert.Equal("Auditor", updatedLogin.User.Role);
    }

    [Fact]
    public async Task UserActiveStatusChangeRevokesExistingSessions_AndDisabledUserCannotLogin()
    {
        var adminLogin = await LoginAsBootstrapAdminAsync();
        using var adminClient = CreateAuthenticatedClient(adminLogin.AccessToken);

        var createdUser = await CreateUserAsync(adminClient, "User", "StatusChangePass123!");
        var userLogin = await LoginAsync(createdUser.Username, "StatusChangePass123!");

        var disableResponse = await adminClient.PutAsJsonAsync($"/users/{createdUser.Id}", new UpdateUserRequest
        {
            Username = createdUser.Username,
            DisplayName = createdUser.DisplayName,
            Email = createdUser.Email,
            Role = createdUser.Role,
            Department = createdUser.Department,
            IsActive = false
        });

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        using var userClient = CreateAuthenticatedClient(userLogin.AccessToken);
        var meResponseWithOldToken = await userClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseWithOldToken.StatusCode);

        using var anonymousClient = Fixture.CreateClient();
        var oldRefreshResponse = await anonymousClient.PostAsJsonAsync("/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = userLogin.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshResponse.StatusCode);

        var disabledLoginResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "StatusChangePass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, disabledLoginResponse.StatusCode);

        var reenableResponse = await adminClient.PutAsJsonAsync($"/users/{createdUser.Id}", new UpdateUserRequest
        {
            Username = createdUser.Username,
            DisplayName = createdUser.DisplayName,
            Email = createdUser.Email,
            Role = createdUser.Role,
            Department = createdUser.Department,
            IsActive = true
        });

        Assert.Equal(HttpStatusCode.OK, reenableResponse.StatusCode);

        var meResponseWithRevokedToken = await userClient.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponseWithRevokedToken.StatusCode);

        var reenabledLoginResponse = await anonymousClient.PostAsJsonAsync("/auth/login", new LoginRequest
        {
            Identifier = createdUser.Username,
            Password = "StatusChangePass123!"
        });

        Assert.Equal(HttpStatusCode.OK, reenabledLoginResponse.StatusCode);
    }
}
