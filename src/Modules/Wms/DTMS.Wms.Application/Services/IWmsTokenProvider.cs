namespace DTMS.Wms.Application.Services;

/// <summary>
/// Supplies the bearer token DTMS presents when calling the WMS API.
///
/// <para>The contract lives here, in the module that needs the token, so the
/// WMS module never takes a dependency on whatever stores it — same shape as
/// <see cref="Commands.SyncWmsLocations.IWmsSyncConfig"/>. The implementation
/// is wired in the composition root.</para>
///
/// <para>Returning null means "no managed token available" and is a normal
/// outcome, not an error: the caller falls back to the statically configured
/// token. That keeps a storage outage — or simply not having onboarded WMS
/// into managed credentials yet — from taking the integration down.</para>
/// </summary>
public interface IWmsTokenProvider
{
    Task<string?> GetTokenAsync(CancellationToken ct = default);
}
