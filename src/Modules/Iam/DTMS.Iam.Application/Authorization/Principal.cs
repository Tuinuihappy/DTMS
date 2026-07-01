namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Common surface across the two principal kinds DTMS authenticates.
/// Defined as an interface (not an abstract class) so a domain entity
/// can never accidentally implement the wrong half — each kind is its
/// own sealed record with the exact fields it needs.
/// </summary>
/// <remarks>
/// The principal lands in <c>HttpContext.Items["principal"]</c> after
/// auth runs (JwtBearerHandler for users, SystemClientAuthMiddleware
/// for systems). The ActorContext resolver in Program.cs probes that
/// slot to populate the PrincipalId + Type + Channel triple.
/// </remarks>
public interface IPrincipal
{
    /// <summary>Stable identifier prefixed with kind — <c>user:{EmployeeId}</c> or <c>system:{key}</c>.</summary>
    string PrincipalId { get; }

    /// <summary>Free-form human name for audit chips.</summary>
    string DisplayName { get; }
}

/// <summary>
/// A human caller — identified by EmployeeId on the JWT and routed
/// through the existing <c>JwtBearerHandler</c> +
/// <c>PermissionClaimsTransformer</c> pipeline.
/// </summary>
public sealed record UserPrincipal(string EmployeeId, string DisplayName) : IPrincipal
{
    public string PrincipalId => $"user:{EmployeeId}";
}

/// <summary>
/// A federated source system — identified by a short slug and resolved
/// by <c>SystemClientAuthMiddleware</c> from the inbound credential.
/// <see cref="Key"/> is the canonical id used in routes
/// (<c>/api/v1/source/*</c>) and audit logs.
/// </summary>
public sealed record SystemPrincipal(string Key, string DisplayName) : IPrincipal
{
    public string PrincipalId => $"system:{Key}";
}
