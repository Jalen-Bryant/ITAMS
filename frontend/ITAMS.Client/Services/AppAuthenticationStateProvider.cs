using Microsoft.AspNetCore.Components.Authorization;

namespace ITAMS.Client.Services;

public sealed class AppAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthSessionService _authSessionService;

    public AppAuthenticationStateProvider(AuthSessionService authSessionService)
    {
        _authSessionService = authSessionService;
        _authSessionService.SessionChanged += HandleSessionChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_authSessionService.CreatePrincipal()));

    public void Dispose() => _authSessionService.SessionChanged -= HandleSessionChanged;

    private void HandleSessionChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
