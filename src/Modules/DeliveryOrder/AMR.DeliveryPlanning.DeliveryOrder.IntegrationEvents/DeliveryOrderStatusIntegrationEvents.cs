using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.DeliveryOrder.IntegrationEvents;

public record DeliveryOrderCancelledIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TenantId, Guid DeliveryOrderId, string Reason) : IIntegrationEvent;

public record DeliveryOrderFailedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TenantId, Guid DeliveryOrderId, string Reason) : IIntegrationEvent;

public record DeliveryOrderCompletedIntegrationEvent(
    Guid EventId, DateTime OccurredOn, Guid TenantId, Guid DeliveryOrderId) : IIntegrationEvent;
