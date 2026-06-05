using System.Net.Http.Json;
using System.Text.Json;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

// Envelope-only RIOT3 client (Phase b7 removed per-task scheduling).
// Inbound: SendOrderAsync POSTs the multi-mission OrderTemplate envelope.
// Outbound state: vehicle queries via GetVehicleStateAsync; trip-level
// cancel/pause/resume go through SendOrderOperationAsync against the
// upperKey (the envelope key) — not individual tasks.
public class Riot3CommandService : IVehicleCommandService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Riot3CommandService> _logger;

    public Riot3CommandService(HttpClient httpClient, ILogger<Riot3CommandService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Multi-mission send used by the OrderTemplate instantiate flow.
    // Returns the orderKey RIOT3 minted for the new order on success.
    public async Task<Result<Riot3DispatchResult>> SendOrderAsync(
        Riot3OrderRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending RIOT3 order upperKey={UpperKey} with {Count} missions (structureType={Structure})",
            request.UpperKey, request.Missions.Count, request.StructureType);

        // Serialize ONCE and keep the JSON so the caller can snapshot
        // exactly what we transmitted — the same bytes go on the wire
        // and into Trip.VendorRequestSnapshot for forensic queries.
        string requestJson;
        try
        {
            requestJson = JsonSerializer.Serialize(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize RIOT3 order upperKey={UpperKey}", request.UpperKey);
            return Result<Riot3DispatchResult>.Failure($"RIOT3 request serialization failed: {ex.Message}");
        }

        try
        {
            using var content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/api/v4/orders", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            // RIOT3 returns HTTP 200 even when the order is rejected (e.g. station
            // not on road network → code "E100006"). The success signal is in the
            // response body: code == "0". Anything else is a vendor-side rejection
            // and must surface as a Failure so the caller doesn't create a Trip
            // for an order the vendor never actually accepted.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            if (payload?.Code != "0")
            {
                var msg = payload?.Message ?? "(no message)";
                _logger.LogWarning("RIOT3 rejected order upperKey={UpperKey}: code={Code} message={Message}",
                    request.UpperKey, payload?.Code ?? "(null)", msg);
                return Result<Riot3DispatchResult>.Failure($"RIOT3 rejected order (code {payload?.Code ?? "null"}): {msg}");
            }

            var orderKey = payload.Data?.OrderKey ?? string.Empty;
            _logger.LogInformation("RIOT3 accepted order upperKey={UpperKey} orderKey={OrderKey}",
                request.UpperKey, orderKey);
            return Result<Riot3DispatchResult>.Success(new Riot3DispatchResult(orderKey, requestJson));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to send RIOT3 order upperKey={UpperKey}", request.UpperKey);
            return Result<Riot3DispatchResult>.Failure($"RIOT3 API error: {ex.Message}");
        }
    }

    // Envelope-level operation (cancel / pause / resume an entire upperKey).
    public Task<Result> CancelEnvelopeAsync(string upperKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(upperKey, Riot3OrderCommandType.Cancel, "cancel", cancellationToken);

    public Task<Result> PauseEnvelopeAsync(string upperKey, CancellationToken cancellationToken = default)
        => SendOrderOperationAsync(upperKey, Riot3OrderCommandType.Hold, "pause", cancellationToken);

    public Task<Result> ResumeEnvelopeAsync(string upperKey, CancellationToken cancellationToken = default)
        // CMD_ORDER_CONTINUE_FROM_HELD pairs with CMD_ORDER_HELD; HANG-from-* is for system-initiated hangs.
        => SendOrderOperationAsync(upperKey, Riot3OrderCommandType.ContinueFromHeld, "resume", cancellationToken);

    public async Task<StandardRobotState?> GetVehicleStateAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<Riot3VehicleResponse>(
                $"/api/v4/robots/{vehicleId}",
                cancellationToken);

            if (response == null) return null;

            return new StandardRobotState
            {
                VehicleId = vehicleId,
                State = MapSystemState(response.SystemState),
                BatteryLevel = response.BatteryLevel / 100.0,
                CurrentX = response.Position?.X,
                CurrentY = response.Position?.Y,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to get vehicle state for {VehicleId} from RIOT3", vehicleId);
            return null;
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<Result> SendOrderOperationAsync(
        string upperKey,
        string orderCommandType,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("RIOT3 envelope operation {Operation} ({Command}) for upperKey {UpperKey}",
            operationLabel, orderCommandType, upperKey);

        var envelope = new Riot3OrderOperationEnvelope
        {
            OrderCommand = new Riot3OrderOperationRequest
            {
                OrderCommandType = orderCommandType,
                DisableVehicle = false
            }
        };

        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"/api/v4/orders/{Uri.EscapeDataString(upperKey)}/operation?isUpper=true",
                envelope,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            // Same body-level check as SendOrderAsync: RIOT3 returns HTTP 200
            // even on logical failures (e.g. order already finished, vendor
            // can't pause). Code "0" means accepted.
            var payload = await response.Content.ReadFromJsonAsync<Riot3CreateOrderResponse>(cancellationToken);
            if (payload?.Code != "0")
            {
                var msg = payload?.Message ?? "(no message)";
                _logger.LogWarning("RIOT3 rejected {Operation} on upperKey {UpperKey}: code={Code} message={Message}",
                    operationLabel, upperKey, payload?.Code ?? "(null)", msg);
                return Result.Failure($"RIOT3 rejected {operationLabel} (code {payload?.Code ?? "null"}): {msg}");
            }
            return Result.Success();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to {Operation} RIOT3 envelope {UpperKey}", operationLabel, upperKey);
            return Result.Failure($"RIOT3 {operationLabel} error: {ex.Message}");
        }
    }

    private static StandardState MapSystemState(string systemState) => systemState?.ToUpperInvariant() switch
    {
        "IDLE" => StandardState.Idle,
        "BUSY" => StandardState.Moving,
        "ERROR" => StandardState.Error,
        "CHARGING" => StandardState.Charging,
        "MAINTENANCE" => StandardState.Maintenance,
        _ => StandardState.Offline
    };
}
