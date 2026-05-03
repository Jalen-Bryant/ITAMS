using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ITAMS.Client.Models;

namespace ITAMS.Client.Services;

public sealed class PublicApiClient(HttpClient client)
{
    public HttpClient Client { get; } = client;
}

public sealed class AuthorizedApiClient(HttpClient client)
{
    public HttpClient Client { get; } = client;
}

public static class ApiResponseHelper
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new ApiException(response.StatusCode, "The server returned an empty response.");
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await CreateExceptionAsync(response, cancellationToken);
    }

    private static async Task<ApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                var validationProblem = JsonSerializer.Deserialize<ValidationProblemResponse>(content, JsonOptions);
                if (validationProblem?.Errors?.Count > 0)
                {
                    return new ApiException(
                        response.StatusCode,
                        validationProblem.Title ?? validationProblem.Detail ?? "The request failed validation.",
                        validationProblem.Errors);
                }
            }
            catch (JsonException)
            {
            }

            try
            {
                var messageResponse = JsonSerializer.Deserialize<MessageResponse>(content, JsonOptions);
                if (!string.IsNullOrWhiteSpace(messageResponse?.Message))
                {
                    return new ApiException(response.StatusCode, messageResponse.Message);
                }
            }
            catch (JsonException)
            {
            }

            return new ApiException(response.StatusCode, content.Trim());
        }

        var defaultMessage = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session is no longer valid.",
            HttpStatusCode.Forbidden => "You do not have permission to perform this action.",
            HttpStatusCode.NotFound => "The requested record could not be found.",
            _ => "The request could not be completed."
        };

        return new ApiException(response.StatusCode, defaultMessage);
    }
}

public sealed class AuthMessageHandler(AuthSessionService authSessionService) : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<bool> RetryKey = new("ITAMS.Client.AuthRetry");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var originalRequest = await request.CloneAsync(cancellationToken);
        ApplyAuthorization(request, authSessionService.CurrentSession?.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized ||
            request.Options.TryGetValue(RetryKey, out var hasRetried) && hasRetried ||
            authSessionService.CurrentSession is null)
        {
            return response;
        }

        response.Dispose();
        if (!await authSessionService.TryRefreshAsync(cancellationToken))
        {
            await authSessionService.ClearSessionAsync();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = originalRequest
            };
        }

        var retryRequest = await originalRequest.CloneAsync(cancellationToken);
        retryRequest.Options.Set(RetryKey, true);
        ApplyAuthorization(retryRequest, authSessionService.CurrentSession?.AccessToken);

        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private static void ApplyAuthorization(HttpRequestMessage request, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}

internal static class HttpRequestMessageExtensions
{
    public static async Task<HttpRequestMessage> CloneAsync(this HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        clone.Version = request.Version;
        clone.VersionPolicy = request.VersionPolicy;
        return clone;
    }
}

public sealed class AssetApiService(AuthorizedApiClient apiClient)
{
    public async Task<List<AssetResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.GetAsync("assets", cancellationToken);
        return await ApiResponseHelper.ReadAsync<List<AssetResponse>>(response, cancellationToken);
    }

    public async Task<AssetResponse> CreateAsync(AssetRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PostAsJsonAsync("assets", request, ApiResponseHelper.JsonOptions, cancellationToken);
        return await ApiResponseHelper.ReadAsync<AssetResponse>(response, cancellationToken);
    }

    public async Task<AssetResponse> UpdateAsync(string id, AssetRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PutAsJsonAsync($"assets/{id}", request, ApiResponseHelper.JsonOptions, cancellationToken);
        return await ApiResponseHelper.ReadAsync<AssetResponse>(response, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.DeleteAsync($"assets/{id}", cancellationToken);
        await ApiResponseHelper.EnsureSuccessAsync(response, cancellationToken);
    }
}

