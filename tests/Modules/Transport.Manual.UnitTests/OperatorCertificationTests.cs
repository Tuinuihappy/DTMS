using AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

public class OperatorCertificationTests
{
    private static Operator CreateOperator() => Operator.CreateFromJwtClaims(
        "EMP-99", "Test", OperatorRole.Operator);

    [Fact]
    public void IsCurrentlyValid_ActiveNoExpiry_ReturnsTrue()
    {
        var op = CreateOperator();
        op.AddCertification(CertificationType.Forklift, expiresAt: null);

        op.Certifications.Single().IsCurrentlyValid(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsCurrentlyValid_ActiveBeforeExpiry_ReturnsTrue()
    {
        var op = CreateOperator();
        var expiry = DateTime.UtcNow.AddDays(30);
        op.AddCertification(CertificationType.ColdChain, expiresAt: expiry);

        op.Certifications.Single().IsCurrentlyValid(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsCurrentlyValid_PastExpiry_ReturnsFalse()
    {
        var op = CreateOperator();
        var expiry = DateTime.UtcNow.AddDays(30);
        op.AddCertification(CertificationType.ColdChain, expiresAt: expiry);

        op.Certifications.Single().IsCurrentlyValid(expiry.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void IsCurrentlyValid_AfterRevoke_ReturnsFalse()
    {
        var op = CreateOperator();
        op.AddCertification(CertificationType.Hazmat, expiresAt: DateTime.UtcNow.AddYears(1));
        op.RevokeCertification(CertificationType.Hazmat, "incident");

        op.Certifications.Single().IsCurrentlyValid(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void AddCertification_PastExpiry_Throws()
    {
        var op = CreateOperator();

        var act = () => op.AddCertification(CertificationType.Hazmat,
            expiresAt: DateTime.UtcNow.AddDays(-1));

        act.Should().Throw<ArgumentException>().WithParameterName("expiresAt");
    }
}
