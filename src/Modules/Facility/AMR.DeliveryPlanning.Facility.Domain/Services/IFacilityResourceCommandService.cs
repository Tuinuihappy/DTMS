namespace AMR.DeliveryPlanning.Facility.Domain.Services;

public interface IFacilityResourceCommandService
{
    Task<bool> SendCommandAsync(string resourceType, string vendorRef, string command, CancellationToken cancellationToken = default);
}
