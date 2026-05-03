using Microsoft.JSInterop;

namespace ITAMS.Client.Services;

public sealed class BrowserSessionStorageService(IJSRuntime jsRuntime)
{
    public ValueTask<T?> GetAsync<T>(string key) =>
        jsRuntime.InvokeAsync<T?>("itamsSessionStorage.get", key);

    public ValueTask SetAsync<T>(string key, T value) =>
        jsRuntime.InvokeVoidAsync("itamsSessionStorage.set", key, value);

    public ValueTask RemoveAsync(string key) =>
        jsRuntime.InvokeVoidAsync("itamsSessionStorage.remove", key);
}
