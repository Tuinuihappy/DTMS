using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.DeliveryOrder.Infrastructure.Repositories;

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
