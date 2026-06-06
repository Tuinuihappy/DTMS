using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetFullOrderAudit;

public class GetFullOrderAuditQueryHandler : IQueryHandler<GetFullOrderAuditQuery, FullOrderAuditDto>
{
    private readonly IOrderAuditEventRepository _auditRepo;
    private readonly IOrderAmendmentRepository _amendmentRepo;
    private readonly ITripRepository _tripRepo;
    private readonly ITripRetryEventRepository _retryRepo;

    public GetFullOrderAuditQueryHandler(
        IOrderAuditEventRepository auditRepo,
        IOrderAmendmentRepository amendmentRepo,
        ITripRepository tripRepo,
        ITripRetryEventRepository retryRepo)
    {
        _auditRepo = auditRepo;
        _amendmentRepo = amendmentRepo;
        _tripRepo = tripRepo;
        _retryRepo = retryRepo;
    }

    public async Task<Result<FullOrderAuditDto>> Handle(GetFullOrderAuditQuery request, CancellationToken cancellationToken)
    {
        // Sequential reads — both DeliveryOrder repos share a scoped
        // DbContext so we can't parallelise within that module. Dispatch
        // repos use a different DbContext but we keep them sequential too
        // for simplicity (the queries are small + indexed).
        var auditEvents = await _auditRepo.GetByOrderAsync(request.OrderId, cancellationToken);
        var amendments  = await _amendmentRepo.GetByOrderAsync(request.OrderId, cancellationToken);
        var trips       = await _tripRepo.GetByDeliveryOrderIdAsync(request.OrderId, cancellationToken);
        var retryEvents = await _retryRepo.GetByDeliveryOrderIdAsync(request.OrderId, cancellationToken);

        var entries = new List<FullAuditEntryDto>();

        // ── Source 1: Order audit events ───────────────────────────────
        entries.AddRange(auditEvents.Select(e => new FullAuditEntryDto(
            Id: e.Id,
            Source: "Order",
            EventType: e.EventType,
            Details: e.Details,
            ActorId: e.ActorId,
            OccurredAt: e.OccurredAt,
            RelatedTripId: null,
            AttemptNumber: null)));

        // ── Source 2: Order amendments ─────────────────────────────────
        entries.AddRange(amendments.Select(a => new FullAuditEntryDto(
            Id: a.Id,
            Source: "Amendment",
            EventType: $"Amendment:{a.Type}",
            Details: a.Reason,
            ActorId: a.AmendedBy,
            OccurredAt: a.AmendedAt,
            RelatedTripId: null,
            AttemptNumber: null)));

        // ── Source 3: Per-trip ExecutionEvents ─────────────────────────
        // The Trip aggregate already eager-loads its Events collection in
        // ITripRepository.GetByDeliveryOrderIdAsync (via the DbContext
        // include); each event maps cleanly to an audit row.
        foreach (var trip in trips)
        {
            foreach (var ev in trip.Events)
            {
                entries.Add(new FullAuditEntryDto(
                    Id: ev.Id,
                    Source: "TripExecution",
                    EventType: ev.EventType,
                    Details: ev.Details,
                    ActorId: null,   // execution events are system-emitted
                    OccurredAt: ev.OccurredAt,
                    RelatedTripId: trip.Id,
                    AttemptNumber: trip.AttemptNumber));
            }
        }

        // ── Source 4: TripRetryEvents (audit-only, immutable) ──────────
        entries.AddRange(retryEvents.Select(r => new FullAuditEntryDto(
            Id: r.Id,
            Source: "TripRetry",
            EventType: $"TripRetry:{r.RetrySource}",
            Details: r.RetryReason ?? $"retry of {r.OriginalStatus} trip",
            ActorId: r.RetriedBy,
            OccurredAt: r.OccurredAt,
            RelatedTripId: r.NewTripId,
            AttemptNumber: r.AttemptNumber)));

        // Sort descending so the most recent action is at the top in the UI.
        var sorted = entries.OrderByDescending(e => e.OccurredAt).ToList();

        return Result<FullOrderAuditDto>.Success(
            new FullOrderAuditDto(request.OrderId, sorted.Count, sorted));
    }
}
