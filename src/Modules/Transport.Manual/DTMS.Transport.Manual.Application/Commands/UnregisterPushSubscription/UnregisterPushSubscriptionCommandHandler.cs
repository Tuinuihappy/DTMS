using DTMS.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Repositories;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.UnregisterPushSubscription;

internal sealed class UnregisterPushSubscriptionCommandHandler : ICommandHandler<UnregisterPushSubscriptionCommand>
{
    private readonly IOperatorRepository _operators;
    public UnregisterPushSubscriptionCommandHandler(IOperatorRepository operators) => _operators = operators;

    public async Task<Result> Handle(UnregisterPushSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var op = await _operators.GetByIdWithDetailsAsync(request.OperatorId, cancellationToken);
        if (op is null)
            return Result.Failure($"Operator {request.OperatorId} not found.");

        op.RemovePushSubscription(request.Endpoint);
        await _operators.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
