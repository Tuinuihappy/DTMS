using DTMS.Transport.Manual.Application.Services;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

public class WarehouseAwareOperatorAssignmentPolicyTests
{
    private readonly IOperatorRepository _repo = Substitute.For<IOperatorRepository>();

    [Fact]
    public async Task Select_NoCandidates_ReturnsNoMatchWithReason()
    {
        var warehouseId = Guid.NewGuid();
        _repo.GetEligibleForAssignmentAsync(warehouseId, Arg.Any<CancellationToken>())
             .Returns(Array.Empty<Operator>());
        var sut = new WarehouseAwareOperatorAssignmentPolicy(_repo);

        var result = await sut.SelectOperatorAsync(
            new OperatorAssignmentContext(warehouseId, Array.Empty<CertificationType>()));

        result.IsAssigned.Should().BeFalse();
        result.RejectionReason.Should().Contain("idle operator");
    }

    [Fact]
    public async Task Select_NoCertRequired_PicksFirstCandidate()
    {
        var op1 = Operator.CreateFromJwtClaims("EMP-100", "First", OperatorRole.Operator);
        var op2 = Operator.CreateFromJwtClaims("EMP-200", "Second", OperatorRole.Operator);
        _repo.GetEligibleForAssignmentAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
             .Returns(new[] { op1, op2 });
        var sut = new WarehouseAwareOperatorAssignmentPolicy(_repo);

        var result = await sut.SelectOperatorAsync(
            new OperatorAssignmentContext(null, Array.Empty<CertificationType>()));

        result.Operator.Should().Be(op1);
    }

    [Fact]
    public async Task Select_CertRequired_SkipsCandidatesMissingTheCert()
    {
        var withoutCert = Operator.CreateFromJwtClaims("EMP-300", "Plain", OperatorRole.Operator);
        var withCert = Operator.CreateFromJwtClaims("EMP-400", "Hazmat-cleared", OperatorRole.Operator);
        withCert.AddCertification(CertificationType.Hazmat, expiresAt: DateTime.UtcNow.AddYears(1));

        _repo.GetEligibleForAssignmentAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
             .Returns(new[] { withoutCert, withCert });
        _repo.GetByIdWithDetailsAsync(withoutCert.Id, Arg.Any<CancellationToken>()).Returns(withoutCert);
        _repo.GetByIdWithDetailsAsync(withCert.Id, Arg.Any<CancellationToken>()).Returns(withCert);

        var sut = new WarehouseAwareOperatorAssignmentPolicy(_repo);

        var result = await sut.SelectOperatorAsync(
            new OperatorAssignmentContext(null, new[] { CertificationType.Hazmat }));

        result.Operator.Should().Be(withCert);
    }

    [Fact]
    public async Task Select_AllCandidatesMissingCert_NoMatch()
    {
        var op1 = Operator.CreateFromJwtClaims("EMP-500", "A", OperatorRole.Operator);
        _repo.GetEligibleForAssignmentAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
             .Returns(new[] { op1 });
        _repo.GetByIdWithDetailsAsync(op1.Id, Arg.Any<CancellationToken>()).Returns(op1);
        var sut = new WarehouseAwareOperatorAssignmentPolicy(_repo);

        var result = await sut.SelectOperatorAsync(
            new OperatorAssignmentContext(null, new[] { CertificationType.ColdChain }));

        result.IsAssigned.Should().BeFalse();
        result.RejectionReason.Should().Contain("certifications");
    }

    [Fact]
    public async Task Select_CertExpired_TreatedAsMissing()
    {
        var op = Operator.CreateFromJwtClaims("EMP-600", "Expired", OperatorRole.Operator);
        op.AddCertification(CertificationType.Forklift, expiresAt: DateTime.UtcNow.AddDays(7));
        op.Certifications.Single().Revoke("training expired");
        // Cert is now IsActive=false, so IsCurrentlyValid returns false.

        _repo.GetEligibleForAssignmentAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
             .Returns(new[] { op });
        _repo.GetByIdWithDetailsAsync(op.Id, Arg.Any<CancellationToken>()).Returns(op);
        var sut = new WarehouseAwareOperatorAssignmentPolicy(_repo);

        var result = await sut.SelectOperatorAsync(
            new OperatorAssignmentContext(null, new[] { CertificationType.Forklift }));

        result.IsAssigned.Should().BeFalse();
    }
}
