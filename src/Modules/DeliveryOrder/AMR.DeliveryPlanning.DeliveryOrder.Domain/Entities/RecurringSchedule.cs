using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

public class RecurringSchedule : Entity<Guid>
{
    public Guid DeliveryOrderId { get; private set; }
    public string CronExpression { get; private set; }
    public DateTime? ValidFrom { get; private set; }
    public DateTime? ValidUntil { get; private set; }

    private RecurringSchedule() { CronExpression = null!; } // For EF Core

    internal RecurringSchedule(Guid deliveryOrderId, string cronExpression, DateTime? validFrom, DateTime? validUntil)
    {
        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        CronExpression = cronExpression;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }
}
