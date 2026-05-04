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
        if (string.IsNullOrWhiteSpace(cronExpression))
            throw new ArgumentException("Cron expression cannot be empty.", nameof(cronExpression));

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 5 or > 6)
            throw new ArgumentException(
                $"Invalid cron expression '{cronExpression}': expected 5 or 6 space-separated fields.", nameof(cronExpression));

        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        CronExpression = cronExpression;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }
}
