using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.Entities;

public enum OverlayType { Blockage, TemporaryStation, SpeedRestriction }

public class TopologyOverlay : Entity<Guid>
{
    public Guid MapId { get; private set; }
    public OverlayType Type { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidUntil { get; private set; }
    public string? PolygonJson { get; private set; }
    public Guid? AffectedStationId { get; private set; }
    public bool IsExpired => DateTime.UtcNow > ValidUntil;

    private TopologyOverlay() { }

    public TopologyOverlay(Guid mapId, OverlayType type, string reason,
        DateTime validFrom, DateTime validUntil, string? polygonJson = null, Guid? affectedStationId = null)
    {
        Id = Guid.NewGuid();
        MapId = mapId;
        Type = type;
        Reason = reason;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        PolygonJson = polygonJson;
        AffectedStationId = affectedStationId;
    }

    public void ExtendUntil(DateTime newValidUntil)
    {
        if (newValidUntil <= ValidUntil)
            throw new InvalidOperationException("New expiry must be later than current expiry.");
        ValidUntil = newValidUntil;
    }
}
