using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Dispatch.Domain.Entities;

public class TripException : Entity<Guid>
{
    public Guid TripId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Severity { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
    public string? Resolution { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTime RaisedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public bool IsResolved => ResolvedAt.HasValue;

    private TripException() { }

    internal TripException(Guid tripId, string code, string severity, string detail)
    {
        Id = Guid.NewGuid();
        TripId = tripId;
        Code = code;
        Severity = severity;
        Detail = detail;
        RaisedAt = DateTime.UtcNow;
    }

    public void Resolve(string resolution, string resolvedBy)
    {
        if (IsResolved)
            throw new InvalidOperationException("Exception is already resolved.");

        Resolution = resolution;
        ResolvedBy = resolvedBy;
        ResolvedAt = DateTime.UtcNow;
    }
}
