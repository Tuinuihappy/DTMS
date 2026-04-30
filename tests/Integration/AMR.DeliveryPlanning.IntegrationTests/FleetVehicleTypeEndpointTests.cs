using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace AMR.DeliveryPlanning.IntegrationTests;

public class FleetVehicleTypeEndpointTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public FleetVehicleTypeEndpointTests(DtmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateVehicleType_ThenRegisterRiot3VehicleWithVendorVehicleKey()
    {
        var client = await _factory.GetAuthenticatedClient();
        var deviceKey = $"SEER-{Guid.NewGuid():N}"[..20];

        var typeResp = await client.PostAsJsonAsync("/api/fleet/vehicle-types", new
        {
            TypeName = "RIOT3 Feeder",
            MaxPayload = 100.0,
            Capabilities = new[] { "MOVE", "LIFT" }
        });

        typeResp.StatusCode.Should().Be(HttpStatusCode.Created,
            $"VehicleType creation failed: {await typeResp.Content.ReadAsStringAsync()}");
        var vehicleTypeId = await typeResp.Content.ReadFromJsonAsync<Guid>();

        var regResp = await client.PostAsJsonAsync("/api/fleet/vehicles", new
        {
            VehicleName = "FAN1_FEEDER_NO6",
            VehicleTypeId = vehicleTypeId,
            AdapterKey = "riot3",
            VendorVehicleKey = deviceKey
        });

        regResp.IsSuccessStatusCode.Should().BeTrue(
            $"Vehicle registration failed: {await regResp.Content.ReadAsStringAsync()}");
    }
}
