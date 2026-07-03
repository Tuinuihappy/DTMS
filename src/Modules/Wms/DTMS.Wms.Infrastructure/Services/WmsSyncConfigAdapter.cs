using DTMS.Wms.Application.Commands.SyncWmsLocations;
using Microsoft.Extensions.Options;

namespace DTMS.Wms.Infrastructure.Services;

/// <summary>
/// Bridges <see cref="WmsOptions"/> (Infrastructure) onto
/// <see cref="IWmsSyncConfig"/> (Application). Uses IOptionsMonitor so
/// live config reloads flow through without a restart.
/// </summary>
public sealed class WmsSyncConfigAdapter : IWmsSyncConfig
{
    private readonly IOptionsMonitor<WmsOptions> _options;

    public WmsSyncConfigAdapter(IOptionsMonitor<WmsOptions> options)
    {
        _options = options;
    }

    public int PageSize => _options.CurrentValue.PageSize;
    public int MaxRowsPerCycle => _options.CurrentValue.MaxRowsPerCycle;
}
