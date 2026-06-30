using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using WireMock.Server;

namespace OmsAdapter.IntegrationTests;

// Phase OMS B3 — Shared WireMock fixture for HttpOmsShipmentClient tests.
//
// Spins a WireMock.Net server on a random localhost port, hands the test
// a real HttpOmsShipmentClient pointed at it, and tears the server down
// when the test class disposes.
//
// The fixture instantiates the internal HttpOmsShipmentClient via
// InternalsVisibleTo, but exposes it through the public IOmsShipmentClient
// surface — tests exercise the public contract, not internal details.
public sealed class HttpOmsShipmentClientFixture : IDisposable
{
    public WireMockServer Server { get; }
    public IOmsShipmentClient Client { get; }
    // Phase S.6 follow-up — methods now take a per-call target instead of
    // pinning BaseAddress at construction. Tests use this fixture's
    // WireMock URL with no bearer token.
    public OmsCallbackTarget Target { get; }

    public HttpOmsShipmentClientFixture()
    {
        Server = WireMockServer.Start();
        var http = new HttpClient();
        Client = new HttpOmsShipmentClient(http, NullLogger<HttpOmsShipmentClient>.Instance);
        Target = new OmsCallbackTarget(Server.Url!, BearerToken: null, Timeout: TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
    }
}
