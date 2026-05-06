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

        try
        {
            Cronos.CronExpression.Parse(cronExpression, Cronos.CronFormat.IncludeSeconds);
        }
        catch
        {
            try { Cronos.CronExpression.Parse(cronExpression); }
            catch (Cronos.CronFormatException ex)
            {
                throw new ArgumentException($"Invalid cron expression '{cronExpression}': {ex.Message}", nameof(cronExpression));
            }
        }

        Id = Guid.NewGuid();
        DeliveryOrderId = deliveryOrderId;
        CronExpression = cronExpression;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }
}
