using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public class LoadUnitProfile : Entity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public double LengthMm { get; private set; }
    public double WidthMm { get; private set; }
    public double HeightMm { get; private set; }
    public double MaxGrossWeightKg { get; private set; }
    public string CarrierTypeCode { get; private set; } = string.Empty;

    private LoadUnitProfile() { }

    public LoadUnitProfile(string code, string displayName,
        double lengthMm, double widthMm, double heightMm,
        double maxGrossWeightKg, string carrierTypeCode)
    {
        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        DisplayName = displayName;
        LengthMm = lengthMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
        MaxGrossWeightKg = maxGrossWeightKg;
        CarrierTypeCode = carrierTypeCode.Trim().ToUpperInvariant();
    }
}
