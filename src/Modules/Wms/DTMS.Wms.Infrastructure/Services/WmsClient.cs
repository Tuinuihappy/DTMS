using System.Net.Http.Json;
using DTMS.Wms.Application.Services;

namespace DTMS.Wms.Infrastructure.Services;

/// <summary>
/// Thin HTTP wrapper over the WMS <c>GET /location</c> endpoint. Bearer
/// auth is applied by <see cref="WmsBearerTokenHandler"/> upstream in the
/// HttpClientFactory pipeline, so this class stays token-unaware.
///
/// Non-2xx / transport failures propagate as <see cref="HttpRequestException"/>
/// so the sync command can distinguish "WMS unreachable" (log warn +
/// preserve snapshot) from "WMS returned empty page" (legitimate end of
/// pagination).
/// </summary>
public sealed class WmsClient : IWmsClient
{
    private readonly HttpClient _http;

    public WmsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<WmsLocationPage> GetPageAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken ct = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "page must be >= 1");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be >= 1");

        // Build path — omit ?search= entirely when the caller didn't pass
        // one so the upstream default (unfiltered) applies.
        var uri = string.IsNullOrWhiteSpace(search)
            ? $"/location?page={page}&pageSize={pageSize}"
            : $"/location?search={Uri.EscapeDataString(search)}&page={page}&pageSize={pageSize}";

        var response = await _http.GetFromJsonAsync<WmsLocationPage>(uri, ct);

        // null body on 2xx is unusual but not fatal — treat as empty page
        // so the sync loop's page.Data.Count < pageSize check terminates
        // cleanly instead of NRE'ing.
        return response ?? new WmsLocationPage
        {
            Total = 0,
            Page = page,
            PageSize = pageSize,
            Data = Array.Empty<WmsLocationDto>(),
        };
    }
}
