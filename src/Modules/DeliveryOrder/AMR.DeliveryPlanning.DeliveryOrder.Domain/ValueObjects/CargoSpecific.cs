namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

public class CargoSpecific
{
    public string? PartNo { get; private set; }
    public string? Wo { get; private set; }
    public string? Line { get; private set; }
    public string? Vendor { get; private set; }
    public string? DateCode { get; private set; }
    public string? TradingCode { get; private set; }
    public string? InventoryNo { get; private set; }
    public string? Po { get; private set; }
    public string? TraceId { get; private set; }
    public string? LotNo { get; private set; }

    private CargoSpecific() { }

    public static CargoSpecific Create(
        string? partNo, string? wo, string? line,
        string? vendor, string? dateCode,
        string? tradingCode, string? inventoryNo, string? po, string? traceId, string? lotNo = null)
    {
        return new CargoSpecific
        {
            PartNo = partNo,
            Wo = wo,
            Line = line,
            Vendor = vendor,
            DateCode = dateCode,
            TradingCode = tradingCode,
            InventoryNo = inventoryNo,
            Po = po,
            TraceId = traceId,
            LotNo = lotNo
        };
    }
}
