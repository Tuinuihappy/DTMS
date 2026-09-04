using System.Net;
using DTMS.Wms.Application.Services;
using DTMS.Wms.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DTMS.Api.UnitTests;

// Token precedence on outbound WMS calls. The managed credential wins so the
// auto-refresh loop's rotation actually reaches the wire; the configured token
// is the fallback, and therefore also the rollback path — drop the credential
// row and every request goes back to Wms:Auth:Token with no redeploy.
public class WmsBearerTokenHandlerTests
{
    [Fact]
    public async Task ManagedToken_WinsOverConfiguredToken()
    {
        var (client, captured) = Build(managed: "managed-jwt", configured: "configured-jwt");

        await client.GetAsync("http://wms.test/location");

        captured.Request!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Request.Headers.Authorization.Parameter.Should().Be("managed-jwt");
    }

    [Fact]
    public async Task NoManagedToken_FallsBackToConfiguration()
    {
        var (client, captured) = Build(managed: null, configured: "configured-jwt");

        await client.GetAsync("http://wms.test/location");

        captured.Request!.Headers.Authorization!.Parameter.Should().Be("configured-jwt");
    }

    [Fact]
    public async Task BlankManagedToken_FallsBackToConfiguration()
    {
        // A credential row that exists but holds no usable token must not
        // shadow the configured one.
        var (client, captured) = Build(managed: "   ", configured: "configured-jwt");

        await client.GetAsync("http://wms.test/location");

        captured.Request!.Headers.Authorization!.Parameter.Should().Be("configured-jwt");
    }

    [Fact]
    public async Task NoTokenAnywhere_StillSendsRequestWithoutHeader()
    {
        // Deliberate: let WMS answer 401 so the sync service logs a real
        // outcome, rather than throwing client-side where it reads as a bug.
        var (client, captured) = Build(managed: null, configured: "");

        var response = await client.GetAsync("http://wms.test/location");

        captured.Request!.Headers.Authorization.Should().BeNull();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static (HttpClient client, CapturingHandler captured) Build(
        string? managed, string configured)
    {
        var provider = Substitute.For<IWmsTokenProvider>();
        provider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(managed);

        var options = Substitute.For<IOptionsMonitor<WmsOptions>>();
        options.CurrentValue.Returns(new WmsOptions { Auth = new WmsAuthOptions { Token = configured } });

        var capturing = new CapturingHandler();
        var handler = new WmsBearerTokenHandler(
            provider, options, NullLogger<WmsBearerTokenHandler>.Instance)
        {
            InnerHandler = capturing,
        };

        return (new HttpClient(handler), capturing);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
