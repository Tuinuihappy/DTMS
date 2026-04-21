using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AMR.DeliveryPlanning.DeliveryOrder.Infrastructure.Repositories;

public class DeliveryOrderRepository : IDeliveryOrderRepository
{
    private readonly DeliveryOrderDbContext _context;

    public DeliveryOrderRepository(DeliveryOrderDbContext context)
    {
        _context = context;
    }

    public async Task<Domain.Entities.DeliveryOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DeliveryOrders
            .Include(o => o.OrderLines)
            .Include(o => o.Schedule)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task AddAsync(Domain.Entities.DeliveryOrder order, CancellationToken cancellationToken = default)
    {
        await _context.DeliveryOrders.AddAsync(order, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Domain.Entities.DeliveryOrder order, CancellationToken cancellationToken = default)
    {
        _context.DeliveryOrders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
