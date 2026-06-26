using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Commands.RegisterPushSubscription;

internal sealed class RegisterPushSubscriptionCommandHandler : ICommandHandler<RegisterPushSubscriptionCommand>
{
    private readonly IOperatorRepository _operators;

    public RegisterPushSubscriptionCommandHandler(IOperatorRepository operators) => _operators = operators;

    public async Task<Result> Handle(RegisterPushSubscriptionCommand request, CancellationToken cancellationToken)
    {
        // Need the operator WITH PushSubscriptions loaded so EF tracks
        // the collection mutation. Plain GetById would attach a fresh
        // entity with empty subscriptions and silently double-insert.
        var op = await _operators.GetByIdWithDetailsAsync(request.OperatorId, cancellationToken);
        if (op is null)
            return Result.Failure($"Operator {request.OperatorId} not found.");

        op.RegisterPushSubscription(
            platform: request.Platform,
            endpoint: request.Endpoint,
            publicKey: request.PublicKey,
            authSecret: request.AuthSecret,
            deviceLabel: request.DeviceLabel);
        await _operators.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