public sealed class AssignmentApiService(AuthorizedApiClient apiClient)
{
    public async Task<List<AssignmentResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.GetAsync("assignments", cancellationToken);
        return await ApiResponseHelper.ReadAsync<List<AssignmentResponse>>(response, cancellationToken);
    }

    public async Task<AssignmentResponse> CreateAsync(AssignmentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PostAsJsonAsync("assignments", request, ApiResponseHelper.JsonOptions, cancellationToken);
        return await ApiResponseHelper.ReadAsync<AssignmentResponse>(response, cancellationToken);
    }

    public async Task<AssignmentResponse> UpdateAsync(string id, AssignmentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PutAsJsonAsync($"assignments/{id}", request, ApiResponseHelper.JsonOptions, cancellationToken);
        return await ApiResponseHelper.ReadAsync<AssignmentResponse>(response, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.DeleteAsync($"assignments/{id}", cancellationToken);
        await ApiResponseHelper.EnsureSuccessAsync(response, cancellationToken);
    }
}

public sealed class UserApiService(AuthorizedApiClient apiClient)
{
    public async Task<List<UserResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.GetAsync("users", cancellationToken);
        return await ApiResponseHelper.ReadAsync<List<UserResponse>>(response, cancellationToken);
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PostAsJsonAsync("users", request, ApiResponseHelper.JsonOptions, cancellationToken);
        return await ApiResponseHelper.ReadAsync<UserResponse>(response, cancellationToken);
    }

    public async Task<UserResponse> UpdateAsync(string id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PutAsJsonAsync($"users/{id}", request, ApiResponseHelper.JsonOptions, cancellationToken);
        return await ApiResponseHelper.ReadAsync<UserResponse>(response, cancellationToken);
    }

    public async Task ResetPasswordAsync(string id, ResetUserPasswordRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.PostAsJsonAsync($"users/{id}/password", request, ApiResponseHelper.JsonOptions, cancellationToken);
        await ApiResponseHelper.EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.DeleteAsync($"users/{id}", cancellationToken);
        await ApiResponseHelper.EnsureSuccessAsync(response, cancellationToken);
    }
}

public sealed class HistoryApiService(AuthorizedApiClient apiClient)
{
    public async Task<List<AuditLogResponse>> GetAuditLogsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.GetAsync("audit-logs", cancellationToken);
        return await ApiResponseHelper.ReadAsync<List<AuditLogResponse>>(response, cancellationToken);
    }

    public async Task<List<LifecycleEventResponse>> GetLifecycleEventsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.GetAsync("lifecycle-events", cancellationToken);
        return await ApiResponseHelper.ReadAsync<List<LifecycleEventResponse>>(response, cancellationToken);
    }
}

public sealed class ReportsApiService(AuthorizedApiClient apiClient)
{
    public async Task<ReportsOverviewResponse> GetOverviewAsync(
        ReportsOverviewQuery query,
        CancellationToken cancellationToken = default)
    {
        using var response = await apiClient.Client.GetAsync(BuildOverviewUri(query), cancellationToken);
        return await ApiResponseHelper.ReadAsync<ReportsOverviewResponse>(response, cancellationToken);
    }

    private static string BuildOverviewUri(ReportsOverviewQuery query)
    {
        var parameters = new List<string>();

        AppendQueryParameter(parameters, "preset", query.Preset);
        AppendQueryParameter(parameters, "startDate", query.StartDate);
        AppendQueryParameter(parameters, "endDate", query.EndDate);
        AppendQueryParameter(parameters, "assetDepartment", query.AssetDepartment);
        AppendQueryParameter(parameters, "userDepartment", query.UserDepartment);

        return parameters.Count == 0
            ? "reports/overview"
            : $"reports/overview?{string.Join("&", parameters)}";
    }

    private static void AppendQueryParameter(List<string> parameters, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parameters.Add($"{WebUtility.UrlEncode(key)}={WebUtility.UrlEncode(value.Trim())}");
    }
}
