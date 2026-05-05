namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

public class InternalOrderNoGenerator
{
    public string Generate() =>
        $"DTMS-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
}
