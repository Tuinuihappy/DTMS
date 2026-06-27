using DTMS.Planning.Application.Queries.GetJobById;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetJobsByOrder;

/// <summary>
/// Phase b10 — list every Job that belongs to a delivery order, sorted by
/// GroupIndex asc so the operator UI mirrors the consumer's dispatch loop.
/// Returns an empty list (not 404) when the order has no Jobs — legacy
/// pre-b8 orders, or orders that haven't been confirmed yet.
/// </summary>
public record GetJobsByOrderQuery(Guid OrderId) : IQuery<List<JobDto>>;
