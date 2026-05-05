using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Repositories;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly DeliveryOrderDbContext _context;
    public DeliveryOrderRepository(DeliveryOrderDbContext context) => _context = context;

    public Task<Domain.Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders
            .Include(o => o.Legs).ThenInclude(l => l.OrderItems)
            .Include(o => o.Schedule)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Domain.Entities.DeliveryOrder?> GetByOrderNoAsync(string orderNo, CancellationToken cancellationToken = default)
        => _context.DeliveryOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo, cancellationToken);

    public async Task<List<Domain.Entities.DeliveryOrder>> GetByStatusAsync(OrderStatus status, int page, int pageSize, CancellationToken cancellationToken = default)
        => await _context.DeliveryOrders
            .Where(o => o.Status == status)
            .OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<List<Domain.Entities.DeliveryOrder>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        => await _context.DeliveryOrders
            .OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Domain.Entities.DeliveryOrder order, CancellationToken cancellationToken = default)
    {
        await _context.DeliveryOrders.AddAsync(order, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<Domain.Entities.DeliveryOrder> orders, CancellationToken cancellationToken = default)
        => await _context.DeliveryOrders.AddRangeAsync(orders, cancellationToken);

    public Task UpdateAsync(Domain.Entities.DeliveryOrder order, CancellationToken cancellationToken = default)
    {
        _context.DeliveryOrders.Update(order);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}

public class OrderAmendmentRepository : IOrderAmendmentRepository
{
    private readonly DeliveryOrderDbContext _context;
    public OrderAmendmentRepository(DeliveryOrderDbContext context) => _context = context;

    public async Task AddAsync(OrderAmendment amendment, CancellationToken cancellationToken = default)
        => await _context.OrderAmendments.AddAsync(amendment, cancellationToken);

    public Task<List<OrderAmendment>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => _context.OrderAmendments
            .Where(a => a.DeliveryOrderId == orderId)
            .OrderBy(a => a.AmendedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}

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
