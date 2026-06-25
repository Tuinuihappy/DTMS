using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Queries.GetMyProfile;

// GET /api/operator/me — operator app's "who am I" call. Used on login
// to render the profile drawer + decide which trip (if any) to show.
public record GetMyProfileQuery(Guid OperatorId) : IQuery<OperatorProfileDto>;

public record OperatorProfileDto(
    Guid Id,
    string EmployeeCode,
    string DisplayName,
    OperatorRole Role,
    OperatorStatus Status,
    Guid? PrimaryWarehouseId,
    Guid? CurrentTripId,
    string? Phone,
    string? ThumbnailUrl,
    DateTime CreatedAt,
    DateTime LastSyncedAt,
    IReadOnlyList<OperatorCertificationDto> Certifications,
    IReadOnlyList<OperatorPushSubscriptionDto> PushSubscriptions);

public record OperatorCertificationDto(
    Guid Id,
    CertificationType Type,
    DateTime IssuedAt,
    DateTime? ExpiresAt,
    bool IsActive);

public record OperatorPushSubscriptionDto(
    Guid Id,
    PushPlatform Platform,
    string Endpoint,
    string? DeviceLabel,
    DateTime SubscribedAt,
    DateTime? LastSucceededAt);
