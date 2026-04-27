using AMR.DeliveryPlanning.Planning.Application.Commands.ReplanJob;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

public class SlaRiskBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaRiskBackgroundService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RiskBuffer = TimeSpan.FromMinutes(10);

    public SlaRiskBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SlaRiskBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SlaRiskBackgroundService started (polling every {Interval}s)", PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAndReplanAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _logger.LogError(ex, "Error in SLA risk check"); }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task CheckAndReplanAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Find jobs whose SLA deadline is within the risk buffer window
        var cutoffTime = DateTime.UtcNow.Add(RiskBuffer);
        var atRiskJobs = await jobRepo.GetAtRiskJobsAsync(cutoffTime, cancellationToken);

        if (atRiskJobs.Count == 0) return;

        _logger.LogWarning("Found {Count} SLA-at-risk jobs — triggering replan", atRiskJobs.Count);

        foreach (var job in atRiskJobs)
        {
            var remaining = job.SlaDeadline!.Value - DateTime.UtcNow;
            _logger.LogWarning("Job {JobId} SLA risk: deadline={Deadline:u} remaining={Remaining}",
                job.Id, job.SlaDeadline, remaining);

            var result = await sender.Send(
                new ReplanJobCommand(job.Id, "SLA_RISK"),
                cancellationToken);

            if (!result.IsSuccess)
                _logger.LogWarning("Replan failed for job {JobId}: {Error}", job.Id, result.Error);
        }
    }
}
