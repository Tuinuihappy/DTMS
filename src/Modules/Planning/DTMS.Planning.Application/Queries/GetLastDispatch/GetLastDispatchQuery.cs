using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Queries.GetLastDispatch;

/// <summary>
/// Latest manual dispatch attempt for a template. Purely informational — the
/// dispatch dialog shows it so an operator who is unsure whether their last
/// click landed can check instead of firing a second robot order. Returns null
/// data when the template has never been dispatched.
/// </summary>
public record GetLastDispatchQuery(Guid OrderTemplateId) : IQuery<LastDispatchDto?>;

public record LastDispatchDto(
    string Status,
    string UpperKey,
    string? VendorOrderKey,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public class GetLastDispatchQueryHandler : IQueryHandler<GetLastDispatchQuery, LastDispatchDto?>
{
    private readonly IDispatchClaimRepository _claims;

    public GetLastDispatchQueryHandler(IDispatchClaimRepository claims) => _claims = claims;

    public async Task<Result<LastDispatchDto?>> Handle(
        GetLastDispatchQuery request,
        CancellationToken cancellationToken)
    {
        var claim = await _claims.GetLatestByTemplateAsync(request.OrderTemplateId, cancellationToken);
        if (claim is null)
            return Result<LastDispatchDto?>.Success(null);

        return Result<LastDispatchDto?>.Success(new LastDispatchDto(
            claim.Status.ToString(),
            claim.UpperKey,
            claim.VendorOrderKey,
            claim.CreatedAt,
            claim.CompletedAt));
    }
}
