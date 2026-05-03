using ITAMS.Client;
using ITAMS.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"]?.Trim();
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = "https://localhost:7004/";
}

if (!apiBaseUrl.EndsWith("/", StringComparison.Ordinal))
{
    apiBaseUrl += "/";
}

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<BrowserSessionStorageService>();
builder.Services.AddScoped(sp => new PublicApiClient(new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl, UriKind.Absolute)
}));
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddScoped<AppAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AppAuthenticationStateProvider>());
builder.Services.AddScoped(sp =>
{
    var handler = new AuthMessageHandler(sp.GetRequiredService<AuthSessionService>())
    {
        InnerHandler = new HttpClientHandler()
    };

    return new AuthorizedApiClient(new HttpClient(handler)
    {
        BaseAddress = new Uri(apiBaseUrl, UriKind.Absolute)
    });
});
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AssetApiService>();
builder.Services.AddScoped<AssignmentApiService>();
builder.Services.AddScoped<UserApiService>();
builder.Services.AddScoped<HistoryApiService>();
builder.Services.AddScoped<ReportsApiService>();

var host = builder.Build();
_ = host.Services.GetRequiredService<AuthenticationStateProvider>();
await host.Services.GetRequiredService<AuthSessionService>().InitializeAsync();
await host.RunAsync();
