using AMR.DeliveryPlanning.Transport.Manual.Application.Services;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class OperatorSyncServiceTests
{
    private readonly IOperatorRepository _repo = Substitute.For<IOperatorRepository>();

    [Fact]
    public async Task FirstLogin_CreatesOperator_AndSaves()
    {
        _repo.GetByEmployeeCodeAsync("EMP-1", Arg.Any<CancellationToken>())
             .Returns((Operator?)null);
        var sut = new OperatorSyncService(_repo);

        var op = await sut.SyncFromClaimsAsync(
            "EMP-1", "Somchai", OperatorRole.Operator, null, null);

        op.EmployeeCode.Should().Be("EMP-1");
        op.DisplayName.Should().Be("Somchai");
        await _repo.Received(1).AddAsync(Arg.Any<Operator>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondLogin_NoDrift_DoesNotSave()
    {
        var existing = Operator.CreateFromJwtClaims("EMP-2", "Same Name", OperatorRole.Operator);
        existing.ClearDomainEvents();
        _repo.GetByEmployeeCodeAsync("EMP-2", Arg.Any<CancellationToken>())
             .Returns(existing);
        var sut = new OperatorSyncService(_repo);

        await sut.SyncFromClaimsAsync(
            "EMP-2", "Same Name", OperatorRole.Operator, null, null);

        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondLogin_RoleDrift_SavesAndUpdates()
    {
        var existing = Operator.CreateFromJwtClaims("EMP-3", "Person", OperatorRole.Operator);
        _repo.GetByEmployeeCodeAsync("EMP-3", Arg.Any<CancellationToken>())
             .Returns(existing);
        var sut = new OperatorSyncService(_repo);

        var result = await sut.SyncFromClaimsAsync(
            "EMP-3", "Person", OperatorRole.Supervisor, null, null);

        result.Role.Should().Be(OperatorRole.Supervisor);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondLogin_PrimaryWarehouseFromClaim_FillsWhenLocalIsNull()
    {
        var existing = Operator.CreateFromJwtClaims("EMP-4", "Person", OperatorRole.Operator);
        _repo.GetByEmployeeCodeAsync("EMP-4", Arg.Any<CancellationToken>())
             .Returns(existing);
        var sut = new OperatorSyncService(_repo);
        var warehouseId = Guid.NewGuid();

        var result = await sut.SyncFromClaimsAsync(
            "EMP-4", "Person", OperatorRole.Operator, null, warehouseId);

        result.PrimaryWarehouseId.Should().Be(warehouseId);
    }

    [Fact]
    public async Task SecondLogin_LocalWarehouseAlreadySet_DoesNotOverwrite()
    {
        var localWarehouse = Guid.NewGuid();
        var existing = Operator.CreateFromJwtClaims(
            "EMP-5", "Person", OperatorRole.Operator,
            primaryWarehouseId: localWarehouse);
        _repo.GetByEmployeeCodeAsync("EMP-5", Arg.Any<CancellationToken>())
             .Returns(existing);
        var sut = new OperatorSyncService(_repo);

        var result = await sut.SyncFromClaimsAsync(
            "EMP-5", "Person", OperatorRole.Operator, null, Guid.NewGuid());

        result.PrimaryWarehouseId.Should().Be(localWarehouse);
    }
}
