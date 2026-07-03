namespace DTMS.Wms.Application.Services;

/// <summary>
/// Typed HTTP contract for the external WMS. One method per external
/// endpoint — the sync command pages through until it has seen every
/// record. Transport / non-2xx failures propagate so callers can
/// distinguish "WMS unreachable" from "WMS returned zero records"
/// (which would trigger soft-delete of the whole catalogue).
/// </summary>
public interface IWmsClient
{
    /// <summary>
    /// Fetch one page of locations. Pass <paramref name="search"/> to
    /// filter server-side (WMS matches loosely on locationCode/displayName).
    /// </summary>
    Task<WmsLocationPage> GetPageAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken ct = default);
}
