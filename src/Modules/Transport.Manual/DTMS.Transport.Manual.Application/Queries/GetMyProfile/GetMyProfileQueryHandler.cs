using DTMS.SharedKernel.Messaging;
using DTMS.Transport.Manual.Domain.Repositories;

namespace DTMS.Transport.Manual.Application.Queries.GetMyProfile;

internal sealed class GetMyProfileQueryHandler : IQueryHandler<GetMyProfileQuery, OperatorProfileDto>
{
    private readonly IOperatorRepository _operators;

    public GetMyProfileQueryHandler(IOperatorRepository operators) => _operators = operators;

    public async Task<Result<OperatorProfileDto>> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var op = await _operators.GetByIdWithDetailsAsync(request.OperatorId, cancellationToken);
        if (op is null)
            return Result<OperatorProfileDto>.Failure($"Operator {request.OperatorId} not found.");

        var dto = new OperatorProfileDto(
            Id: op.Id,
            EmployeeCode: op.EmployeeCode,
            DisplayName: op.DisplayName,
            Role: op.Role,
            Status: op.Status,
            PrimaryWarehouseId: op.PrimaryWarehouseId,
            CurrentTripId: op.CurrentTripId,
            Phone: op.Phone,
            ThumbnailUrl: op.ThumbnailUrl,
            CreatedAt: op.CreatedAt,
            LastSyncedAt: op.LastSyncedAt,
            Certifications: op.Certifications.Select(c => new OperatorCertificationDto(
                c.Id, c.Type, c.IssuedAt, c.ExpiresAt, c.IsActive)).ToList(),
            PushSubscriptions: op.PushSubscriptions.Select(s => new OperatorPushSubscriptionDto(
                s.Id, s.Platform, s.Endpoint, s.DeviceLabel, s.SubscribedAt, s.LastSucceededAt)).ToList());
        return Result<OperatorProfileDto>.Success(dto);
    }
}
