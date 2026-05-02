using System.Net;
using ITAMS.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ITAMS.Api.Tests;

public sealed class OperationContextServiceTests
{
    private readonly OperationContextService _service = new();

    [Fact]
    public void GetClientIp_MapsIpv6LoopbackToIpv4Loopback()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;

        var ip = _service.GetClientIp(httpContext);

        Assert.Equal("127.0.0.1", ip);
    }

    [Fact]
    public void GetClientIp_ExpandsIpv6AddressesForMongoValidation()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");

        var ip = _service.GetClientIp(httpContext);

        Assert.Equal("2001:0db8:0000:0000:0000:0000:0000:0001", ip);
    }
}
