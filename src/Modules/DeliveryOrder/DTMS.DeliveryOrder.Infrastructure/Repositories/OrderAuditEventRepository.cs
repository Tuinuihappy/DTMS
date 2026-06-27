using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Repositories;

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
