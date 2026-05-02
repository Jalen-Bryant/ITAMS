using System.Net.Http.Json;
using ITAMS.Client.Models;

namespace ITAMS.Client.Services;

public sealed class AuthService(
    PublicApiClient publicApiClient,
    AuthorizedApiClient authorizedApiClient,
    AuthSessionService authSessionService)
{
    public async Task LoginAsync(LoginFormModel formModel, CancellationToken cancellationToken = default)
    {
        var request = new LoginRequest
        {
            Identifier = formModel.Identifier.Trim(),
            Password = formModel.Password
        };

        using var response = await publicApiClient.Client.PostAsJsonAsync(
            "auth/login",
            request,
            ApiResponseHelper.JsonOptions,
            cancellationToken);

        var loginResponse = await ApiResponseHelper.ReadAsync<LoginResponse>(response, cancellationToken);
        await authSessionService.SetSessionAsync(loginResponse);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (authSessionService.CurrentSession is not null)
            {
                using var response = await authorizedApiClient.Client.PostAsync("auth/logout", null, cancellationToken);
                _ = response.IsSuccessStatusCode;
            }
        }
        finally
        {
            await authSessionService.ClearSessionAsync();
        }
    }

    public async Task ChangePasswordAsync(ChangePasswordFormModel formModel, CancellationToken cancellationToken = default)
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = formModel.CurrentPassword,
            NewPassword = formModel.NewPassword
        };

        using var response = await authorizedApiClient.Client.PostAsJsonAsync(
            "auth/change-password",
            request,
            ApiResponseHelper.JsonOptions,
            cancellationToken);

        await ApiResponseHelper.EnsureSuccessAsync(response, cancellationToken);
    }
}
