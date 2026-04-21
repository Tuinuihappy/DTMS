using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.SharedKernel.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        _logger.LogInformation("Handling Command/Query {RequestName} ({@Request})", requestName, request);

        var timer = new Stopwatch();
        timer.Start();

        var response = await next();

        timer.Stop();

        _logger.LogInformation("Handled Command/Query {RequestName} in {ElapsedMilliseconds} ms", requestName, timer.ElapsedMilliseconds);

        return response;
    }
}
