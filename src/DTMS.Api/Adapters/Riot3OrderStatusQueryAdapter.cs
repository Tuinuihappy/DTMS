using DTMS.Planning.Application.Services;
using DTMS.Transport.Amr.Services;

namespace DTMS.Api.Adapters;

// Composition-root bridge: answers Planning's vendor-agnostic
// IRobotOrderStatusQuery using the RIOT3 order query service. Lives in the
// API project so Planning.Application never references Transport.Amr
// (module boundary enforced by DTMS.ArchitectureTests) — same arrangement
// as Riot3OrderDispatcherAdapter.
internal sealed class Riot3OrderStatusQueryAdapter : IRobotOrderStatusQuery
{
    private readonly IRiot3OrderQueryService _riot3;
    private readonly ILogger<Riot3OrderStatusQueryAdapter> _logger;

    public Riot3OrderStatusQueryAdapter(
        IRiot3OrderQueryService riot3,
        ILogger<Riot3OrderStatusQueryAdapter> logger)
    {
        _riot3 = riot3;
        _logger = logger;
    }

    public async Task<RobotOrderPresence> CheckAsync(
        string upperKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // GetRawByUpperKeyAsync returns null for 404 / "order is empty",
            // and throws when RIOT3 can't be reached. Those two must stay
            // distinct: a failed lookup reported as NotFound would let a
            // timed-out dispatch retry into a duplicate robot order.
            var raw = await _riot3.GetRawByUpperKeyAsync(upperKey, cancellationToken);
            return raw is null ? RobotOrderPresence.NotFound : RobotOrderPresence.Exists;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RIOT3 status lookup failed for upperKey {UpperKey} — reporting Unknown so the caller stays conservative.",
                upperKey);
            return RobotOrderPresence.Unknown;
        }
    }
}
