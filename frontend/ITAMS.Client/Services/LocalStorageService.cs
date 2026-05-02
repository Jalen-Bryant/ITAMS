using Microsoft.JSInterop;

namespace ITAMS.Client.Services;

public sealed class LocalStorageService(IJSRuntime jsRuntime)
{
    public ValueTask<T?> GetAsync<T>(string key) =>
        jsRuntime.InvokeAsync<T?>("itamsStorage.get", key);

    public ValueTask SetAsync<T>(string key, T value) =>
        jsRuntime.InvokeVoidAsync("itamsStorage.set", key, value);

    public ValueTask RemoveAsync(string key) =>
        jsRuntime.InvokeVoidAsync("itamsStorage.remove", key);
}
