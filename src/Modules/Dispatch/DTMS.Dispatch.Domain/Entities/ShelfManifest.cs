using DTMS.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class ShelfManifest : Entity<Guid>
{
    public Guid JobId { get; private set; }
    public string ShelfRfid { get; private set; } = string.Empty;
    public Guid? TripId { get; private set; }
    public DateTime AssignedAt { get; private set; }
    public DateTime? ReleasedAt { get; private set; }
    public bool IsReleased => ReleasedAt.HasValue;

    private readonly List<string> _packageBarcodes = new();
    public IReadOnlyCollection<string> PackageBarcodes => _packageBarcodes.AsReadOnly();

    private ShelfManifest() { }

    public ShelfManifest(Guid jobId, string shelfRfid, IEnumerable<string> packageBarcodes)
    {
        Id = Guid.NewGuid();
        JobId = jobId;
        ShelfRfid = shelfRfid;
        AssignedAt = DateTime.UtcNow;
        _packageBarcodes.AddRange(packageBarcodes);
    }

    public void AssignToTrip(Guid tripId) => TripId = tripId;

    public void Release() => ReleasedAt = DateTime.UtcNow;
}
