using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ITAMS.Api.Services;

public sealed class OperationContextService
{
    public string GetClientIp(HttpContext httpContext)
    {
        var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress is null)
        {
            return "0.0.0.0";
        }

        if (remoteIpAddress.IsIPv4MappedToIPv6)
        {
            remoteIpAddress = remoteIpAddress.MapToIPv4();
        }

        // Mongo validation expects a fuller IP string than "::1", so normalize loopback to an IPv4 representation.
        if (IPAddress.IsLoopback(remoteIpAddress))
        {
            return "127.0.0.1";
        }

        return remoteIpAddress.AddressFamily == AddressFamily.InterNetworkV6
            ? ExpandIpv6(remoteIpAddress)
            : remoteIpAddress.ToString();
    }

    public string GetUserAgent(HttpContext httpContext)
    {
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(userAgent) ? "Unknown Client" : userAgent.Trim();
    }

    private static string ExpandIpv6(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();
        var builder = new StringBuilder(capacity: 39);

        for (var index = 0; index < bytes.Length; index += 2)
        {
            if (index > 0)
            {
                builder.Append(':');
            }

            var segment = (bytes[index] << 8) | bytes[index + 1];
            builder.Append(segment.ToString("x4"));
        }

        return builder.ToString();
    }
}
