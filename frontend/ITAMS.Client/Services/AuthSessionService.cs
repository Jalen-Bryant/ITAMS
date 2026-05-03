using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using ITAMS.Client.Models;
using Microsoft.AspNetCore.Components;

namespace ITAMS.Client.Services;

public sealed class AuthSessionService(
    BrowserSessionStorageService browserSessionStorageService,
    PublicApiClient publicApiClient,
    NavigationManager navigationManager)
{
    private const string StorageKey = "itams.auth.session";
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public event Action? SessionChanged;

    public AuthSession? CurrentSession { get; private set; }

    public async Task InitializeAsync()
    {
        CurrentSession = await browserSessionStorageService.GetAsync<AuthSession>(StorageKey);
        if (CurrentSession is null)
        {
            NotifySessionChanged();
            return;
        }

        if (CurrentSession.RefreshTokenExpiresAt <= DateTime.UtcNow)
        {
            await ClearSessionAsync(false);
            return;
        }

        if (CurrentSession.AccessTokenExpiresAt <= DateTime.UtcNow.AddSeconds(30))
        {
            if (!await TryRefreshAsync(CancellationToken.None))
            {
                await ClearSessionAsync(false);
            }

            return;
        }

        if (!await TryLoadCurrentUserAsync(CancellationToken.None))
        {
            await ClearSessionAsync(false);
            return;
        }

        NotifySessionChanged();
    }

    public ClaimsPrincipal CreatePrincipal()
    {
        var user = CurrentSession?.User;
        if (user is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("username", user.Username),
            new("department", user.Department)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ITAMS"));
    }

    public async Task SetSessionAsync(LoginResponse response)
    {
        CurrentSession = new AuthSession
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            AccessTokenExpiresAt = response.AccessTokenExpiresAt.ToUniversalTime(),
            RefreshTokenExpiresAt = response.RefreshTokenExpiresAt.ToUniversalTime(),
            User = response.User
        };

        await PersistAsync();
        NotifySessionChanged();
    }

    public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (CurrentSession is null)
            {
                return false;
            }

            if (CurrentSession.AccessTokenExpiresAt > DateTime.UtcNow.AddSeconds(30) &&
                CurrentSession.User is not null)
            {
                return true;
            }

            if (CurrentSession.RefreshTokenExpiresAt <= DateTime.UtcNow)
            {
                return false;
            }

            using var response = await publicApiClient.Client.PostAsJsonAsync(
                "auth/refresh",
                new RefreshTokenRequest { RefreshToken = CurrentSession.RefreshToken },
                ApiResponseHelper.JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var refreshResponse = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(
                ApiResponseHelper.JsonOptions,
                cancellationToken);

            if (refreshResponse is null)
            {
                return false;
            }

            CurrentSession.AccessToken = refreshResponse.AccessToken;
            CurrentSession.RefreshToken = refreshResponse.RefreshToken;
            CurrentSession.AccessTokenExpiresAt = refreshResponse.AccessTokenExpiresAt.ToUniversalTime();
            CurrentSession.RefreshTokenExpiresAt = refreshResponse.RefreshTokenExpiresAt.ToUniversalTime();

            if (!await TryLoadCurrentUserAsync(cancellationToken))
            {
                return false;
            }

            await PersistAsync();
            NotifySessionChanged();
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task ClearSessionAsync(bool navigateToLogin = true)
    {
        CurrentSession = null;
        await browserSessionStorageService.RemoveAsync(StorageKey);
        NotifySessionChanged();

        if (navigateToLogin)
        {
            navigationManager.NavigateTo("/login", true);
        }
    }

    private async Task<bool> TryLoadCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (CurrentSession is null || string.IsNullOrWhiteSpace(CurrentSession.AccessToken))
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CurrentSession.AccessToken);

        using var response = await publicApiClient.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var user = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(
            ApiResponseHelper.JsonOptions,
            cancellationToken);

        if (user is null)
        {
            return false;
        }

        CurrentSession.User = user;
        await PersistAsync();
        return true;
    }

    private async Task PersistAsync()
    {
        if (CurrentSession is null)
        {
            await browserSessionStorageService.RemoveAsync(StorageKey);
            return;
        }

        await browserSessionStorageService.SetAsync(StorageKey, CurrentSession);
    }

    private void NotifySessionChanged() => SessionChanged?.Invoke();
}
