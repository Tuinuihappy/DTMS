using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Repositories;

public class OrderAuditEventRepository : IOrderAuditEventRepository
{
    private readonly DeliveryOrderDbContext _context;
    public OrderAuditEventRepository(DeliveryOrderDbContext context) => _context = context;

    public async Task AddAsync(OrderAuditEvent auditEvent, CancellationToken cancellationToken = default)
        => await _context.OrderAuditEvents.AddAsync(auditEvent, cancellationToken);

    public Task<List<OrderAuditEvent>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => _context.OrderAuditEvents
            .Where(e => e.DeliveryOrderId == orderId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
