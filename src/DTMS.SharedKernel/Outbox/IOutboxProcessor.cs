namespace AMR.DeliveryPlanning.SharedKernel.Outbox;

public interface IOutboxProcessor
{
    Task ProcessUnpublishedEventsAsync(CancellationToken cancellationToken = default);
}
