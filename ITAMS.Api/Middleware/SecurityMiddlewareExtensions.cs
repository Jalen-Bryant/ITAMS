using ITAMS.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ITAMS.Api.Middleware;

public static class SecurityMiddlewareExtensions
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "connect-src 'self' https://itams.app https://www.itams.app http://localhost:* https://localhost:* ws://localhost:* wss://localhost:*; " +
        "font-src 'self' data:; " +
        "form-action 'self'; " +
        "upgrade-insecure-requests";

    public static IApplicationBuilder UseItamsSecurityHeaders(
        this IApplicationBuilder app,
        IHostEnvironment environment,
        IOptions<SecuritySettings> settings)
    {
        return app.Use(async (httpContext, next) =>
        {
            httpContext.Response.OnStarting(() =>
            {
                var headers = httpContext.Response.Headers;
                headers.TryAdd("X-Content-Type-Options", "nosniff");
                headers.TryAdd("X-Frame-Options", "DENY");
                headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                headers.TryAdd(
                    "Permissions-Policy",
                    "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");

                var cspHeaderName = settings.Value.ContentSecurityPolicyReportOnly
                    ? "Content-Security-Policy-Report-Only"
                    : "Content-Security-Policy";
                headers.TryAdd(cspHeaderName, ContentSecurityPolicy);

                if (!environment.IsDevelopment() && httpContext.Request.IsHttps)
                {
                    headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
                }

                return Task.CompletedTask;
            });

            await next(httpContext);
        });
    }

    public static IApplicationBuilder UseItamsRequestBodyLimit(
        this IApplicationBuilder app,
        IOptions<SecuritySettings> settings)
    {
        return app.Use(async (httpContext, next) =>
        {
            var maxRequestBodyBytes = settings.Value.MaxRequestBodyBytes;
            if (httpContext.Request.ContentLength > maxRequestBodyBytes)
            {
                httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    message = $"The request body must be {maxRequestBodyBytes} bytes or smaller."
                });
                return;
            }

            await next(httpContext);
        });
    }
}
