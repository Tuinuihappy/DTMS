using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateMilkRun;

public class CreateMilkRunCommandHandler : ICommandHandler<CreateMilkRunCommand, Guid>
{
    private readonly IJobRepository _jobRepository;
    private readonly IRouteCostCalculator _costCalc;
    private readonly ITenantContext _tenantContext;

    public CreateMilkRunCommandHandler(IJobRepository jobRepository, IRouteCostCalculator costCalc, ITenantContext tenantContext)
    {
        _jobRepository = jobRepository;
        _costCalc = costCalc;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(CreateMilkRunCommand request, CancellationToken cancellationToken)
    {
        if (request.Stops.Count < 2)
            return Result<Guid>.Failure("Milk-run requires at least 2 stops.");

        // Save the template
        var template = new MilkRunTemplate(request.TemplateName, request.CronSchedule);
        foreach (var stop in request.Stops.OrderBy(s => s.SequenceOrder))
        {
            template.AddStop(
                stop.StationId,
                stop.SequenceOrder,
                stop.ArrivalOffsetMinutes.HasValue ? TimeSpan.FromMinutes(stop.ArrivalOffsetMinutes.Value) : null,
                TimeSpan.FromMinutes(stop.DwellMinutes));
        }

        await _jobRepository.AddMilkRunTemplateAsync(template, cancellationToken);

        // Create an initial Job from the template
        var job = new Job(_tenantContext.TenantId, Guid.Empty, request.Priority);
        job.SetPattern(PatternType.MilkRun);

        var orderedStops = request.Stops.OrderBy(s => s.SequenceOrder).ToList();
        for (int i = 0; i < orderedStops.Count - 1; i++)
        {
            var from = orderedStops[i].StationId;
            var to = orderedStops[i + 1].StationId;
            var cost = await _costCalc.CalculateCostAsync(from, to, cancellationToken);
            var leg = job.AddLeg(from, to, i + 1, cost);

            // First stop is pickup, intermediate are pickup+drop, last is drop
            if (i == 0)
                leg.AddStop(from, StopType.Pickup, 1);
            leg.AddStop(to, i == orderedStops.Count - 2 ? StopType.Drop : StopType.Pickup, 2);
        }

        await _jobRepository.AddAsync(job, cancellationToken);

        return Result<Guid>.Success(template.Id);
    }
}
