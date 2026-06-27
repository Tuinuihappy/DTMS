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

    public HttpOmsShipmentClientFixture()
    {
        Server = WireMockServer.Start();
        var http = new HttpClient { BaseAddress = new Uri(Server.Url!) };
        Client = new HttpOmsShipmentClient(http, NullLogger<HttpOmsShipmentClient>.Instance);
    }

    public void Dispose()
    {
        Server.Stop();
        Server.Dispose();
    }
}
