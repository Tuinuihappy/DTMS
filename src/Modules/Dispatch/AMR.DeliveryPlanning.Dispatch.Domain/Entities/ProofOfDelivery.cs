using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class ProofOfDelivery : Entity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid StopId { get; private set; }
    public string? PhotoUrl { get; private set; }
    public string? SignatureData { get; private set; }
    public List<string> ScannedIds { get; private set; } = new();
    public string? Notes { get; private set; }
    public DateTime CapturedAt { get; private set; }

    private ProofOfDelivery() { }

    internal ProofOfDelivery(Guid tripId, Guid stopId, string? photoUrl, string? signatureData, List<string>? scannedIds, string? notes)
    {
        Id = Guid.NewGuid();
        TripId = tripId;
        StopId = stopId;
        PhotoUrl = photoUrl;
        SignatureData = signatureData;
        ScannedIds = scannedIds ?? new List<string>();
        Notes = notes;
        CapturedAt = DateTime.UtcNow;
    }
}
