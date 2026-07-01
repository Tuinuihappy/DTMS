namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.8 — validates inbound system access tokens that DTMS minted
/// itself via <see cref="ISystemJwtIssuer"/>. Used by
/// <c>SystemClientAuthMiddleware</c>'s bearer-jwt branch.
///
/// <para>Symmetric with <see cref="ISystemJwtIssuer"/>: both classes use
/// the same <see cref="SystemJwtIssuerOptions"/> (issuer/audience/key
/// material) so a token minted here is automatically accepted there with
/// no separate config to keep in sync.</para>
/// </summary>
public interface ISystemJwtValidator
{
    /// <summary>
    /// Validate signature + iss + aud + exp on the supplied raw JWT.
    /// On success, returns the system key extracted from the <c>sub</c>
    /// claim (the <c>system:</c> prefix stripped). On failure, returns a
    /// short reason suitable for ops triage — exception details are
    /// already logged inside the validator.
    /// </summary>
    SystemJwtValidationResult Validate(string token);
}

public sealed record SystemJwtValidationResult(
    bool IsValid,
    string? SystemKey,
    string? FailureReason);
