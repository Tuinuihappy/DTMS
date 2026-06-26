using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Services;
using DTMS.Facility.Application.Services;
using FluentAssertions;
using NSubstitute;
using DoLookup = AMR.DeliveryPlanning.DeliveryOrder.Application.Services.WarehouseLookupResult;
using FacilityLookup = DTMS.Facility.Application.Services.WarehouseLookupResult;

namespace DeliveryOrder.UnitTests;

// Phase 2.6 — FacilityWarehouseLookup is a translation layer between
// DeliveryOrder's IWarehouseLookup contract and Facility's IFacilityReadService.
// Both modules carry their own WarehouseLookupResult record with
// identical shape but different namespaces — these tests pin that the
// translation preserves every field and works correctly across module
// boundaries.
public class FacilityWarehouseLookupTests
{
    private readonly IFacilityReadService _facility = Substitute.For<IFacilityReadService>();
    private readonly FacilityWarehouseLookup _sut;

    public FacilityWarehouseLookupTests()
    {
        _sut = new FacilityWarehouseLookup(_facility);
    }

    [Fact]
    public async Task ExistsAsync_DelegatesToFacility()
    {
        var id = Guid.NewGuid();
        _facility.WarehouseExistsAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ExistsAsync(id);

        result.Should().BeTrue();
        await _facility.Received(1).WarehouseExistsAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveByCodeAsync_DelegatesAndReturnsResolvedId()
    {
        var expectedId = Guid.NewGuid();
        _facility.ResolveWarehouseByCodeAsync("WH-BKK-01", Arg.Any<CancellationToken>())
            .Returns(expectedId);

        var result = await _sut.ResolveByCodeAsync("WH-BKK-01");

        result.Should().Be(expectedId);
    }

    [Fact]
    public async Task ResolveByCodeAsync_UnknownCode_ReturnsNull()
    {
        _facility.ResolveWarehouseByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var result = await _sut.ResolveByCodeAsync("BAD-CODE");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBatchAsync_TranslatesEveryFieldAcrossNamespaces()
    {
        // The cross-namespace translation is the entire point of this adapter
        // — if a new field is added to one WarehouseLookupResult and not the
        // other, this test catches it (compile-time error in the field
        // copy), so we exercise every property explicitly.
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var facilityResult = new Dictionary<string, FacilityLookup>(StringComparer.OrdinalIgnoreCase)
        {
            ["WH-BKK-01"] = new FacilityLookup(id1, "WH-BKK-01", "Bangkok DC", IsActive: true),
            ["wh-cnx-01"] = new FacilityLookup(id2, "WH-CNX-01", "Chiang Mai DC", IsActive: false),
        };
        _facility.ResolveWarehousesBatchAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(facilityResult);

        var result = await _sut.ResolveBatchAsync(new[] { "WH-BKK-01", "wh-cnx-01" });

        result.Should().HaveCount(2);
        result["WH-BKK-01"].Should().BeEquivalentTo(
            new DoLookup(id1, "WH-BKK-01", "Bangkok DC", IsActive: true));
        result["wh-cnx-01"].Should().BeEquivalentTo(
            new DoLookup(id2, "WH-CNX-01", "Chiang Mai DC", IsActive: false));
    }

    [Fact]
    public async Task ResolveBatchAsync_EmptyInput_ReturnsEmptyResult()
    {
        _facility.ResolveWarehousesBatchAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, FacilityLookup>());

        var result = await _sut.ResolveBatchAsync(Array.Empty<string>());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveBatchAsync_PreservesCaseInsensitiveKeying()
    {
        // The result dict must use OrdinalIgnoreCase comparer so callers
        // can look up "wh-bkk-01" / "WH-BKK-01" interchangeably — order
        // validation depends on this when raw operator input has mixed
        // casing.
        var id = Guid.NewGuid();
        _facility.ResolveWarehousesBatchAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, FacilityLookup>(StringComparer.OrdinalIgnoreCase)
            {
                ["WH-BKK-01"] = new FacilityLookup(id, "WH-BKK-01", "Bangkok DC", IsActive: true),
            });

        var result = await _sut.ResolveBatchAsync(new[] { "WH-BKK-01" });

        result.ContainsKey("wh-bkk-01").Should().BeTrue();
        result.ContainsKey("WH-BKK-01").Should().BeTrue();
    }
}
